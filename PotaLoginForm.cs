using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PotaActivationTool
{
    // POTA Login (menu bar): shows POTA's own sign-in page in a small browser
    // window (WebView2, built into Windows 11) - see PotaAccountService. When
    // the page sends the browser back to pota.app with a one-time code, that
    // navigation is stopped here and the code is traded for login tokens.
    public class PotaLoginForm : Form
    {
        private readonly string _appDataFolder;
        private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
        private readonly Label _status = new()
        {
            Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0), Text = "Loading POTA's sign-in page..."
        };
        private PotaAccountService.SignInRequest? _request;
        private bool _finishing;

        public PotaLoginForm(string appDataFolder)
        {
            _appDataFolder = appDataFolder;
            Text = "POTA Login";
            Icon = AppLogo.Icon;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 720);
            MinimizeBox = false;
            Controls.Add(_webView);
            Controls.Add(_status);
            Load += async (s, e) => await StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                // The browser's own data (cookies, cache) goes in the app data
                // folder - by default it would go next to the exe, which isn't
                // writable under Program Files.
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_appDataFolder, "WebView2"));
                await _webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _status.Text = "This needs the Microsoft Edge WebView2 Runtime (built into Windows 11) - install it from microsoft.com.";
                return;
            }
            catch (Exception ex)
            {
                _status.Text = "Couldn't open the sign-in page: " + ex.Message;
                return;
            }

            var core = _webView.CoreWebView2;
            // Browser password managers (e.g. Norton's) are extensions this
            // built-in browser doesn't load, so let it offer to save the
            // email and password itself and fill them in next time - kept
            // by WebView2 in the profile folder above, never by this program.
            core.Settings.IsPasswordAutosaveEnabled = true;
            core.Settings.IsGeneralAutofillEnabled = true;
            // Always a fresh sign-in page: a session left over from an
            // earlier sign-in (or a different account) would otherwise sign
            // straight back in without asking.
            core.CookieManager.DeleteAllCookies();
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += (s, e) =>
            {
                if (_finishing) return;
                _status.Text = e.IsSuccess
                    ? "Sign in with your pota.app account."
                    : "Couldn't load POTA's sign-in page - check the internet connection.";
            };
            _request = PotaAccountService.BeginSignIn();
            core.Navigate(_request.Url);
        }

        private async void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (_request == null || !e.Uri.StartsWith(PotaAccountService.RedirectUri, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            if (_finishing) return;

            var query = System.Web.HttpUtility.ParseQueryString(new Uri(e.Uri).Query);
            string? error = query["error_description"] ?? query["error"];
            string? code = query["code"];
            if (error != null || code == null || query["state"] != _request.State)
            {
                _status.Text = "Sign-in didn't complete" + (error != null ? ": " + error : ".") + " Close this window and try again.";
                return;
            }

            _finishing = true;
            _status.Text = "Signing in...";
            _webView.Visible = false;
            try
            {
                await PotaAccountService.CompleteSignInAsync(_appDataFolder, code, _request.CodeVerifier);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Couldn't finish signing in: " + ex.Message;
            }
        }
    }
}
