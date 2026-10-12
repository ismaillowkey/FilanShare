using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FileSender.Helpers;
using FileSender.Models;
using FileSender.Services;

namespace FileSender
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<LogEntry> _logs = new ObservableCollection<LogEntry>();
        private readonly ObservableCollection<ConnectedClientItem> _connectedClients = new ObservableCollection<ConnectedClientItem>();
        private readonly FileHttpServer _server = new FileHttpServer();
        private readonly FileHttpClient _client = new FileHttpClient();

        private CancellationTokenSource? _transferCts;
        private string? _clientConnectedHost;
        private int _clientConnectedPort;
        private string? _clientConnectedUser;
        private string? _clientConnectedPass;
        private string _clientCurrentPath = "";
        private string _downloadFolder;
        private bool _isExplicitExit = false;
        private int _lastCheckedFirewallPort = -1;
        private bool _isFirewallOpen = false;

        private TrayIcon? _trayIcon;
        private NativeMenuItem? _trayItemOpen;
        private NativeMenuItem? _trayItemExit;

        // User Accounts & Permissions
        private readonly ObservableCollection<UserAccount> _userAccounts = new ObservableCollection<UserAccount>();

        // Event Log
        private readonly ObservableCollection<LogEntry> _eventLog = new ObservableCollection<LogEntry>();
        private bool _eventLogExpanded = true;
        private int _elCountInfo = 0;
        private int _elCountSuccess = 0;
        private int _elCountWarn = 0;
        private int _elCountError = 0;

        public MainWindow()
        {
            InitializeComponent();

            LstConnectedDevices.ItemsSource = _connectedClients;
            ListUserAccounts.ItemsSource = _userAccounts;

            // Wire Event Log list
            EventLogList.ItemsSource = _eventLog;

            // Wire up auto-minimize on close [X]
            Closing += MainWindow_Closing;

            // Wire up Drag & Drop for file explorer
            LstFiles.AddHandler(DragDrop.DragOverEvent, LstFiles_DragOver);
            LstFiles.AddHandler(DragDrop.DragLeaveEvent, LstFiles_DragLeave);
            LstFiles.AddHandler(DragDrop.DropEvent, LstFiles_Drop);

            // Wire up server events
            _server.OnLog += Server_OnLog;
            _server.OnTransferStarted += Server_OnTransferStarted;
            _server.OnTransferCompleted += Server_OnTransferCompleted;
            _server.OnClientActivity += Server_OnClientActivity;

            // Setup default download folder: Downloads/FilanShare
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _downloadFolder = Path.Combine(userProfile, "Downloads", "FilanShare");
            if (!Directory.Exists(_downloadFolder))
            {
                try { Directory.CreateDirectory(_downloadFolder); } catch { }
            }

            // Setup default shared folder: Desktop/File Sharing
            InitializeDefaultSharedFolder();

            // Initialize Host Display Info with primary IP
            UpdateHostDisplayInfo();

            // Initialize Settings and About views
            InitSettingsAndAbout();

            // Initialize System Tray Icon
            InitializeTrayIcon();

            // Wire up Language Manager
            LanguageManager.Instance.LanguageChanged += ApplyLanguage;
            ApplyLanguage();

            // Wire up Port change listener for auto firewall check & open
            TxtPort.LostFocus += async (s, e) => await HandlePortChangedAsync();
            TxtPort.KeyUp += async (s, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    await HandlePortChangedAsync();
                }
            };
            TxtPassword.KeyUp += (s, e) => UpdatePasswordDisplay();
            TxtUsername.KeyUp += (s, e) => UpdateHostDisplayInfo();
            ChkMinimizeToTrayOnClose.IsCheckedChanged += (s, e) => SaveSettings();

            Log(LanguageManager.Instance.AppReadyMsg, LogLevel.Info);

            // Auto-start sharing server on app launch (which also verifies and opens firewall)
            _ = StartServerAsync(showErrors: false);
        }

        private void InitSettingsAndAbout()
        {
            TxtSettingSharedPath.Text = TxtSharedFolderPath.Text;

            TxtAboutOs.Text = RuntimeInformation.OSDescription;
            TxtAboutArch.Text = $"{RuntimeInformation.ProcessArchitecture} ({RuntimeInformation.OSArchitecture})";
            TxtAboutAppPath.Text = AppDomain.CurrentDomain.BaseDirectory;

            LoadSettings();
        }

        private string GetSettingsFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "filanshare_settings.json");
        }

        private void LoadSettings()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var data = JsonConvert.DeserializeObject<FilanShareSettingsDto>(json);
                    if (data != null)
                    {
                        if (data.Port > 0 && data.Port <= 65535)
                        {
                            TxtSettingDefaultPort.Text = data.Port.ToString();
                            TxtPort.Text = data.Port.ToString();
                        }
                        if (!string.IsNullOrEmpty(data.SharedPath) && Directory.Exists(data.SharedPath))
                        {
                            TxtSharedFolderPath.Text = data.SharedPath;
                            TxtSettingSharedPath.Text = data.SharedPath;
                        }
                        if (!string.IsNullOrEmpty(data.DownloadPath) && Directory.Exists(data.DownloadPath))
                        {
                            if (data.DownloadPath.EndsWith("FileSender_Downloads", StringComparison.OrdinalIgnoreCase))
                            {
                                string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                                _downloadFolder = Path.Combine(userProf, "Downloads", "FilanShare");
                                if (!Directory.Exists(_downloadFolder)) try { Directory.CreateDirectory(_downloadFolder); } catch { }
                            }
                            else
                            {
                                _downloadFolder = data.DownloadPath;
                            }
                        }

                        if (data.Users != null && data.Users.Count > 0)
                        {
                            _userAccounts.Clear();
                            foreach (var u in data.Users)
                            {
                                _userAccounts.Add(u);
                            }
                        }

                        ChkMinimizeToTrayOnClose.IsChecked = data.MinimizeToTrayOnClose;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[Settings] Gagal membaca konfigurasi: {ex.Message}", LogLevel.Warning);
            }

            // Ensure admin exists with default all checked (CanRead=true, CanWrite=true, CanDelete=true)
            var admin = _userAccounts.FirstOrDefault(u => u.IsAdmin);
            if (admin == null)
            {
                admin = new UserAccount
                {
                    Username = "admin",
                    Password = "admin",
                    CanRead = true,
                    CanWrite = true,
                    CanDelete = true
                };
                _userAccounts.Insert(0, admin);
            }

            TxtUsername.Text = admin.Username;
            TxtPassword.Text = admin.Password;
            ChkAllowDownload.IsChecked = admin.CanRead;
            ChkAllowUpload.IsChecked = admin.CanWrite;

            _server.Users = _userAccounts.ToList();
        }

        private void SaveSettings()
        {
            try
            {
                int port = 5050;
                int.TryParse(TxtSettingDefaultPort.Text?.Trim(), out port);
                if (port <= 0 || port > 65535) port = 5050;

                var data = new FilanShareSettingsDto
                {
                    Port = port,
                    SharedPath = TxtSharedFolderPath.Text ?? "",
                    DownloadPath = _downloadFolder,
                    MinimizeToTrayOnClose = ChkMinimizeToTrayOnClose.IsChecked == true,
                    Users = _userAccounts.ToList()
                };

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                File.WriteAllText(GetSettingsFilePath(), json);
            }
            catch (Exception ex)
            {
                Log($"[Settings] Gagal menyimpan konfigurasi: {ex.Message}", LogLevel.Warning);
            }
        }

        private void InitializeDefaultSharedFolder()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                string defaultFolder = Path.Combine(desktopPath, "File Sharing");

                if (!Directory.Exists(defaultFolder))
                {
                    try
                    {
                        Directory.CreateDirectory(defaultFolder);
                    }
                    catch (UnauthorizedAccessException uex)
                    {
                        Log($"Izin membuat folder di Desktop ditolak ({uex.Message}). Menggunakan folder alternatif.", LogLevel.Warning);
                    }
                }

                // Verify permissions
                if (Directory.Exists(defaultFolder) && NetworkHelper.CheckFolderPermissions(defaultFolder, checkWrite: true, out string? permError))
                {
                    TxtSharedFolderPath.Text = defaultFolder;
                    string defaultLogMsg = LanguageManager.Instance.IsId 
                        ? $"Folder share default diatur ke: {defaultFolder}" 
                        : $"Default shared folder set to: {defaultFolder}";
                    Log(defaultLogMsg, LogLevel.Info);
                }
                else
                {
                    string fallbackFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "File Sharing");
                    if (!Directory.Exists(fallbackFolder))
                    {
                        try { Directory.CreateDirectory(fallbackFolder); } catch { }
                    }

                    TxtSharedFolderPath.Text = fallbackFolder;
                    Log($"Akses Desktop dibatasi permission. Folder default dialihkan ke: {fallbackFolder}", LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                Log($"Peringatan: Gagal mengatur folder default: {ex.Message}", LogLevel.Warning);
                TxtSharedFolderPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }

        private void Server_OnLog(LogEntry log)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Push to Logs tab
                _logs.Insert(0, log);
                if (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);

                // Push to Event Log panel (newest at bottom)
                _eventLog.Add(log);
                if (_eventLog.Count > 300) _eventLog.RemoveAt(0);

                // Update counters
                switch (log.Level)
                {
                    case LogLevel.Success: _elCountSuccess++; TxtLogCountSuccess.Text = $"OK: {_elCountSuccess}"; break;
                    case LogLevel.Warning: _elCountWarn++;    TxtLogCountWarn.Text    = $"WARN: {_elCountWarn}"; break;
                    case LogLevel.Error:   _elCountError++;   TxtLogCountError.Text   = $"ERR: {_elCountError}"; break;
                    default:               _elCountInfo++;    TxtLogCountInfo.Text    = $"INFO: {_elCountInfo}"; break;
                }

                TxtEventLogLastMsg.Text = $"› {log.Message}";
                EventLogScrollViewer.ScrollToEnd();
            });
        }

        private void Server_OnTransferStarted(string clientIp, string fileName, long size)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                    ? $"[Server] Transfer dimulai: {fileName} ({NetworkHelper.FormatBytes(size)}) untuk {clientIp}"
                    : $"[Server] Transfer started: {fileName} ({NetworkHelper.FormatBytes(size)}) for {clientIp}";
            });
        }

        private void Server_OnTransferCompleted(string clientIp, string fileName, long bytes)
        {
            Dispatcher.UIThread.Post(() =>
            {
                TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                    ? $"[Server] Transfer selesai: {fileName} ({NetworkHelper.FormatBytes(bytes)}) untuk {clientIp}"
                    : $"[Server] Transfer completed: {fileName} ({NetworkHelper.FormatBytes(bytes)}) for {clientIp}";
            });
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var entry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Message = message,
                    Level = level
                };

                // Push to Logs tab
                _logs.Insert(0, entry);
                if (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);

                // Push to Event Log panel (newest at bottom)
                _eventLog.Add(entry);
                if (_eventLog.Count > 300) _eventLog.RemoveAt(0);

                // Update counters
                switch (level)
                {
                    case LogLevel.Success: _elCountSuccess++; TxtLogCountSuccess.Text = $"OK: {_elCountSuccess}"; break;
                    case LogLevel.Warning: _elCountWarn++;    TxtLogCountWarn.Text    = $"WARN: {_elCountWarn}"; break;
                    case LogLevel.Error:   _elCountError++;   TxtLogCountError.Text   = $"ERR: {_elCountError}"; break;
                    default:               _elCountInfo++;    TxtLogCountInfo.Text    = $"INFO: {_elCountInfo}"; break;
                }

                // Show last message in header
                TxtEventLogLastMsg.Text = $"› {message}";

                // Auto-scroll event log to bottom
                EventLogScrollViewer.ScrollToEnd();
            });
        }

        private async Task ShowMessageBoxAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                MinWidth = 420,
                MaxWidth = 540,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush.Parse("#FFFFFF"),
                CanResize = false
            };

            var grid = new Grid
            {
                Margin = new Avalonia.Thickness(24, 20),
                RowDefinitions = new RowDefinitions("*,Auto")
            };

            var txt = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#0F172A"),
                FontSize = 13,
                Margin = new Avalonia.Thickness(0, 0, 0, 18),
                LineHeight = 19
            };
            Grid.SetRow(txt, 0);

            var btn = new Button
            {
                Content = "OK",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Classes = { "primary" },
                Padding = new Avalonia.Thickness(24, 8),
                FontSize = 13
            };
            btn.Click += (_, _) => dialog.Close();
            Grid.SetRow(btn, 1);

            grid.Children.Add(txt);
            grid.Children.Add(btn);
            dialog.Content = grid;

            await dialog.ShowDialog(this);
        }

        private async Task<bool> ShowConfirmationBoxAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                MinWidth = 420,
                MaxWidth = 540,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush.Parse("#FFFFFF"),
                CanResize = false
            };

            var grid = new Grid
            {
                Margin = new Avalonia.Thickness(24, 20),
                RowDefinitions = new RowDefinitions("*,Auto")
            };

            var txt = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush.Parse("#0F172A"),
                FontSize = 13,
                Margin = new Avalonia.Thickness(0, 0, 0, 18),
                LineHeight = 19
            };
            Grid.SetRow(txt, 0);

            var panelBtns = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 10
            };
            Grid.SetRow(panelBtns, 1);

            bool confirmed = false;

            var btnCancel = new Button
            {
                Content = LanguageManager.Instance.IsId ? "Batal" : "Cancel",
                Classes = { "outline" },
                Padding = new Avalonia.Thickness(18, 8),
                FontSize = 13
            };
            btnCancel.Click += (_, _) => { confirmed = false; dialog.Close(); };

            var btnConfirm = new Button
            {
                Content = LanguageManager.Instance.IsId ? "Hapus" : "Delete",
                Classes = { "danger" },
                Padding = new Avalonia.Thickness(22, 8),
                FontSize = 13
            };
            btnConfirm.Click += (_, _) => { confirmed = true; dialog.Close(); };

            panelBtns.Children.Add(btnCancel);
            panelBtns.Children.Add(btnConfirm);

            grid.Children.Add(txt);
            grid.Children.Add(panelBtns);
            dialog.Content = grid;

            await dialog.ShowDialog(this);
            return confirmed;
        }

        private (string Title, string Message) FormatUserFriendlyError(Exception ex, string? host, int port)
        {
            bool isId = LanguageManager.Instance.IsId;
            string displayHost = host ?? "127.0.0.1";

            // 1. HTTP 401 Unauthorized -> Wrong username or password
            if ((ex is HttpRequestException httpEx && httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized) 
                || ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
            {
                return (
                    isId ? "Autentikasi Gagal" : "Authentication Failed",
                    isId ? "Username atau Password yang dimasukkan salah.\n\nPeriksa kembali Username dan Password pada perangkat Host."
                         : "Incorrect Username or Password.\n\nPlease check the Username and Password configured on the Host machine."
                );
            }

            // 2. HTTP 403 Forbidden -> Permissions blocked
            if ((ex is HttpRequestException httpEx403 && httpEx403.StatusCode == System.Net.HttpStatusCode.Forbidden) 
                || ex.Message.Contains("403") || ex.Message.Contains("Forbidden"))
            {
                return (
                    isId ? "Akses Ditolak" : "Access Denied",
                    isId ? "Komputer Host menolak akses untuk tindakan ini karena izin download/upload tidak diaktifkan."
                         : "The Host machine denied access because download/upload permission is disabled."
                );
            }

            // 3. Server offline or connection refused
            if (ex.InnerException is SocketException || ex.Message.Contains("actively refused") 
                || ex.Message.Contains("Connection refused") || ex.Message.Contains("No route to host"))
            {
                return (
                    isId ? "Server Tidak Ditemukan" : "Server Not Found",
                    isId ? $"Tidak dapat terhubung ke server di {displayHost}:{port}.\n\nPastikan:\n1. Server di komputer Host sudah aktif ('Mulai Sharing').\n2. Alamat IP ({displayHost}) dan Port ({port}) sudah benar.\n3. Kedua perangkat terhubung ke jaringan LAN / Wi-Fi yang sama."
                         : $"Cannot reach the server at {displayHost}:{port}.\n\nPlease ensure:\n1. Server on the Host PC is active ('Start Sharing').\n2. IP Address ({displayHost}) and Port ({port}) are correct.\n3. Both devices are connected to the same LAN / Wi-Fi network."
                );
            }

            // 4. Timeout
            if (ex is TaskCanceledException || ex is TimeoutException || ex.Message.Contains("timeout") || ex.Message.Contains("timed out"))
            {
                return (
                    isId ? "Waktu Koneksi Habis" : "Connection Timed Out",
                    isId ? $"Komputer Host di {displayHost}:{port} tidak merespons dalam batas waktu.\n\nPeriksa jaringan LAN / Wi-Fi atau pastikan port diizinkan di firewall Host."
                         : $"Host PC at {displayHost}:{port} did not respond in time.\n\nPlease check your network connection or ensure port is open in Host's firewall."
                );
            }

            // 5. Generic user-friendly fallback
            return (
                isId ? "Koneksi Gagal" : "Connection Failed",
                isId ? $"Gagal terhubung ke {displayHost}:{port}.\n\nPastikan kedua perangkat berada di jaringan yang sama dan server Host aktif."
                     : $"Failed to connect to {displayHost}:{port}.\n\nPlease ensure both devices are on the same network and the Host server is running."
            );
        }

        #region Main Top Menu Navigation (File, Setting, About)

        private void BtnMenuFile_Click(object? sender, RoutedEventArgs e)
        {
            ViewMenuFile.IsVisible = true;
            ViewMenuSetting.IsVisible = false;
            ViewMenuAbout.IsVisible = false;

            BtnMenuFile.Classes.Clear(); BtnMenuFile.Classes.Add("tab-active");
            BtnMenuSetting.Classes.Clear(); BtnMenuSetting.Classes.Add("tab");
            BtnMenuAbout.Classes.Clear(); BtnMenuAbout.Classes.Add("tab");

            TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                ? "Menu File: Bagikan folder atau jelajahi file di jaringan LAN." 
                : "File Menu: Share folders or browse files on local network.";
        }

        private void BtnMenuSetting_Click(object? sender, RoutedEventArgs e)
        {
            ViewMenuFile.IsVisible = false;
            ViewMenuSetting.IsVisible = true;
            ViewMenuAbout.IsVisible = false;

            BtnMenuFile.Classes.Clear(); BtnMenuFile.Classes.Add("tab");
            BtnMenuSetting.Classes.Clear(); BtnMenuSetting.Classes.Add("tab-active");
            BtnMenuAbout.Classes.Clear(); BtnMenuAbout.Classes.Add("tab");

            // Sync values to setting view
            TxtSettingSharedPath.Text = TxtSharedFolderPath.Text;

            TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                ? "Menu Setting: Konfigurasi folder dan parameter default." 
                : "Settings Menu: Configure default folders and application parameters.";
        }

        private void BtnMenuAbout_Click(object? sender, RoutedEventArgs e)
        {
            ViewMenuFile.IsVisible = false;
            ViewMenuSetting.IsVisible = false;
            ViewMenuAbout.IsVisible = true;

            BtnMenuFile.Classes.Clear(); BtnMenuFile.Classes.Add("tab");
            BtnMenuSetting.Classes.Clear(); BtnMenuSetting.Classes.Add("tab");
            BtnMenuAbout.Classes.Clear(); BtnMenuAbout.Classes.Add("tab-active");

            TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                ? "Menu About: Informasi versi, fitur, dan diagnostik sistem." 
                : "About Menu: Version info, features, and system diagnostics.";
        }

        #endregion

        #region File Sub-Tabs Navigation (Host, Client)

        private void BtnSubTabHost_Click(object? sender, RoutedEventArgs e)
        {
            ViewHost.IsVisible = true;
            ViewClient.IsVisible = false;

            BtnSubTabHost.Classes.Clear(); BtnSubTabHost.Classes.Add("primary");
            BtnSubTabClient.Classes.Clear(); BtnSubTabClient.Classes.Add("outline");
        }

        private void BtnSubTabClient_Click(object? sender, RoutedEventArgs e)
        {
            ViewHost.IsVisible = false;
            ViewClient.IsVisible = true;

            BtnSubTabHost.Classes.Clear(); BtnSubTabHost.Classes.Add("outline");
            BtnSubTabClient.Classes.Clear(); BtnSubTabClient.Classes.Add("primary");
        }

        #endregion

        #region Host Mode

        private async void BtnBrowseFolder_Click(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Pilih Folder yang Ingin Dibagikan ke Jaringan LAN",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string selected = folders[0].Path.LocalPath;

                // Pre-check permissions
                if (!NetworkHelper.CheckFolderPermissions(selected, false, out string? permError))
                {
                    await ShowMessageBoxAsync("Peringatan Izin Sistem",
                        $"Perhatian: Folder yang Anda pilih memiliki batasan izin di sistem.\n\n" +
                        $"Detail: {permError}\n\n" +
                        "Aplikasi mungkin tidak dapat membaca atau membagikan file di folder ini jika tidak diberikan hak akses.");
                }

                TxtSharedFolderPath.Text = selected;
                TxtSettingSharedPath.Text = selected;
            }
        }

        private void BtnToggleShowPass_Click(object? sender, RoutedEventArgs e)
        {
            if (TxtPassword.PasswordChar == '*')
            {
                TxtPassword.PasswordChar = '\0';
            }
            else
            {
                TxtPassword.PasswordChar = '*';
            }
            UpdatePasswordDisplay();
        }

        private void BtnGeneratePass_Click(object? sender, RoutedEventArgs e)
        {
            if (_server.IsRunning) return;

            string newPass = NetworkHelper.GenerateRandomPassword(8);
            TxtPassword.Text = newPass;
            UpdatePasswordDisplay();
            Log(LanguageManager.Instance.RandomPassGeneratedMsg, LogLevel.Info);
        }

        private void CmbIpAddresses_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateHostDisplayInfo();
        }

        #region Firewall Management

        private int GetCurrentPort()
        {
            if (int.TryParse(TxtPort.Text?.Trim(), out int port) && port > 0 && port <= 65535)
            {
                return port;
            }
            return 5050;
        }

        private async Task HandlePortChangedAsync()
        {
            int port = GetCurrentPort();
            UpdateHostDisplayInfo();
            if (port != _lastCheckedFirewallPort)
            {
                await CheckAndEnsureFirewallAsync(port, autoOpen: true);
            }
        }

        private async Task CheckAndEnsureFirewallAsync(int port, bool autoOpen = true)
        {
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PillFirewallStatus.Background = Brush.Parse("#EFF6FF");
                    PillFirewallStatus.BorderBrush = Brush.Parse("#BFDBFE");
                    TxtFirewallIcon.Text = "🔄";
                    TxtFirewallStatus.Text = LanguageManager.Instance.FirewallChecking;
                    TxtFirewallStatus.Foreground = Brush.Parse("#1E40AF");
                    BtnFixFirewall.IsVisible = false;
                });

                bool isOpen = await FirewallManager.IsFirewallOpenAsync(port);
                if (isOpen)
                {
                    _isFirewallOpen = true;
                    _lastCheckedFirewallPort = port;
                    Dispatcher.UIThread.Post(() => UpdateFirewallUi(port, true));
                    Log(string.Format(LanguageManager.Instance.FirewallAlreadyOpen, port), LogLevel.Info);
                    return;
                }

                // If not open, auto-open
                if (autoOpen)
                {
                    Log(LanguageManager.Instance.IsId 
                        ? $"[Firewall] Port {port} belum terbuka. Membuka otomatis di Windows Firewall..." 
                        : $"[Firewall] Port {port} is not open. Automatically configuring firewall...", LogLevel.Warning);

                    var (success, message) = await FirewallManager.OpenFirewallAsync(port);
                    if (success)
                    {
                        _isFirewallOpen = true;
                        _lastCheckedFirewallPort = port;
                        Dispatcher.UIThread.Post(() => UpdateFirewallUi(port, true));
                        Log(string.Format(LanguageManager.Instance.FirewallAutoOpened, port), LogLevel.Success);
                    }
                    else
                    {
                        _isFirewallOpen = false;
                        _lastCheckedFirewallPort = port;
                        Dispatcher.UIThread.Post(() => UpdateFirewallUi(port, false));
                        Log(string.Format(LanguageManager.Instance.FirewallOpenFailed, port, message), LogLevel.Warning);
                    }
                }
                else
                {
                    _isFirewallOpen = false;
                    _lastCheckedFirewallPort = port;
                    Dispatcher.UIThread.Post(() => UpdateFirewallUi(port, false));
                }
            }
            catch (Exception ex)
            {
                Log($"[Firewall] Error: {ex.Message}", LogLevel.Warning);
            }
        }

        private void UpdateFirewallUi(int port, bool isOpen)
        {
            if (isOpen)
            {
                PillFirewallStatus.Background = Brush.Parse("#ECFDF5");
                PillFirewallStatus.BorderBrush = Brush.Parse("#A7F3D0");
                TxtFirewallIcon.Text = "🛡️";
                TxtFirewallStatus.Text = string.Format(LanguageManager.Instance.FirewallStatusOpen, port);
                TxtFirewallStatus.Foreground = Brush.Parse("#065F46");
                BtnFixFirewall.IsVisible = false;
            }
            else
            {
                PillFirewallStatus.Background = Brush.Parse("#FEF2F2");
                PillFirewallStatus.BorderBrush = Brush.Parse("#FECACA");
                TxtFirewallIcon.Text = "⚠️";
                TxtFirewallStatus.Text = string.Format(LanguageManager.Instance.FirewallStatusClosed, port);
                TxtFirewallStatus.Foreground = Brush.Parse("#991B1B");
                BtnFixFirewall.IsVisible = true;
            }
        }

        private async void BtnFixFirewall_Click(object? sender, RoutedEventArgs e)
        {
            int port = GetCurrentPort();
            await CheckAndEnsureFirewallAsync(port, autoOpen: true);
        }

        #endregion

        private void LoadIpAddresses()
        {
            var ips = NetworkHelper.GetLocalIPv4Addresses();
            CmbInfoHostIp.ItemsSource = ips;
            if (ips.Count > 0)
            {
                if (CmbInfoHostIp.SelectedItem == null || !ips.Contains(CmbInfoHostIp.SelectedItem))
                {
                    CmbInfoHostIp.SelectedIndex = 0;
                }
                if (CmbInfoHostIp.SelectedItem is NetworkInterfaceItem selected)
                {
                    TxtInfoHostIp.Text = selected.IpAddress;
                }
            }
            else
            {
                TxtInfoHostIp.Text = "127.0.0.1";
            }
        }

        private void CmbInfoHostIp_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CmbInfoHostIp.SelectedItem is NetworkInterfaceItem item)
            {
                TxtInfoHostIp.Text = item.IpAddress;
            }
        }

        private void UpdateHostDisplayInfo()
        {
            LoadIpAddresses();
            TxtInfoPort.Text = TxtPort.Text;
            TxtInfoUsername.Text = TxtUsername.Text;
            UpdatePasswordDisplay();
        }

        private void UpdatePasswordDisplay()
        {
            string rawPass = TxtPassword.Text ?? "";
            bool isMasked = TxtPassword.PasswordChar == '*';

            if (isMasked)
            {
                TxtInfoPassword.Text = string.IsNullOrEmpty(rawPass) ? "" : new string('•', rawPass.Length);
                BtnToggleShowPass.Content = LanguageManager.Instance.ShowPassBtn;
                if (BtnToggleShowPassInfo != null) BtnToggleShowPassInfo.Content = "👁";
            }
            else
            {
                TxtInfoPassword.Text = rawPass;
                BtnToggleShowPass.Content = LanguageManager.Instance.HidePassBtn;
                if (BtnToggleShowPassInfo != null) BtnToggleShowPassInfo.Content = "🙈";
            }
        }

        private async void BtnToggleServer_Click(object? sender, RoutedEventArgs e)
        {
            if (_server.IsRunning)
            {
                StopServer();
            }
            else
            {
                await StartServerAsync(showErrors: true);
            }
        }

        public async Task StartServerAsync(bool showErrors = true)
        {
            if (_server.IsRunning) return;

            // Validate Folder
            string path = TxtSharedFolderPath.Text ?? "";
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                if (showErrors)
                {
                    await ShowMessageBoxAsync(
                        LanguageManager.Instance.IsId ? "Folder Belum Dipilih" : "No Folder Selected", 
                        LanguageManager.Instance.IsId ? "Silakan pilih folder yang valid untuk dibagikan terlebih dahulu." : "Please select a valid folder to share first.");
                }
                return;
            }

            // Check permissions
            bool checkWrite = ChkAllowUpload.IsChecked == true;
            if (!NetworkHelper.CheckFolderPermissions(path, checkWrite, out string? permError))
            {
                string message = LanguageManager.Instance.IsId
                    ? $"Akses ke folder '{path}' ditolak oleh sistem keamanan (Access Denied).\n\n{permError}\n\nSolusi:\n1. Gunakan folder di Desktop atau Documents yang tidak diproteksi.\n2. Atau periksa hak akses baca & tulis user Anda pada folder tersebut."
                    : $"Access to folder '{path}' was denied by security permissions (Access Denied).\n\n{permError}\n\nSolution:\n1. Use an unprotected folder in Desktop or Documents.\n2. Or verify your user read & write permissions for this folder.";

                if (showErrors)
                {
                    await ShowMessageBoxAsync(LanguageManager.Instance.IsId ? "Izin Akses Ditolak" : "Access Denied", message);
                }
                Log($"[Akses Ditolak] Tidak dapat memulai server untuk folder '{path}': {permError}", LogLevel.Error);
                return;
            }

            // Validate Port
            if (!int.TryParse(TxtPort.Text?.Trim(), out int port) || port < 1 || port > 65535)
            {
                if (showErrors)
                {
                    await ShowMessageBoxAsync(
                        LanguageManager.Instance.IsId ? "Port Tidak Valid" : "Invalid Port", 
                        LanguageManager.Instance.IsId ? "Port harus berupa angka antara 1 dan 65535." : "Port must be a number between 1 and 65535.");
                }
                return;
            }

            // Re-check and ensure firewall is open for active port
            _ = CheckAndEnsureFirewallAsync(port, autoOpen: true);

            string user = TxtUsername.Text?.Trim() ?? "admin";
            string pass = TxtPassword.Text ?? "";

            try
            {
                var adminUser = _userAccounts.FirstOrDefault(u => u.IsAdmin);
                if (adminUser != null)
                {
                    adminUser.Username = user;
                    adminUser.Password = pass;
                }

                _server.SharedDirectory = path;
                _server.Port = port;
                _server.Username = user;
                _server.Password = pass;
                _server.AllowDownload = ChkAllowDownload.IsChecked == true;
                _server.AllowUpload = ChkAllowUpload.IsChecked == true;
                _server.Users = _userAccounts.ToList();

                _server.Start();

                BtnToggleServer.Content = LanguageManager.Instance.StopSharingBtn;
                BtnToggleServer.Classes.Clear();
                BtnToggleServer.Classes.Add("danger");

                PanelHostStopped.IsVisible = false;
                PanelHostRunning.IsVisible = true;

                DotServerStatus.Fill = Brush.Parse("#10B981");
                TxtServerStatusBadge.Text = LanguageManager.Instance.ServerOnline;

                UpdateHostDisplayInfo();

                TxtSharedFolderPath.IsEnabled = false;
                BtnBrowseFolder.IsEnabled = false;
                TxtPort.IsEnabled = false;
                TxtUsername.IsEnabled = false;
                TxtPassword.IsEnabled = false;
                BtnGeneratePass.IsEnabled = false;
                ChkAllowDownload.IsEnabled = false;
                ChkAllowUpload.IsEnabled = false;

                TxtStatusMessage.Text = string.Format(LanguageManager.Instance.ServerRunningMessage, port);
            }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    await ShowMessageBoxAsync(
                        LanguageManager.Instance.IsId ? "Kesalahan Server" : "Server Error", 
                        $"{(LanguageManager.Instance.IsId ? "Gagal memulai server:" : "Failed to start server:")}\n{ex.Message}");
                }
                Log($"Gagal memulai server: {ex.Message}", LogLevel.Error);
            }
        }

        public void StopServer()
        {
            if (!_server.IsRunning) return;

            _server.Stop();

            BtnToggleServer.Content = LanguageManager.Instance.StartSharingBtn;
            BtnToggleServer.Classes.Clear();
            BtnToggleServer.Classes.Add("success");

            PanelHostStopped.IsVisible = true;
            PanelHostRunning.IsVisible = false;

            DotServerStatus.Fill = Brush.Parse("#DC2626");
            TxtServerStatusBadge.Text = LanguageManager.Instance.ServerOffline;

            TxtSharedFolderPath.IsEnabled = true;
            BtnBrowseFolder.IsEnabled = true;
            TxtPort.IsEnabled = true;
            TxtUsername.IsEnabled = true;
            TxtPassword.IsEnabled = true;
            BtnGeneratePass.IsEnabled = true;
            ChkAllowDownload.IsEnabled = true;
            ChkAllowUpload.IsEnabled = true;

            TxtStatusMessage.Text = LanguageManager.Instance.ServerStoppedMessage;
        }

        private async void BtnCopyCredentials_Click(object? sender, RoutedEventArgs e)
        {
            string hostIp = (CmbInfoHostIp.SelectedItem as NetworkInterfaceItem)?.IpAddress ?? TxtInfoHostIp.Text ?? "";
            var allIps = NetworkHelper.GetLocalIPv4Addresses();
            var allIpList = string.Join("\n", allIps.Select(i => $"  - {i.DisplayText}"));

            string info = $"[FilanShare Connection Info]\n" +
                          $"Host IP: {hostIp}\n" +
                          $"Port: {TxtInfoPort.Text}\n" +
                          $"Username: {TxtInfoUsername.Text}\n" +
                          $"Password: {TxtInfoPassword.Text}\n\n" +
                          $"All Available Interface IPs (Server listens on 0.0.0.0):\n" +
                          $"{allIpList}";

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(info);
                await ShowMessageBoxAsync(LanguageManager.Instance.CopiedTitle, LanguageManager.Instance.CredentialsCopiedMsg);
            }
        }

        #endregion

        #region Client Mode

        private async void BtnClientConnect_Click(object? sender, RoutedEventArgs e)
        {
            string host = TxtClientHostIp.Text?.Trim() ?? "127.0.0.1";
            if (!int.TryParse(TxtClientPort.Text?.Trim(), out int port) || port < 1 || port > 65535)
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxInvalidPortTitle, LanguageManager.Instance.MsgBoxInvalidPortDesc);
                return;
            }

            string user = TxtClientUsername.Text?.Trim() ?? "admin";
            string pass = TxtClientPassword.Text ?? "";

            BtnClientConnect.IsEnabled = false;
            TxtStatusMessage.Text = string.Format(LanguageManager.Instance.ClientConnectingMessage, host, port);

            try
            {
                var ping = await _client.PingAsync(host, port, user, pass);
                if (ping != null && ping.Success)
                {
                    _clientConnectedHost = host;
                    _clientConnectedPort = port;
                    _clientConnectedUser = user;
                    _clientConnectedPass = pass;

                    BtnClientConnect.IsEnabled = false;
                    BtnClientDisconnect.IsEnabled = true;
                    TxtClientHostIp.IsEnabled = false;
                    TxtClientPort.IsEnabled = false;
                    TxtClientUsername.IsEnabled = false;
                    TxtClientPassword.IsEnabled = false;

                    Log(LanguageManager.Instance.IsId ? $"Terhubung ke Host {host}:{port} (Folder: {ping.SharedFolderName})" : $"Connected to Host {host}:{port} (Folder: {ping.SharedFolderName})", LogLevel.Success);
                    TxtStatusMessage.Text = string.Format(LanguageManager.Instance.ClientConnectedMessage, host, port);

                    await LoadDirectoryAsync("");
                }
                else
                {
                    throw new Exception(LanguageManager.Instance.IsId ? "Respon server tidak valid." : "Invalid server response.");
                }
            }
            catch (Exception ex)
            {
                BtnClientConnect.IsEnabled = true;
                var (errTitle, errMsg) = FormatUserFriendlyError(ex, host, port);
                await ShowMessageBoxAsync(errTitle, errMsg);
                Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal terhubung ke {host}:{port}: {errTitle}" : $"[Client] Failed to connect to {host}:{port}: {errTitle}", LogLevel.Error);
                TxtStatusMessage.Text = errTitle;
            }
        }

        private void BtnClientDisconnect_Click(object? sender, RoutedEventArgs e)
        {
            _clientConnectedHost = null;
            _clientCurrentPath = "";
            LstFiles.ItemsSource = null;
            TxtCurrentPathDisplay.Text = "/";

            BtnClientConnect.IsEnabled = true;
            BtnClientDisconnect.IsEnabled = false;
            TxtClientHostIp.IsEnabled = true;
            TxtClientPort.IsEnabled = true;
            TxtClientUsername.IsEnabled = true;
            TxtClientPassword.IsEnabled = true;
            BtnDeleteFile.IsEnabled = false;

            Log(LanguageManager.Instance.IsId ? "Terputus dari server." : "Disconnected from server.", LogLevel.Warning);
            TxtStatusMessage.Text = LanguageManager.Instance.ClientDisconnectedMessage;
        }

        private async Task LoadDirectoryAsync(string relativePath)
        {
            if (string.IsNullOrEmpty(_clientConnectedHost)) return;

            TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                ? $"Memuat direktori: /{relativePath}..." 
                : $"Loading directory: /{relativePath}...";

            try
            {
                var response = await _client.BrowseAsync(_clientConnectedHost, _clientConnectedPort, _clientConnectedUser ?? "", _clientConnectedPass ?? "", relativePath);

                if (response != null && response.Success)
                {
                    _clientCurrentPath = response.CurrentPath ?? "";
                    TxtCurrentPathDisplay.Text = string.IsNullOrEmpty(_clientCurrentPath) ? "/" : $"/{_clientCurrentPath}";
                    LstFiles.ItemsSource = response.Items;

                    BtnNavUp.IsEnabled = !string.IsNullOrEmpty(_clientCurrentPath);
                    BtnUploadFile.IsEnabled = response.AllowUpload;
                    BtnDownloadSelected.IsEnabled = response.AllowDownload;
                    BtnDeleteFile.IsEnabled = response.AllowDelete;

                    TxtStatusMessage.Text = LanguageManager.Instance.IsId 
                        ? $"Ditemukan {response.Items.Count} item di /{_clientCurrentPath}" 
                        : $"Found {response.Items.Count} item(s) in /{_clientCurrentPath}";
                }
                else
                {
                    await ShowMessageBoxAsync(
                        LanguageManager.Instance.IsId ? "Kesalahan" : "Error", 
                        LanguageManager.Instance.IsId ? $"Gagal memuat folder: {response?.Message ?? "Respon tidak valid"}" : $"Failed to load folder: {response?.Message ?? "Invalid response"}");
                }
            }
            catch (Exception ex)
            {
                var (errTitle, errMsg) = FormatUserFriendlyError(ex, _clientConnectedHost, _clientConnectedPort);
                await ShowMessageBoxAsync(errTitle, errMsg);
                Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal memuat folder: {errTitle}" : $"[Client] Failed to load directory: {errTitle}", LogLevel.Error);
            }
        }

        private async void LstFiles_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (LstFiles.SelectedItem is FileItem item)
            {
                if (item.IsDirectory)
                {
                    await LoadDirectoryAsync(item.RelativePath);
                }
                else
                {
                    BtnDownloadSelected_Click(sender, new RoutedEventArgs());
                }
            }
        }

        private async void BtnNavUp_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clientCurrentPath)) return;

            string path = _clientCurrentPath.Trim('/', '\\').Replace('\\', '/');
            int lastSlash = path.LastIndexOf('/');
            string parent = lastSlash > 0 ? path.Substring(0, lastSlash) : "";

            await LoadDirectoryAsync(parent);
        }

        private async void BtnNavRefresh_Click(object? sender, RoutedEventArgs e)
        {
            await LoadDirectoryAsync(_clientCurrentPath);
        }

        private void LstFilesItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(sender as Visual);
            if (point.Properties.IsRightButtonPressed)
            {
                if ((sender as Control)?.DataContext is FileItem clickedItem)
                {
                    var selected = LstFiles.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();
                    if (!selected.Contains(clickedItem))
                    {
                        LstFiles.SelectedItems?.Clear();
                        LstFiles.SelectedItems?.Add(clickedItem);
                    }
                }
            }
        }

        private async void BtnDownloadSelected_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clientConnectedHost))
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxNotConnectedTitle, LanguageManager.Instance.MsgBoxNotConnectedDesc);
                return;
            }

            var selectedItems = LstFiles.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();
            if (selectedItems.Count == 0 && LstFiles.SelectedItem is FileItem singleItem)
            {
                selectedItems.Add(singleItem);
            }

            if (selectedItems.Count == 0)
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxSelectFileTitle, 
                    LanguageManager.Instance.IsId ? "Silakan pilih file atau folder yang ingin diunduh terlebih dahulu." : "Please select the file or folder you want to download first.");
                return;
            }

            // Prompt destination folder
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LanguageManager.Instance.SelectDestFolderTitle,
                AllowMultiple = false
            });

            if (folders == null || folders.Count == 0) return;

            string targetDirectory = folders[0].Path.LocalPath;
            _downloadFolder = targetDirectory;

            // Show Transfer Monitor
            PanelTransferMonitor.IsVisible = true;
            BtnOpenDownloadFolder.IsVisible = false;
            TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusDownloading;
            TxtTransferStatus.Foreground = Brush.Parse("#2563EB");
            PrgTransfer.Value = 0;

            _transferCts = new CancellationTokenSource();
            BtnCancelTransfer.IsEnabled = true;

            int downloadedFilesCount = 0;
            var progress = new Progress<TransferProgressInfo>(p =>
            {
                PrgTransfer.Value = p.Percentage;
                TxtTransferDetail.Text = $"{p.FormattedProgress} • {p.FormattedSpeed}";
            });

            try
            {
                int totalItems = selectedItems.Count;
                int itemIndex = 0;

                foreach (var item in selectedItems)
                {
                    if (_transferCts.IsCancellationRequested) break;
                    itemIndex++;
                    string prefix = totalItems > 1 ? $"[{itemIndex}/{totalItems}] " : "";

                    if (item.IsDirectory)
                    {
                        string localSubFolder = Path.Combine(targetDirectory, item.Name);
                        Log(LanguageManager.Instance.IsId 
                            ? $"[Klien] {prefix}Mengunduh folder '{item.Name}'..." 
                            : $"[Client] {prefix}Downloading folder '{item.Name}'...", LogLevel.Info);

                        int count = await DownloadFolderRecursiveAsync(item.RelativePath, localSubFolder, progress, _transferCts.Token);
                        downloadedFilesCount += count;
                    }
                    else
                    {
                        string targetFile = Path.Combine(targetDirectory, item.Name);
                        TxtTransferFileName.Text = $"{prefix}{item.Name}";
                        PrgTransfer.Value = 0;
                        Log(LanguageManager.Instance.IsId 
                            ? $"[Klien] {prefix}Mengunduh '{item.Name}'..." 
                            : $"[Client] {prefix}Downloading '{item.Name}'...", LogLevel.Info);

                        await _client.DownloadFileAsync(
                            _clientConnectedHost,
                            _clientConnectedPort,
                            _clientConnectedUser ?? "",
                            _clientConnectedPass ?? "",
                            item.RelativePath,
                            targetFile,
                            progress,
                            _transferCts.Token
                        );
                        downloadedFilesCount++;
                    }
                }

                if (!_transferCts.IsCancellationRequested)
                {
                    TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusDownloadComplete;
                    TxtTransferStatus.Foreground = Brush.Parse("#059669");
                    BtnOpenDownloadFolder.IsVisible = true;
                    string finishMsg = LanguageManager.Instance.IsId 
                        ? $"Berhasil mengunduh {downloadedFilesCount} file ke '{targetDirectory}'." 
                        : $"Successfully downloaded {downloadedFilesCount} file(s) to '{targetDirectory}'.";
                    Log(finishMsg, LogLevel.Success);
                    TxtStatusMessage.Text = finishMsg;
                }
            }
            catch (OperationCanceledException)
            {
                TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusDownloadCancelled;
                TxtTransferStatus.Foreground = Brush.Parse("#D97706");
                Log(LanguageManager.Instance.IsId ? "[Klien] Unduhan dibatalkan oleh pengguna." : "[Client] Download cancelled by user.", LogLevel.Warning);
            }
            catch (Exception ex)
            {
                TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusDownloadFailed;
                TxtTransferStatus.Foreground = Brush.Parse("#DC2626");
                var (errTitle, errMsg) = FormatUserFriendlyError(ex, _clientConnectedHost, _clientConnectedPort);
                await ShowMessageBoxAsync(errTitle, errMsg);
                Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal download: {errTitle}" : $"[Client] Download failed: {errTitle}", LogLevel.Error);
            }
            finally
            {
                BtnCancelTransfer.IsEnabled = false;
            }
        }

        private async Task<int> DownloadFolderRecursiveAsync(string serverRelativePath, string localFolder, IProgress<TransferProgressInfo> progress, CancellationToken ct)
        {
            if (ct.IsCancellationRequested || string.IsNullOrEmpty(_clientConnectedHost)) return 0;
            if (!Directory.Exists(localFolder))
            {
                try { Directory.CreateDirectory(localFolder); } catch { }
            }

            var browse = await _client.BrowseAsync(
                _clientConnectedHost,
                _clientConnectedPort,
                _clientConnectedUser ?? "",
                _clientConnectedPass ?? "",
                serverRelativePath
            );

            if (!browse.Success || browse.Items == null) return 0;

            int count = 0;
            foreach (var item in browse.Items)
            {
                if (ct.IsCancellationRequested) break;

                if (item.IsDirectory)
                {
                    string subLocal = Path.Combine(localFolder, item.Name);
                    count += await DownloadFolderRecursiveAsync(item.RelativePath, subLocal, progress, ct);
                }
                else
                {
                    string localFile = Path.Combine(localFolder, item.Name);
                    TxtTransferFileName.Text = item.Name;
                    PrgTransfer.Value = 0;

                    await _client.DownloadFileAsync(
                        _clientConnectedHost,
                        _clientConnectedPort,
                        _clientConnectedUser ?? "",
                        _clientConnectedPass ?? "",
                        item.RelativePath,
                        localFile,
                        progress,
                        ct
                    );
                    count++;
                }
            }
            return count;
        }

        private async void BtnDeleteFile_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clientConnectedHost)) return;

            var selectedItems = LstFiles.SelectedItems?.OfType<FileItem>().ToList() ?? new List<FileItem>();
            if (selectedItems.Count == 0 && LstFiles.SelectedItem is FileItem singleItem)
            {
                selectedItems.Add(singleItem);
            }

            if (selectedItems.Count == 0)
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxSelectFileTitle, 
                    LanguageManager.Instance.IsId ? "Silakan pilih file atau folder yang ingin Anda hapus terlebih dahulu." : "Please select the file or folder you want to delete first.");
                return;
            }

            string prompt = selectedItems.Count == 1
                ? string.Format(LanguageManager.Instance.ConfirmDeleteMsg, selectedItems[0].Name)
                : string.Format(LanguageManager.Instance.ConfirmDeleteMultipleMsg, selectedItems.Count);

            bool confirmed = await ShowConfirmationBoxAsync(LanguageManager.Instance.ConfirmDeleteTitle, prompt);
            if (!confirmed) return;

            int deletedCount = 0;
            foreach (var item in selectedItems)
            {
                try
                {
                    await _client.DeleteFileAsync(_clientConnectedHost, _clientConnectedPort, _clientConnectedUser ?? "", _clientConnectedPass ?? "", item.RelativePath);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    var (errTitle, errMsg) = FormatUserFriendlyError(ex, _clientConnectedHost, _clientConnectedPort);
                    await ShowMessageBoxAsync(errTitle, errMsg);
                    Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal menghapus '{item.Name}': {errTitle}" : $"[Client] Failed to delete '{item.Name}': {errTitle}", LogLevel.Error);
                    break;
                }
            }

            if (deletedCount > 0)
            {
                string successMsg = selectedItems.Count == 1 
                    ? string.Format(LanguageManager.Instance.FileDeletedSuccess, selectedItems[0].Name)
                    : string.Format(LanguageManager.Instance.ItemsDeletedSuccess, deletedCount);
                Log(successMsg, LogLevel.Success);
                await LoadDirectoryAsync(_clientCurrentPath);
            }
        }

        #region Drag & Drop and Upload Actions

        private void LstFiles_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                DropOverlay.IsVisible = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
                DropOverlay.IsVisible = false;
            }
        }

        private void LstFiles_DragLeave(object? sender, DragEventArgs e)
        {
            DropOverlay.IsVisible = false;
        }

        private async void LstFiles_Drop(object? sender, DragEventArgs e)
        {
            DropOverlay.IsVisible = false;

            if (!e.Data.Contains(DataFormats.Files)) return;

            var droppedPaths = new System.Collections.Generic.List<string>();
            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    string? localPath = file.TryGetLocalPath();
                    if (string.IsNullOrEmpty(localPath))
                    {
                        localPath = file.Path?.LocalPath;
                    }
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        droppedPaths.Add(localPath);
                    }
                }
            }

            if (droppedPaths.Count > 0)
            {
                await UploadFilesAndFoldersAsync(droppedPaths);
            }
        }

        private async void BtnUploadFile_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clientConnectedHost))
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxNotConnectedTitle, LanguageManager.Instance.MsgBoxNotConnectedDesc);
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LanguageManager.Instance.IsId ? "Pilih File untuk Diunggah ke Host" : "Select Files to Upload to Host",
                AllowMultiple = true
            });

            if (files == null || files.Count == 0) return;

            var paths = new System.Collections.Generic.List<string>();
            foreach (var f in files)
            {
                string? localPath = f.TryGetLocalPath() ?? f.Path?.LocalPath;
                if (!string.IsNullOrEmpty(localPath))
                {
                    paths.Add(localPath);
                }
            }

            if (paths.Count > 0)
            {
                await UploadFilesAndFoldersAsync(paths);
            }
        }

        private async Task UploadFilesAndFoldersAsync(System.Collections.Generic.IEnumerable<string> paths)
        {
            if (string.IsNullOrEmpty(_clientConnectedHost))
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxNotConnectedTitle, LanguageManager.Instance.MsgBoxNotConnectedDesc);
                return;
            }

            var fileList = new System.Collections.Generic.List<string>();
            foreach (var p in paths)
            {
                if (File.Exists(p))
                {
                    fileList.Add(p);
                }
                else if (Directory.Exists(p))
                {
                    try
                    {
                        var filesInDir = Directory.GetFiles(p, "*", SearchOption.AllDirectories);
                        fileList.AddRange(filesInDir);
                    }
                    catch (Exception ex)
                    {
                        Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal membaca direktori '{p}': {ex.Message}" : $"[Client] Failed to read directory '{p}': {ex.Message}", LogLevel.Warning);
                    }
                }
            }

            if (fileList.Count == 0)
            {
                await ShowMessageBoxAsync(LanguageManager.Instance.MsgBoxEmptyFilesTitle, LanguageManager.Instance.MsgBoxEmptyFilesDesc);
                return;
            }

            // Show Transfer Monitor with progress bar (hide open downloads folder during upload)
            PanelTransferMonitor.IsVisible = true;
            BtnOpenDownloadFolder.IsVisible = false;
            _transferCts = new CancellationTokenSource();
            BtnCancelTransfer.IsEnabled = true;

            int totalFiles = fileList.Count;
            int uploadedCount = 0;

            for (int i = 0; i < totalFiles; i++)
            {
                if (_transferCts.IsCancellationRequested) break;

                string localFile = fileList[i];
                string fileName = Path.GetFileName(localFile);
                string fileCounter = totalFiles > 1 ? $" ({i + 1}/{totalFiles})" : "";

                TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusUploading;
                TxtTransferStatus.Foreground = Brush.Parse("#2563EB");
                TxtTransferFileName.Text = fileName + fileCounter;
                PrgTransfer.Value = 0;
                TxtTransferDetail.Text = LanguageManager.Instance.IsId ? "Mempersiapkan pengunggahan..." : "Preparing upload...";

                var progress = new Progress<TransferProgressInfo>(p =>
                {
                    PrgTransfer.Value = p.Percentage;
                    TxtTransferDetail.Text = $"{p.FormattedProgress} • {p.FormattedSpeed}";

                    if (p.IsCompleted)
                    {
                        TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusUploadComplete;
                        TxtTransferStatus.Foreground = Brush.Parse("#059669");
                    }
                });

                try
                {
                    Log(LanguageManager.Instance.IsId ? $"[Klien] Mengunggah '{fileName}'{fileCounter}..." : $"[Client] Uploading '{fileName}'{fileCounter}...", LogLevel.Info);
                    await _client.UploadFileAsync(
                        _clientConnectedHost,
                        _clientConnectedPort,
                        _clientConnectedUser ?? "",
                        _clientConnectedPass ?? "",
                        localFile,
                        _clientCurrentPath,
                        progress,
                        _transferCts.Token
                    );
                    uploadedCount++;
                }
                catch (OperationCanceledException)
                {
                    TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusUploadCancelled;
                    TxtTransferStatus.Foreground = Brush.Parse("#D97706");
                    Log(LanguageManager.Instance.IsId ? "[Klien] Pengunggahan dibatalkan oleh pengguna." : "[Client] Upload cancelled by user.", LogLevel.Warning);
                    break;
                }
                catch (Exception ex)
                {
                    TxtTransferStatus.Text = LanguageManager.Instance.TransferStatusUploadFailed;
                    TxtTransferStatus.Foreground = Brush.Parse("#DC2626");
                    Log(LanguageManager.Instance.IsId ? $"[Klien] Gagal mengunggah '{fileName}': {ex.Message}" : $"[Client] Failed to upload '{fileName}': {ex.Message}", LogLevel.Error);
                }
            }

            BtnCancelTransfer.IsEnabled = false;

            // Refresh directory listing to show new files
            await LoadDirectoryAsync(_clientCurrentPath);

            if (uploadedCount > 0)
            {
                Log(LanguageManager.Instance.IsId ? $"[Klien] Berhasil mengunggah {uploadedCount} dari {totalFiles} file ke host." : $"[Client] Successfully uploaded {uploadedCount} of {totalFiles} files to host.", LogLevel.Success);
                TxtStatusMessage.Text = string.Format(LanguageManager.Instance.UploadCompleteMessage, $"{uploadedCount} files");
            }
        }

        #endregion

        private void BtnCancelTransfer_Click(object? sender, RoutedEventArgs e)
        {
            _transferCts?.Cancel();
        }

        private void BtnOpenDownloadFolder_Click(object? sender, RoutedEventArgs e)
        {
            NetworkHelper.OpenFolder(_downloadFolder);
        }

        #endregion

        #region Setting Actions

        private void BtnOpenFolderHost_Click(object? sender, RoutedEventArgs e)
        {
            string? path = TxtSharedFolderPath.Text;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                NetworkHelper.OpenFolder(path);
            }
        }

        private void BtnOpenSharedFolderSettings_Click(object? sender, RoutedEventArgs e)
        {
            string? path = TxtSettingSharedPath.Text;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                NetworkHelper.OpenFolder(path);
            }
        }

        private async void BtnChangeSharedFolder_Click(object? sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LanguageManager.Instance.IsId ? "Pilih Folder Default yang Ingin Dibagikan" : "Select Default Folder to Share",
                AllowMultiple = false
            });

            if (folders.Count > 0)
            {
                string sel = folders[0].Path.LocalPath;
                TxtSharedFolderPath.Text = sel;
                TxtSettingSharedPath.Text = sel;
                Log(string.Format(LanguageManager.Instance.SharedFolderChangedMsg, sel), LogLevel.Info);
            }
        }

        private void BtnAddUser_Click(object? sender, RoutedEventArgs e)
        {
            int nextNumber = _userAccounts.Count + 1;
            var newUser = new UserAccount
            {
                Username = $"user{nextNumber}",
                Password = "user123",
                CanRead = true,
                CanWrite = false,
                CanDelete = false
            };
            _userAccounts.Add(newUser);
            Log(LanguageManager.Instance.IsId 
                ? $"[Pengguna] User baru '{newUser.Username}' ditambahkan ke daftar." 
                : $"[User] New user '{newUser.Username}' added to list.", LogLevel.Info);
        }

        private void BtnDeleteUserRow_Click(object? sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is UserAccount user)
            {
                if (user.IsAdmin)
                {
                    _ = ShowMessageBoxAsync(LanguageManager.Instance.IsId ? "Peringatan" : "Warning", LanguageManager.Instance.CannotDeleteAdminMsg);
                    return;
                }

                _userAccounts.Remove(user);
                Log(LanguageManager.Instance.IsId 
                    ? $"[Pengguna] User '{user.Username}' dihapus dari daftar." 
                    : $"[User] User '{user.Username}' removed from list.", LogLevel.Info);
            }
        }

        private async void BtnSaveSettings_Click(object? sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtSettingDefaultPort.Text?.Trim(), out int port) && port > 0 && port <= 65535)
            {
                TxtPort.Text = port.ToString();
            }

            // Ensure admin exists with default all checked if missing
            var admin = _userAccounts.FirstOrDefault(u => u.IsAdmin);
            if (admin == null)
            {
                admin = new UserAccount
                {
                    Username = "admin",
                    Password = "admin",
                    CanRead = true,
                    CanWrite = true,
                    CanDelete = true
                };
                _userAccounts.Insert(0, admin);
            }

            TxtUsername.Text = admin.Username;
            TxtPassword.Text = admin.Password;
            ChkAllowDownload.IsChecked = admin.CanRead;
            ChkAllowUpload.IsChecked = admin.CanWrite;

            SaveSettings();

            // Apply immediately to server if running
            _server.Users = _userAccounts.ToList();

            await HandlePortChangedAsync();

            await ShowMessageBoxAsync(LanguageManager.Instance.IsId ? "Pengaturan Disimpan" : "Settings Saved", 
                LanguageManager.Instance.IsId ? "Pengaturan dan daftar hak akses pengguna telah berhasil disimpan!" : "Settings and user accounts have been saved successfully!");
            Log(LanguageManager.Instance.IsId ? "Pengaturan aplikasi dan daftar akun berhasil disimpan." : "Settings and user accounts saved successfully.", LogLevel.Success);
        }

        private void BtnResetSettings_Click(object? sender, RoutedEventArgs e)
        {
            TxtSettingDefaultPort.Text = "5050";
            TxtPort.Text = "5050";
            ChkMinimizeToTrayOnClose.IsChecked = true;

            _userAccounts.Clear();
            _userAccounts.Add(new UserAccount
            {
                Username = "admin",
                Password = "admin",
                CanRead = true,
                CanWrite = true,
                CanDelete = true
            });

            InitializeDefaultSharedFolder();
            TxtSettingSharedPath.Text = TxtSharedFolderPath.Text;

            string userProf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _downloadFolder = Path.Combine(userProf, "Downloads", "FilanShare");
            if (!Directory.Exists(_downloadFolder)) try { Directory.CreateDirectory(_downloadFolder); } catch { }

            SaveSettings();
            _server.Users = _userAccounts.ToList();

            Log(LanguageManager.Instance.SettingsRestoredMsg, LogLevel.Info);
        }

        #endregion

        #region About Actions

        private void BtnOpenAppFolder_Click(object? sender, RoutedEventArgs e)
        {
            NetworkHelper.OpenFolder(AppDomain.CurrentDomain.BaseDirectory);
        }

        private async void BtnCopySysInfo_Click(object? sender, RoutedEventArgs e)
        {
            string sysInfo = $"FilanShare {FileSender.Helpers.AppVersion.Value}\n" +
                             $"OS: {TxtAboutOs.Text}\n" +
                             $"Architecture: {TxtAboutArch.Text}\n" +
                             $"App Directory: {TxtAboutAppPath.Text}\n" +
                             $".NET: {RuntimeInformation.FrameworkDescription}";

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(sysInfo);
                await ShowMessageBoxAsync(LanguageManager.Instance.CopiedTitle, LanguageManager.Instance.SysInfoCopiedMsg);
            }
        }

        #endregion

        #region Logs & Clean Up

        private async void BtnCopyLogs_Click(object? sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var log in _logs)
            {
                sb.AppendLine($"[{log.FormattedTime}] [{log.Level}] {log.Message}");
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(sb.ToString());
                await ShowMessageBoxAsync(LanguageManager.Instance.CopiedTitle, LanguageManager.Instance.LogsCopiedMsg);
            }
        }

        private void BtnClearLogs_Click(object? sender, RoutedEventArgs e)
        {
            _logs.Clear();
            Log(LanguageManager.Instance.LogsClearedMsg, LogLevel.Info);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.IsVisible = false;
                    _trayIcon.Dispose();
                }
                _server.Stop();
            }
            catch { }
        }

        #endregion

        #region System Tray

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new TrayIcon
                {
                    Icon = this.Icon,
                    ToolTipText = $"FilanShare ({FileSender.Helpers.AppVersion.Value})",
                    IsVisible = true
                };

                _trayIcon.Clicked += (s, e) => RestoreFromTray();

                var menu = new NativeMenu();

                _trayItemOpen = new NativeMenuItem(LanguageManager.Instance.TrayOpenApp);
                _trayItemOpen.Click += (s, e) => RestoreFromTray();
                menu.Items.Add(_trayItemOpen);

                menu.Items.Add(new NativeMenuItemSeparator());

                _trayItemExit = new NativeMenuItem(LanguageManager.Instance.TrayExitApp);
                _trayItemExit.Click += (s, e) =>
                {
                    _isExplicitExit = true;
                    try { _server.Stop(); } catch { }
                    Close();
                };
                menu.Items.Add(_trayItemExit);

                _trayIcon.Menu = menu;

                var icons = new TrayIcons { _trayIcon };
                TrayIcon.SetIcons(Application.Current!, icons);
            }
            catch (Exception ex)
            {
                Log($"[Tray] Inisialisasi system tray: {ex.Message}", LogLevel.Warning);
            }
        }

        public void RestoreFromTray()
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsVisible = true;
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();
            });
        }

        #endregion

        #region Lifecycle & Connected Clients

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (!_isExplicitExit)
            {
                if (ChkMinimizeToTrayOnClose.IsChecked == true)
                {
                    e.Cancel = true;
                    Hide();
                    Log(LanguageManager.Instance.IsId 
                        ? "Aplikasi diminimalkan ke System Tray (Server tetap aktif di latar belakang)." 
                        : "Application minimized to System Tray (Server remains active in background).", LogLevel.Info);
                }
                else
                {
                    _isExplicitExit = true;
                    try { _server.Stop(); } catch { }
                }
            }
        }

        private void BtnExitApp_Click(object? sender, RoutedEventArgs e)
        {
            _isExplicitExit = true;
            try { _server.Stop(); } catch { }
            Close();
        }

        private void Server_OnClientActivity(string clientIp, string action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    ConnectedClientItem? existing = null;
                    foreach (var c in _connectedClients)
                    {
                        if (string.Equals(c.IpAddress, clientIp, StringComparison.OrdinalIgnoreCase))
                        {
                            existing = c;
                            break;
                        }
                    }

                    if (existing != null)
                    {
                        existing.LastConnected = DateTime.Now;
                        existing.LastAction = action;
                        int idx = _connectedClients.IndexOf(existing);
                        if (idx > 0)
                        {
                            _connectedClients.Move(idx, 0);
                        }
                    }
                    else
                    {
                        var newClient = new ConnectedClientItem
                        {
                            IpAddress = clientIp,
                            DeviceName = clientIp,
                            LastConnected = DateTime.Now,
                            LastAction = action
                        };
                        _connectedClients.Insert(0, newClient);

                        // Asynchronously resolve hostname
                        Task.Run(() =>
                        {
                            try
                            {
                                var hostEntry = System.Net.Dns.GetHostEntry(clientIp);
                                if (!string.IsNullOrWhiteSpace(hostEntry.HostName))
                                {
                                    Dispatcher.UIThread.Post(() =>
                                    {
                                        newClient.DeviceName = hostEntry.HostName;
                                    });
                                }
                            }
                            catch { }
                        });
                    }

                    UpdateConnectedClientsUi();
                }
                catch { }
            });
        }

        private void UpdateConnectedClientsUi()
        {
            bool hasClients = _connectedClients.Count > 0;
            PanelNoConnectedDevices.IsVisible = !hasClients;
            LstConnectedDevices.IsVisible = hasClients;
            TxtConnectedCount.Text = string.Format(LanguageManager.Instance.DevicesCountFormat, _connectedClients.Count);
        }

        private void BtnClearConnectedList_Click(object? sender, RoutedEventArgs e)
        {
            _connectedClients.Clear();
            UpdateConnectedClientsUi();
            Log(LanguageManager.Instance.HistoryClearedMsg, LogLevel.Info);
        }

        #endregion

        #region Language Localization

        private void ApplyLanguage()
        {
            var l = LanguageManager.Instance;

            LblAppSubtitle.Text = l.AppSubtitle;
            BtnMenuFile.Content = l.MenuFile;
            BtnMenuSetting.Content = l.MenuSetting;
            BtnMenuAbout.Content = l.MenuAbout;

            TxtServerStatusBadge.Text = _server.IsRunning ? l.ServerOnline : l.ServerOffline;

            // Sub Tabs
            BtnSubTabHost.Content = l.TabHost;
            BtnSubTabClient.Content = l.TabClient;

            // Host Mode
            LblSharedFolderHeader.Text = l.SharedFolderHeader;
            BtnBrowseFolder.Content = l.BrowseFolderBtn;
            LblNetworkAuthHeader.Text = l.NetworkAuthHeader;
            LblPort.Text = l.PortLabel;
            BtnFixFirewall.Content = l.FirewallBtnOpen;
            if (_lastCheckedFirewallPort > 0)
            {
                UpdateFirewallUi(_lastCheckedFirewallPort, _isFirewallOpen);
            }
            LblUsername.Text = l.UsernameLabel;
            LblPassword.Text = l.PasswordLabel;
            UpdatePasswordDisplay();
            BtnGeneratePass.Content = l.RandomPassBtn;
            ChkAllowDownload.Content = l.AllowDownloadChk;
            ChkAllowUpload.Content = l.AllowUploadChk;
            BtnToggleServer.Content = _server.IsRunning ? l.StopSharingBtn : l.StartSharingBtn;

            LblServerStoppedTitle.Text = l.ServerStoppedTitle;
            LblServerStoppedDesc.Text = l.ServerStoppedDesc;
            LblStealthNotice.Text = l.StealthNotice;

            LblServerActiveBanner.Text = l.ServerActiveBanner;
            LblCredentialsTitle.Text = l.CredentialsTitle;
            TxtAllInterfacesTag.Text = l.AllInterfacesTag;
            LblAllInterfacesNotice.Text = l.AllInterfacesNotice;
            LblCredHostIp.Text = l.IsId ? "IP Host:" : "Host IP:";
            LblCredPort.Text = l.IsId ? "Port:" : "Port:";
            LblCredUser.Text = l.IsId ? "Username:" : "Username:";
            LblCredPass.Text = l.IsId ? "Password:" : "Password:";
            BtnCopyCredentials.Content = l.CopyCredentialsBtn;

            // Komputer Terhubung
            LblConnectedDevicesTitle.Text = l.ConnectedDevicesHeader;
            LblNoConnectedDevices.Text = l.NoConnectedDevicesText;
            BtnClearConnectedList.Content = l.ClearConnectedBtn;
            TxtConnectedCount.Text = string.Format(l.DevicesCountFormat, _connectedClients.Count);

            // Client Mode
            LblClientHostIp.Text = l.HostIpLabel;
            LblClientPort.Text = l.PortLabel;
            LblClientUser.Text = l.UsernameLabel;
            LblClientPass.Text = l.PasswordLabel;
            BtnClientConnect.Content = l.ConnectBtn;
            BtnClientDisconnect.Content = l.DisconnectBtn;

            BtnNavUp.Content = l.NavUpBtn;
            BtnNavRefresh.Content = l.NavRefreshBtn;
            LblLocationTag.Text = l.IsId ? "Lokasi: " : "Location: ";
            BtnDownloadSelected.Content = l.DownloadSelectedBtn;
            BtnUploadFile.Content = l.UploadFileBtn;
            BtnDeleteFile.Content = l.DeleteSelectedBtn;

            LblColName.Text = l.ColName;
            LblColType.Text = l.ColType;
            LblColSize.Text = l.ColSize;
            LblColModified.Text = l.ColModified;

            BtnCancelTransfer.Content = l.CancelBtn;
            BtnOpenDownloadFolder.Content = l.OpenDownloadFolderBtn;

            CtxMenuDownload.Header = l.ContextDownload;
            CtxMenuDelete.Header = l.ContextDelete;
            CtxMenuRefresh.Header = l.ContextRefresh;

            // Setting
            LblSettingTitle.Text = l.SettingTitle;
            LblSettingDesc.Text = l.SettingDesc;
            LblSettingLocations.Text = l.StorageLocationsTitle;
            LblSettingSharedDesc.Text = l.SettingSharedFolderDesc;
            BtnChangeSharedFolder.Content = l.ChangeBtn;
            BtnOpenSharedFolderSettings.Content = l.OpenFolderBtn;
            BtnOpenFolderHost.Content = l.OpenFolderBtn;

            LblSettingParams.Text = l.DefaultServerParamsTitle;
            LblSettingDefaultPort.Text = l.DefaultPortLabel;
            LblUserListTitle.Text = l.UserListTitle;
            LblUserListDesc.Text = l.UserListDesc;
            BtnAddUser.Content = l.AddUserBtn;
            LblColUser.Text = l.ColUser;
            LblColPass.Text = l.ColPass;
            LblColRead.Text = l.ColRead;
            LblColWrite.Text = l.ColWrite;
            LblColDelete.Text = l.ColDelete;
            LblColAction.Text = l.ColAction;
            BtnSaveSettings.Content = l.SaveSettingsBtn;
            BtnResetSettings.Content = l.ResetSettingsBtn;

            // Perilaku Aplikasi & System Tray
            LblAppBehaviorTitle.Text = l.AppBehaviorTitle;
            LblAutoMinimizeNote.Text = l.AutoMinimizeNote;
            ChkMinimizeToTrayOnClose.Content = l.MinimizeToTrayChk;
            BtnExitApp.Content = l.ExitAppBtn;
            if (_trayItemOpen != null) _trayItemOpen.Header = l.TrayOpenApp;
            if (_trayItemExit != null) _trayItemExit.Header = l.TrayExitApp;
            if (_trayIcon != null) _trayIcon.ToolTipText = $"FilanShare ({FileSender.Helpers.AppVersion.Value})";

            // About
            LblAboutVersion.Text = l.AboutVersion;
            LblAboutDesc.Text = l.AboutDesc;
            LblAboutFeatures.Text = l.FeaturesTitle;
            LblAboutSysInfo.Text = l.SystemInfoTitle;
            LblAboutOsTag.Text = l.OsLabel;
            LblAboutFrameworkTag.Text = l.FrameworkLabel;
            LblAboutArchTag.Text = l.ArchLabel;
            LblAboutAppPathTag.Text = l.AppFolderLabel;
            BtnOpenAppFolder.Content = l.OpenAppFolderBtn;
            BtnCopySysInfo.Content = l.CopySysInfoBtn;

            // Overlay & About Features
            TxtDropTitle.Text = l.DropOverlayTitle;
            TxtDropDesc.Text = l.DropOverlayDesc;
            TxtAboutFeature1.Text = l.AboutFeature1;
            TxtAboutFeature2.Text = l.AboutFeature2;
            TxtAboutFeature3.Text = l.AboutFeature3;
            TxtAboutFeature4.Text = l.AboutFeature4;

            // Footer version
            TxtFooterVersion.Text = $"FilanShare {FileSender.Helpers.AppVersion.Value} •";

            // Refresh badges for connected devices
            foreach (var client in _connectedClients)
            {
                client.RefreshLanguage();
            }

            // Update Flag Switcher visual selection
            if (l.IsId)
            {
                BtnLangID.Classes.Clear(); BtnLangID.Classes.Add("primary");
                BtnLangEN.Classes.Clear(); BtnLangEN.Classes.Add("outline");
            }
            else
            {
                BtnLangID.Classes.Clear(); BtnLangID.Classes.Add("outline");
                BtnLangEN.Classes.Clear(); BtnLangEN.Classes.Add("primary");
            }
        }

        private void BtnLangID_Click(object? sender, RoutedEventArgs e)
        {
            LanguageManager.Instance.SetLanguage("ID");
            Log("Bahasa antarmuka diubah ke Bahasa Indonesia (ID).", LogLevel.Info);
        }

        private void BtnLangEN_Click(object? sender, RoutedEventArgs e)
        {
            LanguageManager.Instance.SetLanguage("EN");
            Log("Interface language changed to English (EN).", LogLevel.Info);
        }

        #endregion

        #region Event Log Panel

        private void BtnToggleEventLog_Click(object? sender, RoutedEventArgs e)
        {
            _eventLogExpanded = !_eventLogExpanded;
            PanelEventLog.IsVisible = _eventLogExpanded;
            TxtEventLogArrow.Text = _eventLogExpanded ? "▲" : "▼";
            if (_eventLogExpanded)
            {
                EventLogScrollViewer.ScrollToEnd();
            }
        }

        private async void BtnEventLogCopyAll_Click(object? sender, RoutedEventArgs e)
        {
            if (_eventLog.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (var entry in _eventLog)
            {
                sb.AppendLine($"[{entry.FormattedTimeFull}] [{entry.LevelLabel.Trim()}] {entry.Message}");
            }
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(sb.ToString());
                TxtEventLogLastMsg.Text = $"› {_eventLog.Count} log entries copied to clipboard.";
            }
        }

        private void BtnEventLogClear_Click(object? sender, RoutedEventArgs e)
        {
            _eventLog.Clear();
            _elCountInfo = _elCountSuccess = _elCountWarn = _elCountError = 0;
            TxtLogCountInfo.Text    = "INFO: 0";
            TxtLogCountSuccess.Text = "OK: 0";
            TxtLogCountWarn.Text    = "WARN: 0";
            TxtLogCountError.Text   = "ERR: 0";
            TxtEventLogLastMsg.Text = "";
        }

        #endregion
    }
}
