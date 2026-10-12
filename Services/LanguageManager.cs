using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FileSender.Services
{
    public class LanguageManager : INotifyPropertyChanged
    {
        public static LanguageManager Instance { get; } = new LanguageManager();

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? LanguageChanged;

        private string _currentLang = "EN"; // "ID" atau "EN" (Default English)

        public string CurrentLang
        {
            get => _currentLang;
            set
            {
                if (_currentLang != value)
                {
                    _currentLang = value;
                    OnPropertyChanged("");
                    LanguageChanged?.Invoke();
                }
            }
        }

        public bool IsId => _currentLang == "ID";

        public void SetLanguage(string lang)
        {
            CurrentLang = lang.ToUpperInvariant();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ==========================================
        // DAFTAR ISTILAH / STRINGS (MUDAH DIUBAH)
        // ==========================================

        // Header & Menu Utama
        public string AppTitle => "FilanShare";
        public string AppSubtitle => IsId ? "Stealth P2P Transfer • Windows & Linux" : "Stealth P2P Transfer • Windows & Linux";
        public string MenuFile => IsId ? "📁 File" : "📁 File";
        public string MenuSetting => IsId ? "⚙️ Setting" : "⚙️ Settings";
        public string MenuAbout => IsId ? "ℹ️ About" : "ℹ️ About";
        public string ServerOffline => IsId ? "Server: Offline" : "Server: Offline";
        public string ServerOnline => IsId ? "Server: Online" : "Server: Online";

        // Sub Tabs di Menu File
        public string TabHost => IsId ? "📡 Mode Host (Bagikan)" : "📡 Host Mode (Share)";
        public string TabClient => IsId ? "💻 Mode Klien (Jelajahi)" : "💻 Client Mode (Browse)";
        public string TabLogs => IsId ? "📋 Log Aktivitas" : "📋 Activity Logs";

        // Host Mode (Server)
        public string SharedFolderHeader => IsId ? "📁 Folder yang Ingin Dibagikan" : "📁 Folder to Share";
        public string SharedFolderDesc => IsId ? "Folder ini dapat diakses oleh perangkat lain di jaringan LAN." : "This folder can be accessed by other devices on the LAN.";
        public string BrowseFolderBtn => IsId ? "Pilih Folder..." : "Browse Folder...";
        public string NetworkAuthHeader => IsId ? "🔐 Pengaturan Jaringan & Autentikasi" : "🔐 Network & Auth Settings";
        public string LocalIpLabel => IsId ? "IP Address Perangkat Ini:" : "This Device IP Address:";
        public string PortLabel => IsId ? "Port Server:" : "Server Port:";
        public string UsernameLabel => IsId ? "Username:" : "Username:";
        public string PasswordLabel => IsId ? "Password:" : "Password:";
        public string ShowPassBtn => IsId ? "👁 Tampilkan" : "👁 Show";
        public string HidePassBtn => IsId ? "🙈 Sembunyikan" : "🙈 Hide";
        public string RandomPassBtn => IsId ? "🎲 Password Acak" : "🎲 Random Pass";
        public string ClientPermissionsLabel => IsId ? "Hak Akses Client:" : "Client Permissions:";
        public string AllowDownloadChk => IsId ? "Izinkan Unduh (Download)" : "Allow Download";
        public string AllowUploadChk => IsId ? "Izinkan Kirim Balik (Upload)" : "Allow Upload";
        public string StartSharingBtn => IsId ? "🚀 MULAI SHARING" : "🚀 START SHARING";
        public string StopSharingBtn => IsId ? "⏹ HENTIKAN SHARING" : "⏹ STOP SHARING";

        public string ServerStoppedTitle => IsId ? "Server Sedang Tidak Aktif" : "Server is Stopped";
        public string ServerStoppedDesc => IsId ? "Pilih folder di sebelah kiri dan klik 'MULAI SHARING'\nuntuk mengaktifkan server di jaringan LAN." : "Select a folder on the left and click 'START SHARING'\nto activate server on local network.";
        public string StealthNotice => IsId ? "🔒 Keamanan Stealth: Server ini tidak memancarkan beacon UDP/mDNS sehingga perangkat Anda aman dan tidak terdeteksi otomatis oleh orang lain di LAN." : "🔒 Stealth Security: Server does not emit UDP/mDNS beacons, keeping your device hidden from LAN auto-discovery scanners.";

        public string ServerActiveBanner => IsId ? "SERVER AKTIF & SIAP DIAKSES" : "SERVER ACTIVE & READY";
        public string CredentialsTitle => IsId ? "Informasi untuk dimasukkan di Klien (PC A):" : "Information for Client (PC A):";
        public string AllInterfacesTag => IsId ? "🌐 Semua Interface (0.0.0.0)" : "🌐 All Interfaces (0.0.0.0)";
        public string AllInterfacesNotice => IsId ? "🌐 Terbuka di semua interface (0.0.0.0). Pilih IP di atas sesuai koneksi klien:" : "🌐 Open on all interfaces (0.0.0.0). Select IP above matching client connection:";
        public string CopyCredentialsBtn => IsId ? "📋 Salin Kredensial Lengkap" : "📋 Copy Full Credentials";
        public string WebAccessTitle => IsId ? "Atau Akses Cepat via Web Browser:" : "Or Quick Access via Web Browser:";
        public string WebAccessDesc => IsId ? "Perangkat tanpa aplikasi bisa download langsung lewat browser:" : "Devices without the app can download directly via browser:";
        public string OpenBrowserBtn => IsId ? "🌐 Buka di Browser" : "🌐 Open in Browser";
        public string CopyWebUrlBtn => IsId ? "📋 Salin URL" : "📋 Copy URL";

        // Komputer Terhubung (Host Mode)
        public string ConnectedDevicesHeader => IsId ? "💻 Komputer yang Terakhir Terhubung" : "💻 Recently Connected Computers";
        public string ConnectedDevicesDesc => IsId ? "Daftar perangkat di LAN yang baru saja mengakses atau mendownload/upload file dari server ini." : "List of devices on LAN that recently accessed or downloaded/uploaded files from this server.";
        public string NoConnectedDevicesText => IsId ? "Belum ada komputer yang terhubung. Komputer yang mengakses server akan otomatis muncul di sini." : "No computers connected yet. Devices accessing the server will automatically appear here.";
        public string ClearConnectedBtn => IsId ? "🧹 Bersihkan Riwayat" : "🧹 Clear History";
        public string DevicesCountFormat => IsId ? "{0} Perangkat" : "{0} Devices";

        // Client Mode
        public string HostIpLabel => IsId ? "IP Host (PC B):" : "Host IP (PC B):";
        public string ConnectBtn => IsId ? "⚡ HUBUNGKAN" : "⚡ CONNECT";
        public string DisconnectBtn => IsId ? "Putus" : "Disconnect";
        public string NavUpBtn => IsId ? "⬆ Folder Atas" : "⬆ Up Folder";
        public string NavRefreshBtn => IsId ? "🔄 Muat Ulang" : "🔄 Refresh";
        public string DownloadSelectedBtn => IsId ? "⬇ Unduh File Terpilih" : "⬇ Download Selected";
        public string UploadFileBtn => IsId ? "⬆ Upload File" : "⬆ Upload File";
        public string ColName => IsId ? "Nama" : "Name";
        public string ColType => IsId ? "Tipe" : "Type";
        public string ColSize => IsId ? "Ukuran" : "Size";
        public string ColModified => IsId ? "Waktu Modifikasi" : "Last Modified";
        public string DownloadingStatus => IsId ? "Mengunduh: " : "Downloading: ";
        public string UploadingStatus => IsId ? "Mengunggah: " : "Uploading: ";
        public string CancelBtn => IsId ? "Batal" : "Cancel";
        public string OpenDownloadFolderBtn => IsId ? "📁 Buka Folder Unduhan" : "📁 Open Downloads Folder";

        // Logs
        public string LogsTitle => IsId ? "📋 Riwayat & Catatan Aktivitas Jaringan" : "📋 Network & Activity Logs";
        public string CopyLogsBtn => IsId ? "📋 Salin Semua" : "📋 Copy All";
        public string ClearLogsBtn => IsId ? "🧹 Bersihkan Log" : "🧹 Clear Logs";

        // Settings
        public string SettingTitle => IsId ? "⚙️ Pengaturan Aplikasi" : "⚙️ Application Settings";
        public string SettingDesc => IsId ? "Sesuaikan preferensi direktori default, port, dan hak akses untuk FilanShare." : "Customize default folder directories, port, and permissions for FilanShare.";
        public string StorageLocationsTitle => IsId ? "📁 Folder Berbagi Server (Shared Folder)" : "📁 Server Shared Folder";
        public string SettingSharedFolderDesc => IsId ? "Folder ini adalah lokasi penyimpanan file yang dibagikan oleh server. Anda dapat langsung copy-paste file ke folder ini di File Explorer." : "This folder is the storage location shared across local network. You can copy-paste files directly into this folder in File Explorer.";
        public string OpenFolderBtn => IsId ? "📂 Buka Folder" : "📂 Open Folder";
        public string ChangeBtn => IsId ? "Ubah..." : "Change...";
        public string DefaultServerParamsTitle => IsId ? "🌐 Parameter Server & Keamanan Default" : "🌐 Default Server & Security Settings";
        public string DefaultPortLabel => IsId ? "Port Server Default:" : "Default Server Port:";
        public string DefaultUsernameLabel => IsId ? "Username Default:" : "Default Username:";
        public string DefaultPermissionsLabel => IsId ? "Hak Akses Otomatis saat Server Dimulai:" : "Default Permissions on Start:";
        public string UserListTitle => IsId ? "👥 Daftar Pengguna & Hak Akses" : "👥 User Accounts & Permissions";
        public string UserListDesc => IsId ? "Kelola akun pengguna beserta izin Baca (Read), Tulis (Write), dan Hapus (Delete). User 'admin' default memiliki semua izin." : "Manage user accounts with Read, Write, and Delete permissions. 'admin' user has all permissions by default.";
        public string ColUser => "Username";
        public string ColPass => "Password";
        public string ColRead => "Read";
        public string ColWrite => "Write";
        public string ColDelete => "Delete";
        public string ColAction => IsId ? "Aksi" : "Action";
        public string AddUserBtn => IsId ? "➕ Tambah Pengguna" : "➕ Add User";
        public string DeleteSelectedBtn => IsId ? "🗑 Hapus File" : "🗑 Delete File";
         public string ConfirmDeleteTitle => IsId ? "Konfirmasi Hapus File" : "Confirm File Delete";
        public string ConfirmDeleteMsg => IsId ? "Apakah Anda yakin ingin menghapus '{0}' secara permanen dari server?" : "Are you sure you want to permanently delete '{0}' from server?";
        public string ConfirmDeleteMultipleMsg => IsId ? "Apakah Anda yakin ingin menghapus {0} file/folder terpilih secara permanen dari server?" : "Are you sure you want to permanently delete {0} selected items from server?";
        public string FileDeletedSuccess => IsId ? "File '{0}' berhasil dihapus dari server." : "File '{0}' deleted successfully from server.";
        public string ItemsDeletedSuccess => IsId ? "{0} item berhasil dihapus dari server." : "{0} items deleted successfully from server.";
        public string CannotDeleteAdminMsg => IsId ? "User 'admin' utama tidak dapat dihapus." : "Primary 'admin' user cannot be deleted.";
        public string SaveSettingsBtn => IsId ? "💾 Simpan Pengaturan" : "💾 Save Settings";
        public string ResetSettingsBtn => IsId ? "Kembalikan Default" : "Restore Defaults";
        public string ContextDownload => IsId ? "⬇ Unduh (Download)" : "⬇ Download";
        public string ContextDelete => IsId ? "🗑 Hapus (Delete)" : "🗑 Delete";
        public string ContextRefresh => IsId ? "🔄 Muat Ulang (Refresh)" : "🔄 Refresh";
        public string SelectDestFolderTitle => IsId ? "Pilih Folder Tujuan Penyimpanan Unduhan" : "Select Destination Folder to Save Downloads";

        // Perilaku Aplikasi & Lifecycle
        public string AppBehaviorTitle => IsId ? "⚡ Perilaku Aplikasi & System Tray" : "⚡ Application Behavior & System Tray";
        public string AutoMinimizeNote => IsId ? "Aplikasi dibatasi 1x jalan (Single Instance)." : "Single-instance enforced.";
        public string MinimizeToTrayChk => IsId ? "Minimalkan ke system tray saat tombol tutup [X] ditekan" : "Minimized to system tray when click close";
        public string ExitAppBtn => IsId ? "❌ Keluar dari Aplikasi" : "❌ Exit Application";
        public string TrayOpenApp => IsId ? "Buka FilanShare" : "Open FilanShare";
        public string TrayExitApp => IsId ? "Keluar dari Aplikasi" : "Exit Application";

        // About
        public string AboutTitle => "FilanShare";
        public string AboutVersion => IsId ? $"Versi {FileSender.Helpers.AppVersion.Value} (Avalonia UI Desktop Multiplatform)" : $"Version {FileSender.Helpers.AppVersion.Value} (Avalonia UI Desktop Multiplatform)";
        public string AboutDesc => IsId ? "Aplikasi transfer file P2P lokal stealth untuk Windows dan Linux tanpa publish/broadcast beacon." : "Stealth local P2P file transfer app for Windows and Linux without discovery beacon.";
        public string FeaturesTitle => IsId ? "✨ Fitur Unggulan" : "✨ Key Features";
        public string SystemInfoTitle => IsId ? "🖥️ Informasi Sistem & Lingkungan" : "🖥️ System & Environment Info";
        public string OsLabel => IsId ? "Sistem Operasi:" : "Operating System:";
        public string FrameworkLabel => IsId ? "Framework:" : "Framework:";
        public string ArchLabel => IsId ? "Arsitektur CPU:" : "CPU Architecture:";
        public string AppFolderLabel => IsId ? "Folder Aplikasi:" : "App Directory:";
        public string OpenAppFolderBtn => IsId ? "📂 Buka Folder Aplikasi" : "📂 Open App Folder";
        public string CopySysInfoBtn => IsId ? "📋 Salin Info Sistem" : "📋 Copy System Info";

        // Status bar & messages
        public string ReadyStatus => IsId ? "Siap. Gunakan tab Menu File untuk membagikan atau menjelajahi file." : "Ready. Use File tab to share or browse files.";
        public string ServerStoppedMessage => IsId ? "Server telah dihentikan." : "Server has been stopped.";
        public string ServerRunningMessage => IsId ? "Server aktif pada port {0}. Siap dihubungi oleh klien." : "Server active on port {0}. Ready for client connections.";
        public string ClientConnectingMessage => IsId ? "Menghubungkan ke {0}:{1}..." : "Connecting to {0}:{1}...";
        public string ClientConnectedMessage => IsId ? "Terhubung ke {0}:{1}." : "Connected to {0}:{1}.";
        public string ClientDisconnectedMessage => IsId ? "Koneksi klien terputus." : "Client disconnected.";
        public string DownloadCompleteMessage => IsId ? "Unduhan '{0}' berhasil diselesaikan." : "Download '{0}' completed successfully.";
        public string UploadCompleteMessage => IsId ? "File '{0}' berhasil diunggah." : "File '{0}' uploaded successfully.";

        // Firewall Status & Actions
        public string FirewallStatusOpen => IsId ? "Firewall: Terbuka (Port {0})" : "Firewall: Open (Port {0})";
        public string FirewallStatusClosed => IsId ? "Firewall: Tertutup (Port {0})" : "Firewall: Blocked (Port {0})";
        public string FirewallChecking => IsId ? "Cek Firewall..." : "Checking Firewall...";
        public string FirewallBtnOpen => IsId ? "Buka" : "Open";
        public string FirewallAutoOpened => IsId ? "[Firewall] Port {0} berhasil dibuka otomatis di firewall." : "[Firewall] Port {0} automatically opened in firewall.";
        public string FirewallAlreadyOpen => IsId ? "[Firewall] Port {0} sudah diizinkan di firewall." : "[Firewall] Port {0} is already allowed in firewall.";
        public string FirewallOpenFailed => IsId ? "[Firewall] Gagal membuka port {0}: {1}" : "[Firewall] Failed to open port {0}: {1}";

        // Drag and Drop Overlay
        public string DropOverlayTitle => IsId ? "Lepaskan File atau Folder di Sini" : "Drop Files or Folders Here";
        public string DropOverlayDesc => IsId ? "Mendukung banyak file sekaligus & folder lengkap untuk diunggah ke host" : "Supports multiple files and entire folders to upload to host";

        // Connected Device Badges & Actions
        public string ClientStatusActive => IsId ? "Aktif" : "Active";
        public string ClientActionConnected => IsId ? "Terhubung" : "Connected";

        // About Features List
        public string AboutFeature1 => IsId ? "100% Stealth Mode: Tidak menyiarkan UDP broadcast/mDNS sehingga nama device tidak terdeteksi otomatis oleh orang lain di LAN." : "100% Stealth Mode: No UDP broadcast/mDNS beacons emitted, keeping your device hidden from LAN discovery scanners.";
        public string AboutFeature2 => IsId ? "Cross-Platform: Berjalan identik di Windows 10/11 dan Linux (Ubuntu, Debian, Fedora, Arch)." : "Cross-Platform: Runs identically on Windows 10/11 and Linux (Ubuntu, Debian, Fedora, Arch).";
        public string AboutFeature3 => IsId ? "High-Speed Streaming: Transfer langsung antar perangkat dengan buffer streaming cepat tanpa batasan ukuran file." : "High-Speed Streaming: Direct peer-to-peer transfer with fast buffer streaming and no file size limit.";
        public string AboutFeature4 => IsId ? "Direct P2P Transfer: Pengiriman file langsung peer-to-peer terenkripsi lokal tanpa server pihak ketiga." : "Direct P2P Transfer: Direct peer-to-peer local transfer without third-party servers.";

        // Transfer Monitor Statuses
        public string TransferStatusDownloading => IsId ? "Mengunduh: " : "Downloading: ";
        public string TransferStatusDownloadComplete => IsId ? "Unduhan Selesai: " : "Download Complete: ";
        public string TransferStatusDownloadCancelled => IsId ? "Unduhan Dibatalkan: " : "Download Cancelled: ";
        public string TransferStatusDownloadFailed => IsId ? "Gagal Mengunduh: " : "Download Failed: ";
        public string TransferStatusUploading => IsId ? "Mengunggah: " : "Uploading: ";
        public string TransferStatusUploadComplete => IsId ? "Unggahan Selesai: " : "Upload Complete: ";
        public string TransferStatusUploadCancelled => IsId ? "Unggahan Dibatalkan: " : "Upload Cancelled: ";
        public string TransferStatusUploadFailed => IsId ? "Gagal Mengunggah: " : "Upload Failed: ";

        // Dialogs & Notifications
        public string CopiedTitle => IsId ? "Tersalin" : "Copied";
        public string CredentialsCopiedMsg => IsId ? "Kredensial berhasil disalin ke clipboard! Kirimkan ke pengguna PC/Linux klien." : "Credentials copied to clipboard! Share them with the client PC/Linux user.";
        public string SysInfoCopiedMsg => IsId ? "Informasi sistem berhasil disalin ke clipboard!" : "System info copied to clipboard!";
        public string LogsCopiedMsg => IsId ? "Seluruh catatan log berhasil disalin ke clipboard!" : "All event logs copied to clipboard!";
        public string RandomPassGeneratedMsg => IsId ? "Password acak baru dibuat." : "New random password generated.";
        public string LogsClearedMsg => IsId ? "Log dibersihkan." : "Logs cleared.";
        public string HistoryClearedMsg => IsId ? "Riwayat komputer terhubung telah dibersihkan." : "Connected devices history cleared.";
        public string SettingsRestoredMsg => IsId ? "Pengaturan dikembalikan ke nilai default." : "Settings restored to default values.";
        public string DownloadFolderChangedMsg => IsId ? "Folder unduhan diubah ke: {0}" : "Download folder changed to: {0}";
        public string SharedFolderChangedMsg => IsId ? "Folder share diubah ke: {0}" : "Shared folder changed to: {0}";
        public string MsgBoxNotConnectedTitle => IsId ? "Belum Terhubung" : "Not Connected";
        public string MsgBoxNotConnectedDesc => IsId ? "Silakan hubungkan ke Host (PC B) terlebih dahulu sebelum mengunggah file." : "Please connect to Host (PC B) first before transferring files.";
        public string MsgBoxSelectFileTitle => IsId ? "Pilih File" : "Select File";
        public string MsgBoxSelectFileDesc => IsId ? "Silakan pilih salah satu file dari daftar terlebih dahulu." : "Please select a file from the list first.";
        public string MsgBoxFolderInfoTitle => IsId ? "Info" : "Info";
        public string MsgBoxFolderInfoDesc => IsId ? "Item ini adalah folder. Klik ganda pada folder untuk melihat isinya." : "This item is a folder. Double-click to browse its contents.";
        public string MsgBoxInvalidPortTitle => IsId ? "Port Tidak Valid" : "Invalid Port";
        public string MsgBoxInvalidPortDesc => IsId ? "Port harus berupa angka antara 1 dan 65535." : "Port must be a number between 1 and 65535.";
        public string MsgBoxConnectionFailedTitle => IsId ? "Koneksi Gagal" : "Connection Failed";
        public string MsgBoxEmptyFilesTitle => IsId ? "File Kosong" : "No Files";
        public string MsgBoxEmptyFilesDesc => IsId ? "Tidak ada file valid yang ditemukan untuk diunggah." : "No valid files found to upload.";
        public string AppReadyMsg => IsId ? "FilanShare Desktop siap digunakan." : "FilanShare Desktop is ready.";
    }
}
