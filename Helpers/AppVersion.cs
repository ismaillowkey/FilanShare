using System;
using System.IO;
using System.Reflection;

namespace FileSender.Helpers
{
    public static class AppVersion
    {
        private static string? _version;

        public static string Value
        {
            get
            {
                if (_version == null)
                {
                    _version = LoadVersion();
                }
                return _version;
            }
        }

        public static string NumberOnly
        {
            get
            {
                string v = Value;
                return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v.Substring(1) : v;
            }
        }

        private static string LoadVersion()
        {
            try
            {
                string confPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "version.conf");
                if (File.Exists(confPath))
                {
                    string raw = File.ReadAllText(confPath).Trim();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        return raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? raw : "v" + raw;
                    }
                }

                var infoVer = typeof(AppVersion).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(infoVer))
                {
                    string v = infoVer.Split('+')[0].Trim();
                    return v.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? v : "v" + v;
                }

                var asmVer = typeof(AppVersion).Assembly.GetName().Version;
                if (asmVer != null)
                {
                    return $"v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
                }
            }
            catch { }

            return "v0.1.0";
        }
    }
}
