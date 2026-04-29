// MainPage.xaml.cs — WP8.1 Silverlight port
//
// UWP → WP8.1 changes:
//   • Page → PhoneApplicationPage  (base class; handled by XAML)
//   • Windows.UI.Xaml.* → Microsoft.Phone.Controls (mostly transparent)
//   • Dispatcher.RunAsync(CoreDispatcherPriority, ...) →
//         Deployment.Current.Dispatcher.BeginInvoke(Action)
//   • Windows.ApplicationModel.Email.EmailManager.ShowComposeNewEmailAsync →
//         Microsoft.Phone.Tasks.EmailComposeTask
//   • Windows.Storage.ApplicationData.Current.LocalFolder file I/O →
//         System.IO.IsolatedStorage.IsolatedStorageFile
//   • ContactManager.RequestStoreAsync → ContactStore.CreateOrOpenAsync
//     (called inside ContactStoreService; nothing to do here)
//   • RoutedEventArgs stays the same in Silverlight
//   • Task.Delay(TimeSpan) → Task.Delay(int milliseconds)  [.NET 4.5 API OK on WP8.1]

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Tasks;
using MSContactsSyncWP.Helpers;
using MSContactsSyncWP.Services;

namespace MSContactsSyncWP
{
    public partial class MainPage : PhoneApplicationPage
    {
        private readonly StringBuilder _log = new StringBuilder();
        private int            _pendingLogUpdate = 0;
        private GraphApiService _api;
        private DispatcherTimer _pollTimer;

        // ================================================================
        // CLIENT ID — obfuscated with XOR 42 (same as UWP version)
        // ================================================================
        private static readonly byte[] _clientIdBytes =
        {
            0x1f,0x1d,0x13,0x1f,0x1b,0x48,0x1b,0x12,0x07,0x12,0x48,0x18,
            0x1b,0x07,0x1e,0x4f,0x1e,0x48,0x07,0x4b,0x49,0x1b,0x1c,0x07,
            0x1c,0x4b,0x48,0x1a,0x4e,0x1b,0x1f,0x13,0x4e,0x13,0x1e,0x1a
        };

        private static string DefaultClientId
        {
            get
            {
                var sb = new StringBuilder();
                foreach (byte b in _clientIdBytes)
                    sb.Append((char)(b ^ 42));
                return sb.ToString();
            }
        }

        private string MaskClientId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (id.Length <= 6) return "***";
            return id.Substring(0, 3) + "***" + id.Substring(id.Length - 3);
        }

        // ================================================================
        // CONSTRUCTOR
        // ================================================================
        public MainPage()
        {
            InitializeComponent();
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string savedId   = CredentialStorage.LoadClientId();
                bool   hasCustom = !string.IsNullOrEmpty(savedId) &&
                                   savedId != DefaultClientId;
                if (hasCustom)
                {
                    ChkCustomClientId.IsChecked = true;
                    TxtClientId.Password        = savedId;
                    PanelClientId.Visibility    = Visibility.Visible;
                }

                if (CredentialStorage.HasToken())
                    ShowSignedInState(CredentialStorage.LoadUsername());
                else
                    ShowSignedOutState();
            }
            catch
            {
                ShowSignedOutState();
            }

