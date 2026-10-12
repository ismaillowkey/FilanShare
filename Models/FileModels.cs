using System;
using System.Collections.Generic;

namespace FileSender.Models
{
    public class FileItem
    {
        public string Name { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public string FormattedSize { get; set; } = "";
        public DateTime LastModified { get; set; }
        public string Extension { get; set; } = "";

        public string Icon => IsDirectory ? "📁" : "📄";
        public string ItemType => IsDirectory ? "Folder" : (string.IsNullOrEmpty(Extension) ? "File" : Extension.ToUpperInvariant() + " File");
    }

    public class BrowseResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string CurrentPath { get; set; } = "";
        public string ParentPath { get; set; } = "";
        public List<FileItem> Items { get; set; } = new List<FileItem>();
        public bool AllowUpload { get; set; }
        public bool AllowDownload { get; set; }
        public bool AllowDelete { get; set; }
    }

    public class PingResponse
    {
        public bool Success { get; set; }
        public string ServerName { get; set; } = "";
        public bool AllowUpload { get; set; }
        public bool AllowDownload { get; set; }
        public bool AllowDelete { get; set; }
        public string SharedFolderName { get; set; } = "";
    }

    public class UserAccount : System.ComponentModel.INotifyPropertyChanged
    {
        private string _username = "";
        private string _password = "";
        private bool _canRead = true;
        private bool _canWrite = false;
        private bool _canDelete = false;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public string Username
        {
            get => _username;
            set
            {
                if (_username != value)
                {
                    _username = value;
                    OnPropertyChanged(nameof(Username));
                    OnPropertyChanged(nameof(IsAdmin));
                    OnPropertyChanged(nameof(CanBeDeleted));
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged(nameof(Password));
                }
            }
        }

        public bool CanRead
        {
            get => _canRead;
            set
            {
                if (_canRead != value)
                {
                    _canRead = value;
                    OnPropertyChanged(nameof(CanRead));
                }
            }
        }

        public bool CanWrite
        {
            get => _canWrite;
            set
            {
                if (_canWrite != value)
                {
                    _canWrite = value;
                    OnPropertyChanged(nameof(CanWrite));
                }
            }
        }

        public bool CanDelete
        {
            get => _canDelete;
            set
            {
                if (_canDelete != value)
                {
                    _canDelete = value;
                    OnPropertyChanged(nameof(CanDelete));
                }
            }
        }

        public bool IsAdmin => string.Equals(_username, "admin", StringComparison.OrdinalIgnoreCase);
        public bool CanBeDeleted => !IsAdmin;

        public UserAccount Clone()
        {
            return new UserAccount
            {
                Username = this.Username,
                Password = this.Password,
                CanRead = this.CanRead,
                CanWrite = this.CanWrite,
                CanDelete = this.CanDelete
            };
        }

        protected void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
        }
    }

    public class TransferProgressInfo
    {
        public string FileName { get; set; } = "";
        public long BytesTransferred { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage => TotalBytes > 0 ? Math.Min(100.0, (double)BytesTransferred / TotalBytes * 100.0) : 0;
        public double SpeedBytesPerSec { get; set; }
        public string FormattedSpeed { get; set; } = "";
        public string FormattedProgress { get; set; } = "";
        public string Status { get; set; } = "";
        public bool IsCompleted { get; set; }
        public bool IsFailed { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
        public string FormattedTimeFull => Timestamp.ToString("HH:mm:ss.fff");
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";

        // Event log table display helpers
        public string LevelLabel => Level switch
        {
            LogLevel.Success => " OK ",
            LogLevel.Warning => "WARN",
            LogLevel.Error   => " ERR",
            _                => "INFO"
        };

        public string LevelBackground => Level switch
        {
            LogLevel.Success => "#064E3B",
            LogLevel.Warning => "#78350F",
            LogLevel.Error   => "#7F1D1D",
            _                => "#1E3A5F"
        };

        public string LevelForeground => Level switch
        {
            LogLevel.Success => "#6EE7B7",
            LogLevel.Warning => "#FDE68A",
            LogLevel.Error   => "#FCA5A5",
            _                => "#93C5FD"
        };

        public string MessageColor => Level switch
        {
            LogLevel.Success => "#34D399",
            LogLevel.Warning => "#FCD34D",
            LogLevel.Error   => "#F87171",
            _                => "#CBD5E1"
        };
    }

    public class ConnectedClientItem : System.ComponentModel.INotifyPropertyChanged
    {
        private string _ipAddress = "";
        private string _deviceName = "";
        private DateTime _lastConnected = DateTime.Now;
        private string _lastAction = FileSender.Services.LanguageManager.Instance.ClientActionConnected;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public void RefreshLanguage()
        {
            OnPropertyChanged(nameof(StatusBadge));
        }

        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (_ipAddress != value)
                {
                    _ipAddress = value;
                    OnPropertyChanged(nameof(IpAddress));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DeviceName
        {
            get => string.IsNullOrEmpty(_deviceName) ? _ipAddress : _deviceName;
            set
            {
                if (_deviceName != value)
                {
                    _deviceName = value;
                    OnPropertyChanged(nameof(DeviceName));
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string DisplayName => string.IsNullOrEmpty(_deviceName) || _deviceName == _ipAddress 
            ? _ipAddress 
            : $"{_deviceName} ({_ipAddress})";

        public DateTime LastConnected
        {
            get => _lastConnected;
            set
            {
                _lastConnected = value;
                OnPropertyChanged(nameof(LastConnected));
                OnPropertyChanged(nameof(FormattedTime));
            }
        }

        public string FormattedTime => _lastConnected.ToString("HH:mm:ss");

        public string LastAction
        {
            get => _lastAction;
            set
            {
                if (_lastAction != value)
                {
                    _lastAction = value;
                    OnPropertyChanged(nameof(LastAction));
                }
            }
        }

        public string Icon => "💻";

        public string StatusBadge => FileSender.Services.LanguageManager.Instance.ClientStatusActive;

        protected void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propName));
        }
    }

    public class RecentHostItem
    {
        public string HostIp { get; set; } = "";
        public int Port { get; set; } = 5050;
        public string Username { get; set; } = "admin";
        public DateTime LastConnected { get; set; } = DateTime.Now;
        public string FormattedTime => LastConnected.ToString("yyyy-MM-dd HH:mm");
        public string DisplayText => $"{HostIp}:{Port} ({Username})";
    }

    public class FilanShareSettingsDto
    {
        public int Port { get; set; } = 5050;
        public string SharedPath { get; set; } = "";
        public string DownloadPath { get; set; } = "";
        public bool MinimizeToTrayOnClose { get; set; } = true;
        public List<UserAccount> Users { get; set; } = new List<UserAccount>();
    }
}
