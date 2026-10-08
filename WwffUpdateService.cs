using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace PotaActivationTool
{
    // Converts the KFF-POTA cross reference spreadsheet published by WWFF/KFF into
    // the simple KffCrossReference.csv file this app reads. The "POTA to KFF" tab in
    // that spreadsheet is messy in a few specific ways, all handled here:
    //   - Some rows have ">>>>>>" in the WWFF ID column - that's just a "see the
    //     rows below" marker, not a real value, and gets skipped.
    //   - Some rows have a blank WWFF ID column, or the literal word "None" - both
    //     mean "this park has no KFF reference."
    //   - Parks that span multiple states can have several rows, one KFF ID per
    //     state (or, for a few National Forests, one KFF ID per sub-unit). The
    //     state/sub-unit name is only findable in the Name column, e.g.
    //     "Cumberland Gap (KY)" - so all of a park's real rows get grouped
    //     together and combined into one field like:
    //     "KFF-0019 (KY); KFF-4586 (TN); KFF-4587 (VA)"
    //   - A handful of KFF IDs in WWFF's own spreadsheet are typo'd as "KFF 1234"
    //     or "KFF1234" instead of "KFF-1234" - these get normalized.
    public static class WwffUpdateService
    {
        private const string InfoFileName = "KffCrossReference.info.txt";
        private const string LastCheckFileName = "KffCrossReference.lastcheck.txt";

        public class UpdateResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int TotalMappings { get; set; }
            public DateTime? SourceDate { get; set; }
        }

        // Reads the given .xls/.xlsx workbook, finds its "POTA to KFF" tab, and
        // writes a cleaned-up KffCrossReference.csv to outputCsvPath. When
        // outputNamesCsvPath is given, also writes a second, small
        // "KFF,Name" file mapping each individual KFF reference to its own
        // name as recorded in the spreadsheet (e.g. "Cumberland Gap (KY)") -
        // kept separate because a multi-state park's several KFF numbers each
        // have their own name, not one shared with the POTA park's own name.
        public static UpdateResult ConvertXlsToCsv(string sourceFilePath, string outputCsvPath, string? outputNamesCsvPath = null)
        {
            var result = new UpdateResult();
            try
            {
                DataTable? table;
                using (var stream = File.Open(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = ExcelReaderFactory.CreateReader(stream))
                {
                    var dataSet = reader.AsDataSet();
                    table = FindPotaToKffTable(dataSet);
                }

                if (table == null)
                {
                    result.Success = false;
                    result.Message = "Could not find a \"POTA to KFF\" tab in this file. " +
                                      "Make sure you selected the KFF-POTA cross reference workbook from wwff.us.";
                    return result;
                }

                var groups = new Dictionary<string, List<(string Kff, string Name)>>(StringComparer.OrdinalIgnoreCase);

                for (int r = 1; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];
                    string pota = GetCell(row, 0).Trim();
                    string kff = GetCell(row, 1).Trim();
                    string name = GetCell(row, 2).Trim();

                    if (pota == "") continue;

                    if (!groups.TryGetValue(pota, out var list))
                    {
                        list = new List<(string, string)>();
                        groups[pota] = list;
                    }
                    list.Add((kff, name));
                }

                var lines = new List<string> { "POTA,KFF" };
                var kffNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int mappingCount = 0;

                foreach (var pota in groups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var real = new List<(string Kff, string Name)>();
                    foreach (var (kffRaw, name) in groups[pota])
                    {
                        string kff = kffRaw.Trim();
                        if (kff == "" || kff.Equals("none", StringComparison.OrdinalIgnoreCase) || kff.StartsWith(">>>"))
                            continue;

                        real.Add((NormalizeKff(kff), name));
                    }

                    if (real.Count == 0) continue;

                    foreach (var entry in real)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Name))
                            kffNames[entry.Kff] = entry.Name;
                    }

                    string kffField = real.Count == 1
                        ? real[0].Kff
                        : string.Join("; ", real.Select(entry => $"{entry.Kff} ({GetLabel(entry.Name)})"));

                    lines.Add($"{CsvField(pota)},{CsvField(kffField)}");
                    mappingCount++;
                }

                File.WriteAllLines(outputCsvPath, lines, Encoding.UTF8);

                if (outputNamesCsvPath != null)
                {
                    var nameLines = new List<string> { "KFF,Name" };
                    foreach (var kv in kffNames.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        nameLines.Add($"{CsvField(kv.Key)},{CsvField(kv.Value)}");
                    File.WriteAllLines(outputNamesCsvPath, nameLines, Encoding.UTF8);
                }

                result.Success = true;
                result.TotalMappings = mappingCount;
                result.SourceDate = ExtractDateFromFileName(Path.GetFileName(sourceFilePath))
                                     ?? File.GetLastWriteTime(sourceFilePath).Date;
                result.Message = $"Updated - {mappingCount} POTA-to-KFF mappings loaded.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "Could not read that file: " + ex.Message;
            }

            return result;
        }

        private static DataTable? FindPotaToKffTable(DataSet dataSet)
        {
            foreach (DataTable t in dataSet.Tables)
            {
                if (t.TableName.IndexOf("POTA to KFF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.TableName.IndexOf("POTA-KFF", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.TableName.IndexOf("POTA to WWFF", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return t;
                }
            }
            return null;
        }

        private static string GetCell(DataRow row, int index)
        {
            if (index >= row.Table.Columns.Count) return "";
            var value = row[index];
            return value == null || value == DBNull.Value ? "" : value.ToString() ?? "";
        }

        private static readonly Regex KffTypoRegex = new Regex(@"^KFF[\s-]*(\d+)$", RegexOptions.IgnoreCase);

        // Fixes "KFF 1234" or "KFF1234" typos (missing/wrong hyphen) into
        // "KFF-1234". Leaves anything else - like a UM-#### reference, which is a
        // different, legitimately-formatted WWFF-affiliate prefix - untouched.
        private static string NormalizeKff(string value)
        {
            var m = KffTypoRegex.Match(value);
            return m.Success ? "KFF-" + m.Groups[1].Value : value;
        }

        private static readonly Regex LabelParenRegex = new Regex(@"\(([^)]+)\)\s*$");
        private static readonly Regex LabelOpenParenRegex = new Regex(@"\(([^()]*)$");

        // Pulls the distinguishing label out of a multi-entry park's Name column,
        // e.g. "Cumberland Gap (KY)" -> "KY". A few National Forest entries don't
        // use a "(STATE)" suffix at all - they use the sub-unit's own name instead,
        // like "Bridger" / "Teton" for Bridger-Teton National Forest - so if there's
        // no parenthetical to pull out, the whole name is used as the label.
        private static string GetLabel(string name)
        {
            var m = LabelParenRegex.Match(name);
            if (m.Success) return m.Groups[1].Value.Trim();

            m = LabelOpenParenRegex.Match(name);
            if (m.Success && m.Groups[1].Value.Trim() != "") return m.Groups[1].Value.Trim();

            return name.Trim();
        }

        private static string CsvField(string field)
        {
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
                return "\"" + field.Replace("\"", "\"\"") + "\"";
            return field;
        }

        // Tries to read the date out of the source file's own name. WWFF has used
        // a couple of different naming schemes over time:
        //   kff_pota_cross_reference_2025_1_10.xls   -> January 10, 2025
        //   KFF-POTA-CTY-X-Ref-28Jul2026.xls          -> July 28, 2026
        // Returns null if neither pattern matches, so the caller can fall back to
        // something else (like the file's own saved date).
        public static DateTime? ExtractDateFromFileName(string fileName)
        {
            var m = Regex.Match(fileName, @"(\d{4})_(\d{1,2})_(\d{1,2})");
            if (m.Success)
            {
                try
                {
                    return new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Fall through and try the other pattern.
                }
            }

            m = Regex.Match(fileName, @"(\d{1,2})([A-Za-z]{3})(\d{4})");
            if (m.Success)
            {
                string dateText = $"{m.Groups[1].Value} {m.Groups[2].Value} {m.Groups[3].Value}";
                if (DateTime.TryParseExact(dateText, "d MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    return parsed;
            }

            return null;
        }

        // Remembers the source date across app restarts by writing it to a tiny
        // text file next to KffCrossReference.csv.
        public static void SaveInfoFile(string folder, DateTime? sourceDate)
        {
            try
            {
                string path = Path.Combine(folder, InfoFileName);
                string content = sourceDate.HasValue ? sourceDate.Value.ToString("yyyy-MM-dd") : "";
                File.WriteAllText(path, content);
            }
            catch
            {
                // Not critical - the date just won't be remembered next time the app starts.
            }
        }

        public static DateTime? LoadInfoFile(string folder)
        {
            try
            {
                string path = Path.Combine(folder, InfoFileName);
                if (!File.Exists(path)) return null;

                string content = File.ReadAllText(path).Trim();
                if (DateTime.TryParseExact(content, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d;
            }
            catch
            {
                // Ignore - just means the date won't show until the next update.
            }
            return null;
        }

        // Tracks when we last *attempted* an auto-update check - separate from
        // SaveInfoFile/LoadInfoFile above, which track the date WWFF published
        // the data we're using. This one is what EnsureWwffDataAsync uses to
        // decide whether it's time to check again, regardless of whether the
        // last check found anything new.
        public static void SaveLastCheckedTime(string folder, DateTime utcTime)
        {
            try
            {
                string path = Path.Combine(folder, LastCheckFileName);
                File.WriteAllText(path, utcTime.ToString("o"));
            }
            catch
            {
                // Not critical - worst case, the next check just happens sooner than strictly necessary.
            }
        }

        public static DateTime? LoadLastCheckedTime(string folder)
        {
            try
            {
                string path = Path.Combine(folder, LastCheckFileName);
                if (!File.Exists(path)) return null;

                string content = File.ReadAllText(path).Trim();
                if (DateTime.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d))
                    return d;
            }
            catch
            {
                // Ignore - treated the same as "never checked."
            }
            return null;
        }
    }
}
