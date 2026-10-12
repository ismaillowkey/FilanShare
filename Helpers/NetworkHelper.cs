using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FileSender.Helpers
{
    public class NetworkInterfaceItem
    {
        public string Name { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public bool IsVirtual { get; set; } = false;
        public string DisplayText
        {
            get
            {
                string icon = Name.ToLowerInvariant().Contains("wi-fi") || Name.ToLowerInvariant().Contains("wireless") || Name.ToLowerInvariant().Contains("wlan")
                    ? "📶 "
                    : (IsVirtual ? "🔧 " : "🌐 ");
                return $"{icon}{Name} ({IpAddress})";
            }
        }

        public override string ToString() => DisplayText;
    }

    public static class NetworkHelper
    {
        public static List<NetworkInterfaceItem> GetLocalIPv4Addresses()
        {
            var list = new List<NetworkInterfaceItem>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                foreach (var ni in interfaces)
                {
                    string lowerName = ni.Name.ToLowerInvariant();
                    string lowerDesc = ni.Description.ToLowerInvariant();

                    bool isVirtual = lowerName.Contains("loopback") || lowerName.Contains("virtual") ||
                                     lowerName.Contains("vethernet") || lowerName.Contains("wsl") ||
                                     lowerName.Contains("hyper-v") || lowerName.Contains("plcsim") ||
                                     lowerName.Contains("vmware") || lowerName.Contains("virtualbox") ||
                                     lowerDesc.Contains("virtual") || lowerDesc.Contains("hyper-v") ||
                                     lowerDesc.Contains("loopback");

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ip = addr.Address.ToString();
                            // Filter out APIPA / auto-assigned 169.254.x.x
                            if (!ip.StartsWith("169.254."))
                            {
                                list.Add(new NetworkInterfaceItem
                                {
                                    Name = ni.Name,
                                    IpAddress = ip,
                                    IsVirtual = isVirtual
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }

            // Sort: Wi-Fi / physical first, virtual adapters last
            list = list
                .OrderBy(item => item.IsVirtual ? 1 : 0)
                .ThenByDescending(item => item.Name.ToLowerInvariant().Contains("wi-fi") || item.Name.ToLowerInvariant().Contains("wireless") || item.Name.ToLowerInvariant().Contains("wlan"))
                .ThenBy(item => item.Name)
                .ToList();

            if (list.Count == 0)
            {
                list.Add(new NetworkInterfaceItem
                {
                    Name = "Localhost",
                    IpAddress = "127.0.0.1",
                    IsVirtual = false
                });
            }

            return list;
        }

        public static string GetPrimaryLocalIPv4Address()
        {
            var list = GetLocalIPv4Addresses();
            return list.FirstOrDefault()?.IpAddress ?? "127.0.0.1";
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "0 B";
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public static string GenerateRandomPassword(int length = 8)
        {
            const string validChars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var sb = new StringBuilder();
            while (length-- > 0)
            {
                int idx = RandomNumberGenerator.GetInt32(validChars.Length);
                sb.Append(validChars[idx]);
            }
            return sb.ToString();
        }

        public static bool IsSubPathOf(string rootPath, string targetPath)
        {
            try
            {
                string fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string fullTarget = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool CheckFolderPermissions(string folderPath, bool checkWrite, out string? errorMessage)
        {
            errorMessage = null;
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // Test read permission by attempting enumeration
                Directory.GetFileSystemEntries(folderPath);

                // Test write permission if requested
                if (checkWrite)
                {
                    string testFile = Path.Combine(folderPath, $".fs_perm_test_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "perm_test");
                    if (File.Exists(testFile))
                    {
                        File.Delete(testFile);
                    }
                }

                return true;
            }
            catch (UnauthorizedAccessException uex)
            {
                errorMessage = $"Akses ditolak oleh perizinan sistem (Access is Denied).\nDetail: {uex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Terjadi kesalahan saat memeriksa izin folder:\n{ex.Message}";
                return false;
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("open", url) { UseShellExecute = false });
                }
            }
            catch { }
        }

        public static void OpenFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", folderPath) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start(new ProcessStartInfo("xdg-open", folderPath) { UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("open", folderPath) { UseShellExecute = false });
                }
            }
            catch { }
        }
    }
}