            // NOTE: WP8.1 ContactStore.CreateOrOpenAsync() handles permissions;
            // no separate RequestStoreAsync permission call needed here.
        }

        // ================================================================
        // CUSTOM CLIENT ID TOGGLE
        // ================================================================
        private void ChkCustomClientId_Changed(object sender, RoutedEventArgs e)
        {
            bool enabled = ChkCustomClientId.IsChecked == true;
            PanelClientId.Visibility = enabled
                ? Visibility.Visible : Visibility.Collapsed;
            if (!enabled)
                TxtClientId.Password = "";
        }

        // ================================================================
        // SIGN IN
        // ================================================================
        private async void BtnSignIn_Click(object sender, RoutedEventArgs e)
        {
            string clientId = DefaultClientId;
            if (ChkCustomClientId.IsChecked == true &&
                !string.IsNullOrEmpty(TxtClientId.Password.Trim()))
                clientId = TxtClientId.Password.Trim();

            CredentialStorage.SaveClientId(clientId);
            _api = new GraphApiService(clientId);

            BtnSignIn.IsEnabled = false;
            TxtLoginStatus.Text = "Requesting device code...";

            bool ok = await _api.StartDeviceFlowAsync();
            if (!ok)
            {
                TxtLoginStatus.Text = "Failed to start device flow. Check Client ID.";
                BtnSignIn.IsEnabled = true;
                return;
            }

            TxtVerificationUrl.Text = _api.VerificationUrl;
            TxtUserCode.Text        = _api.UserCode;
            PanelCode.Visibility    = Visibility.Visible;
            TxtLoginStatus.Text     = "";

            // WP8.1: DispatcherTimer is in System.Windows.Threading — same API
            _pollTimer          = new DispatcherTimer();
            _pollTimer.Interval = TimeSpan.FromSeconds(_api.Interval);
            _pollTimer.Tick    += PollTimer_Tick;
            _pollTimer.Start();
        }

        private async void PollTimer_Tick(object sender, EventArgs e)
        {
            string result = await _api.PollForTokenAsync();
            if (result == "ok")
            {
                _pollTimer.Stop();
                PanelCode.Visibility = Visibility.Collapsed;

                string accessToken = CredentialStorage.LoadAccessToken();
                if (!string.IsNullOrEmpty(accessToken))
                {
                    string username = await _api.GetUsernameAsync(accessToken);
                    if (!string.IsNullOrEmpty(username))
                        CredentialStorage.SaveUsername(username);
                    ShowSignedInState(username);
                }
                else ShowSignedInState();

                Log("Signed in successfully.");
                TxtLoginStatus.Text = "";
                await SaveMsalCacheAsync();
            }
            else if (result == "authorization_pending" || result == "slow_down")
            {
                if (result == "slow_down")
                    _pollTimer.Interval =
                        TimeSpan.FromSeconds(_pollTimer.Interval.TotalSeconds + 5);
            }
            else
            {
                _pollTimer.Stop();
                PanelCode.Visibility = Visibility.Collapsed;
                TxtLoginStatus.Text  = "Auth failed: " + result;
                BtnSignIn.IsEnabled  = true;
            }
        }

        // ================================================================
        // SYNC — Microsoft → Phone
        // ================================================================
        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            SetUiBusy(true);
            Log("=== Sync started: " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            MainPivot.SelectedIndex = 2;

            string clientId = CredentialStorage.LoadClientId();
            if (string.IsNullOrEmpty(clientId))
                clientId = DefaultClientId;

            _api = new GraphApiService(clientId);

            Task.Run(async () =>
            {
                try
                {
                    string refreshToken = CredentialStorage.LoadToken();
                    Log("Getting access token...");
                    string accessToken = await _api.GetAccessTokenAsync(refreshToken);
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        Log("Failed to get access token. Please sign in again.");
                        return;
                    }
                    Log("Access token OK.");

                    Log("Fetching contacts from Microsoft...");
                    var fetchResult = await _api.FetchAllContactsAsync(
                        accessToken, msg => Log(msg));
                    var contacts  = fetchResult.Contacts;
                    var deltaLink = fetchResult.DeltaLink;
                    Log("Fetched: " + contacts.Count + " contacts.");

                    if (contacts.Count == 0)
                    {
                        Log("No contacts found.");
                        return;
                    }

                    var  savedEtags = SyncStateStorage.Load();
                    bool isFirst    = savedEtags.Count == 0;
                    bool isDelta    = !string.IsNullOrEmpty(
                        SyncStateStorage.LoadDeltaLink());
                    Log("isFirst=" + isFirst + " isDelta=" + isDelta +
                        " savedETags=" + savedEtags.Count);

                    var store    = new ContactStoreService();
                    var newEtags = new Dictionary<string, string>(savedEtags);
                    int updated  = 0;
                    int skipped  = 0;
                    int deleted  = 0;
                    int idx      = 0;

                    foreach (var c in contacts)
                    {
                        if (string.IsNullOrEmpty(c.Id)) continue;
                        idx++;
                        if (idx % 20 == 0)
                            Log("Processing " + idx + "/" + contacts.Count + "...");

                        if (c.IsDeleted)
                        {
                            await store.DeleteContactAsync(c.Id);
                            newEtags.Remove(c.Id);
                            deleted++;
                            continue;
                        }

                        bool changed = !savedEtags.ContainsKey(c.Id) ||
                                       savedEtags[c.Id] != c.ETag;

                        if (isFirst || !isDelta || changed)
                        {
                            await store.UpsertContactAsync(c);
                            newEtags[c.Id] = c.ETag ?? "";
                            updated++;
                        }
                        else skipped++;
                    }

                    SyncStateStorage.Save(newEtags);
                    if (!string.IsNullOrEmpty(deltaLink))
                    {
                        SyncStateStorage.SaveDeltaLink(deltaLink);
                        Log("DeltaLink saved for next sync.");
                    }

                    // WP8.1: marshal back to UI thread via BeginInvoke
                    RunOnUi(async () => await SaveMsalCacheAsync());

                    Log("Updated=" + updated + " Skipped=" + skipped +
                        " Deleted=" + deleted);
                    Log("=== Done: " + DateTime.Now.ToString("HH:mm:ss") + " ===");

                    RunOnUi(() =>
                    {
                        TxtLastSync.Text       = "Last sync: " +
                            DateTime.Now.ToString("dd MMM yyyy HH:mm");
                        TxtLastSync.Visibility = Visibility.Visible;
                    });
                }
                catch (Exception ex)
                {
                    Log("EXCEPTION: " + ex.GetType().Name);
                    Log("MSG: " + ex.Message);
                    if (ex.InnerException != null)
                        Log("INNER: " + ex.InnerException.Message);
                }
                finally
                {
                    FlushLog();
                    RunOnUi(() => SetUiBusy(false));
                }
            });
        }

        // ================================================================
        // SAVE MSAL CACHE — WP8.1: use IsolatedStorageFile
        // ================================================================
        private async Task SaveMsalCacheAsync()
        {
            try
            {
                string clientId     = CredentialStorage.LoadClientId();
                string refreshToken = CredentialStorage.LoadToken();
                string accessToken  = CredentialStorage.LoadAccessToken();
                string username     = CredentialStorage.LoadUsername();
                long   expiresOn    = CredentialStorage.LoadExpiry();

                if (string.IsNullOrEmpty(refreshToken)) return;

                string json = _api.BuildMsalCacheJson(
                    clientId, refreshToken, accessToken, username, expiresOn);

                // WP8.1: IsolatedStorageFile instead of StorageFolder
                await Task.Run(() =>
                {
                    using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
                    using (var stream = iso.OpenFile(
                        GraphApiService.CacheFileName,
                        FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(json);
                    }
                });

                Log("MSAL cache saved.");
            }
            catch (Exception ex)
            {
                Log("Cache save error: " + ex.Message);
            }
        }

        // ================================================================
        // SIGN OUT
        // ================================================================
        private void BtnSignOut_Click(object sender, RoutedEventArgs e)
        {
            CredentialStorage.DeleteToken();
            SyncStateStorage.Clear();
            SyncStateStorage.ClearDeltaLink();
            ShowSignedOutState();
            TxtLastSync.Visibility = Visibility.Collapsed;

            try
            {
                using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
                    if (iso.FileExists(GraphApiService.CacheFileName))
                        iso.DeleteFile(GraphApiService.CacheFileName);
            }
            catch { }
        }

        // ================================================================
        // EMAIL LOG — WP8.1: EmailComposeTask (no async)
        // ================================================================
        private void BtnEmailLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var task = new EmailComposeTask
                {
                    Subject = "MSContactSync log " +
                        DateTime.Now.ToString("dd MMM yyyy HH:mm"),
                    Body = _log.ToString()
                };
                task.Show();
            }
            catch { }
        }

        // ================================================================
        // CLEAR LOG
        // ================================================================
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _log.Clear();
            System.Threading.Interlocked.Exchange(ref _pendingLogUpdate, 0);
            TxtLog.Text = "";
        }

        // ================================================================
        // UI STATE
        // ================================================================
        private void ShowSignedInState(string username = null)
        {
            string clientId = CredentialStorage.LoadClientId() ?? DefaultClientId;
            string masked   = MaskClientId(clientId);
            string who = string.IsNullOrEmpty(username)
                ? "Connected  |  ID: " + masked
                : "Signed in: " + username + "  |  ID: " + masked;
            TxtAccountStatus.Text = who;
            BtnSync.IsEnabled     = true;
            BtnSignOut.IsEnabled  = true;
            BtnSignIn.IsEnabled   = false;
        }

        private void ShowSignedOutState()
        {
            TxtAccountStatus.Text = "Not signed in";
            BtnSync.IsEnabled     = false;
            BtnSignOut.IsEnabled  = false;
            BtnSignIn.IsEnabled   = true;
        }

        private void SetUiBusy(bool busy)
        {
            BtnSync.IsEnabled    = !busy;
            BtnSignOut.IsEnabled = !busy;
            BtnSignIn.IsEnabled  = !busy;
        }

        // ================================================================
        // LOGGING
        // ================================================================
        private void Log(string msg)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + " " + msg;
            _log.AppendLine(line);

            if (System.Threading.Interlocked.Exchange(ref _pendingLogUpdate, 1) == 0)
            {
                RunOnUi(() =>
                {
                    System.Threading.Interlocked.Exchange(ref _pendingLogUpdate, 0);
                    TxtLog.Text = _log.ToString();
                    // WP8.1 ScrollViewer: use ScrollToVerticalOffset
                    LogScroller.ScrollToVerticalOffset(LogScroller.ScrollableHeight);
                });
            }
        }

        private void FlushLog()
        {
            RunOnUi(() =>
            {
                TxtLog.Text = _log.ToString();
                LogScroller.ScrollToVerticalOffset(LogScroller.ScrollableHeight);
            });
        }

        // ================================================================
        // HELPER: marshal to UI thread
        // WP8.1: Deployment.Current.Dispatcher.BeginInvoke replaces
        //        Dispatcher.RunAsync(CoreDispatcherPriority, ...)
        // ================================================================
        private void RunOnUi(Action action)
        {
            Deployment.Current.Dispatcher.BeginInvoke(action);
        }
    }
}
