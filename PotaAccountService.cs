using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PotaActivationTool
{
    public class PotaNotSignedInException : Exception
    {
        public PotaNotSignedInException() : base("Not signed in to POTA.") { }
    }

    // Signed in, but with a login that has no POTA account behind it - e.g.
    // "Continue with Google" when the POTA account was made with an email and
    // password. pota.app treats those as two different logins, and its own
    // site sends such a login to its sign-up page. Seen 2026-10-08.
    public class PotaNoAccountException : Exception
    {
        public PotaNoAccountException() : base("This login has no POTA account.") { }
    }

    // Signing in to pota.app, so the map's rove planner can load the rove
    // plans saved there. pota.app has no published API for other programs;
    // this works the same way pota.app's own site does:
    //  - sign-in happens on POTA's own login page (Amazon Cognito's hosted
    //    login, shown in PotaLoginForm), so this program never sees the
    //    password, and Google/Apple sign-in work if the account uses them
    //  - that page hands back a one-time code at pota.app's own address,
    //    which PotaLoginForm catches before the page loads and trades here
    //    for login tokens (OAuth 2 authorization code with PKCE)
    //  - api.pota.app calls carry the ID token as the Authorization header.
    // The refresh token (good for weeks) is kept encrypted for this Windows
    // user (DPAPI) in the app data folder, so signing in is rarely needed.
    public static class PotaAccountService
    {
        // From pota.app's own sign-in setup (next.pota.app's Amplify config).
        private const string ClientId = "7hluqct0n2nckib7i7sd5753oa";
        private const string AuthDomain = "https://parksontheair.auth.us-east-2.amazoncognito.com";
        public const string RedirectUri = "https://next.pota.app/";
        private const string RoveApi = "https://api.pota.app/v1/rove";
        private const string LoginFileName = "PotaLogin.dat";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("POTA-Activation-Tool");
            return client;
        }

        private static readonly object Gate = new();
        private static bool _loaded;
        private static string? _refreshToken;
        private static string? _idToken;
        private static DateTime _idTokenExpiresUtc;
        private static string _callsign = "";

        public static bool IsSignedIn(string appDataFolder)
        {
            EnsureLoaded(appDataFolder);
            lock (Gate) return _refreshToken != null;
        }

        public static string Callsign(string appDataFolder)
        {
            EnsureLoaded(appDataFolder);
            lock (Gate) return _callsign;
        }

        // ---- Sign-in

        public record SignInRequest(string Url, string CodeVerifier, string State);

        public static SignInRequest BeginSignIn()
        {
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = Base64Url(RandomNumberGenerator.GetBytes(16));
            string url = AuthDomain + "/oauth2/authorize" +
                "?response_type=code" +
                "&client_id=" + ClientId +
                "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                "&scope=" + Uri.EscapeDataString("openid email profile") +
                "&state=" + state +
                "&code_challenge=" + challenge +
                "&code_challenge_method=S256";
            return new SignInRequest(url, verifier, state);
        }

        // Trades the login page's one-time code for tokens and saves them.
        public static async Task CompleteSignInAsync(string appDataFolder, string code, string codeVerifier)
        {
            var tokens = await PostTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = codeVerifier
            });
            string? refresh = tokens["refresh_token"]?.GetValue<string>();
            if (refresh == null) throw new InvalidDataException("POTA's login didn't return a refresh token.");
            lock (Gate)
            {
                _loaded = true;
                _refreshToken = refresh;
                SetIdToken(tokens);
                Save(appDataFolder);
            }
        }

        public static void SignOut(string appDataFolder)
        {
            lock (Gate)
            {
                _loaded = true;
                _refreshToken = null;
                _idToken = null;
                _callsign = "";
                try { File.Delete(Path.Combine(appDataFolder, LoginFileName)); } catch { /* already gone */ }
            }
        }

        // A current ID token, refreshed when it's about to run out (they last
        // an hour). Throws PotaNotSignedInException when not signed in, or
        // when the saved sign-in has expired or been revoked, and
        // HttpRequestException when POTA can't be reached.
        private static async Task<string> GetIdTokenAsync(string appDataFolder)
        {
            EnsureLoaded(appDataFolder);
            string refresh;
            lock (Gate)
            {
                if (_refreshToken == null) throw new PotaNotSignedInException();
                if (_idToken != null && DateTime.UtcNow < _idTokenExpiresUtc.AddMinutes(-2)) return _idToken;
                refresh = _refreshToken;
            }

            JsonNode tokens;
            try
            {
                tokens = await PostTokenAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = ClientId,
                    ["refresh_token"] = refresh
                });
            }
            catch (InvalidDataException)
            {
                // invalid_grant: the saved sign-in expired or was signed out
                // elsewhere - sign in again.
                SignOut(appDataFolder);
                throw new PotaNotSignedInException();
            }
            lock (Gate)
            {
                SetIdToken(tokens);
                Save(appDataFolder);
                return _idToken!;
            }
        }

        // Throws InvalidDataException when Cognito refuses (bad or expired
        // code/refresh token), HttpRequestException when it can't be reached.
        private static async Task<JsonNode> PostTokenAsync(Dictionary<string, string> form)
        {
            using var response = await Http.PostAsync(AuthDomain + "/oauth2/token", new FormUrlEncodedContent(form));
            string body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                string error = "";
                try { error = JsonNode.Parse(body)?["error"]?.GetValue<string>() ?? ""; } catch { }
                throw new InvalidDataException("POTA's login refused the request" + (error.Length > 0 ? " (" + error + ")" : "") + ".");
            }
            return JsonNode.Parse(body) ?? throw new InvalidDataException("Empty answer from POTA's login.");
        }

        // Caller holds Gate.
        private static void SetIdToken(JsonNode tokens)
        {
            _idToken = tokens["id_token"]?.GetValue<string>() ?? throw new InvalidDataException("POTA's login didn't return an ID token.");
            int expiresIn = tokens["expires_in"]?.GetValue<int>() ?? 3600;
            _idTokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            string callsign = ReadTokenClaim(_idToken, "pota:callsign");
            if (callsign.Length > 0) _callsign = callsign;
        }

        // A claim from the token's payload - only read for display (the
        // callsign), never trusted for anything.
        private static string ReadTokenClaim(string jwt, string claim)
        {
            try
            {
                string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                return JsonNode.Parse(Convert.FromBase64String(payload))?[claim]?.GetValue<string>() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool HasClaim(string jwt, string claim)
        {
            try
            {
                string payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                return JsonNode.Parse(Convert.FromBase64String(payload))?.AsObject().ContainsKey(claim) == true;
            }
            catch
            {
                return true; // unreadable - don't claim it's the wrong login
            }
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // ---- Saved sign-in (DPAPI-encrypted, this Windows user only)

        private static void EnsureLoaded(string appDataFolder)
        {
            lock (Gate)
            {
                if (_loaded) return;
                _loaded = true;
                try
                {
                    string path = Path.Combine(appDataFolder, LoginFileName);
                    if (!File.Exists(path)) return;
                    byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                    var saved = JsonNode.Parse(plain);
                    _refreshToken = saved?["refreshToken"]?.GetValue<string>();
                    _callsign = saved?["callsign"]?.GetValue<string>() ?? "";
                }
                catch
                {
                    _refreshToken = null; // unreadable (e.g. copied from another PC) - sign in again
                }
            }
        }

        // Caller holds Gate.
        private static void Save(string appDataFolder)
        {
            if (_refreshToken == null) return;
            var json = new JsonObject { ["refreshToken"] = _refreshToken, ["callsign"] = _callsign };
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json.ToJsonString()), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(Path.Combine(appDataFolder, LoginFileName), protectedBytes);
        }

        // ---- Rove plans on pota.app

        private static async Task<JsonNode> GetRoveJsonAsync(string appDataFolder, string url)
        {
            string token = await GetIdTokenAsync(appDataFolder);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", token);
            using var response = await Http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // A POTA account's ID is in the token (pota:id, put there by
                // pota.app's sign-in); a token without one is a login pota.app
                // doesn't know, not an expired one.
                if (!HasClaim(token, "pota:id")) throw new PotaNoAccountException();
                throw new PotaNotSignedInException();
            }
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? new JsonArray();
        }

        // Your pota.app rove plans as [{ id, name, stopCount }], newest first
        // (pota.app's own order), deleted ones left out.
        public static async Task<string> GetRovePlansJsonAsync(string appDataFolder)
        {
            var plans = await GetRoveJsonAsync(appDataFolder, RoveApi + "/plans");
            var list = new JsonArray();
            foreach (var p in plans.AsArray())
            {
                if (p == null || p["removedAt"] is JsonValue removed && removed.GetValueKind() != JsonValueKind.Null) continue;
                list.Add(new JsonObject
                {
                    ["id"] = p["roveId"]?.GetValue<int>() ?? 0,
                    ["name"] = p["name"]?.GetValue<string>() ?? "",
                    ["stopCount"] = p["stopCount"]?.GetValue<int>() ?? p["parkCount"]?.GetValue<int>() ?? 0
                });
            }
            return list.ToJsonString();
        }

        // One pota.app rove plan as { name, potaRoveId, stops: [...] } in
        // the planner's own stop format, in pota.app's stop order.
        public static async Task<string> GetRovePlanJsonAsync(string appDataFolder, int roveId)
        {
            var plan = await GetRoveJsonAsync(appDataFolder, RoveApi + "/plans/" + roveId);
            var parks = (plan["parks"]?.AsArray() ?? new JsonArray())
                .Where(p => p != null)
                .OrderBy(p => p!["sortOrder"]?.GetValue<int>() ?? 0);
            var stops = new JsonArray();
            foreach (var p in parks)
            {
                // A stop moved on pota.app (e.g. to a parking spot) keeps
                // the moved location in override*.
                double? lat = Number(p!["overrideLatitude"]) ?? Number(p["latitude"]) ?? Number(p["parkLatitude"]);
                double? lon = Number(p["overrideLongitude"]) ?? Number(p["longitude"]) ?? Number(p["parkLongitude"]);
                if (lat == null || lon == null) continue;
                stops.Add(new JsonObject
                {
                    ["reference"] = p["reference"]?.GetValue<string>() ?? "",
                    ["name"] = p["name"]?.GetValue<string>() ?? "",
                    ["lat"] = lat,
                    ["lon"] = lon
                });
            }
            return new JsonObject
            {
                ["name"] = plan["name"]?.GetValue<string>() ?? "pota.app rove",
                ["potaRoveId"] = roveId,
                ["stops"] = stops
            }.ToJsonString();
        }

        private static double? Number(JsonNode? node) =>
            node is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? v.GetValue<double>() : null;
    }
}
