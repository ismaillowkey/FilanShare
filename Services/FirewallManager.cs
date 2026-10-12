using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FileSender.Services
{
    public static class FirewallManager
    {
        public static async Task<bool> IsFirewallOpenAsync(int port)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await CheckWindowsFirewallAsync(port);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await CheckLinuxFirewallAsync(port);
            }
            return true;
        }

        private static async Task<bool> CheckWindowsFirewallAsync(int port)
        {
            try
            {
                // Check if specific port rule exists (FilanShare or legacy FileSender)
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"FilanShare LAN Port {port}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0)
                {
                    // Check if disabled
                    if (output.Contains("Enabled:                              No", StringComparison.OrdinalIgnoreCase) ||
                        output.Contains("Diaktifkan:                           Tidak", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(output, @"Enabled\s*:\s*No", RegexOptions.IgnoreCase) ||
                        Regex.IsMatch(output, @"Diaktifkan\s*:\s*Tidak", RegexOptions.IgnoreCase))
                    {
                        return false;
                    }
                    return true;
                }

                // Check legacy rule name
                var psiLegacy = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall show rule name=\"FileSender LAN Port {port}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var procLeg = Process.Start(psiLegacy);
                if (procLeg != null)
                {
                    string outLeg = await procLeg.StandardOutput.ReadToEndAsync();
                    await procLeg.WaitForExitAsync();
                    if (procLeg.ExitCode == 0 &&
                        !Regex.IsMatch(outLeg, @"(Enabled|Diaktifkan)\s*:\s*(No|Tidak)", RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }

                // Also check if app-level rule exists
                var psiApp = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=\"FilanShare LAN App\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var procApp = Process.Start(psiApp);
                if (procApp != null)
                {
                    string outApp = await procApp.StandardOutput.ReadToEndAsync();
                    await procApp.WaitForExitAsync();
                    if (procApp.ExitCode == 0 &&
                        !Regex.IsMatch(outApp, @"(Enabled|Diaktifkan)\s*:\s*(No|Tidak)", RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> CheckLinuxFirewallAsync(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ufw",
                    Arguments = "status",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return true; // ufw not available, assume open

                string output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (output.Contains("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (output.Contains($"{port}/tcp", StringComparison.OrdinalIgnoreCase) &&
                    output.Contains("ALLOW", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                // ufw not installed or permission restricted, assume open
                return true;
            }
        }

        public static bool IsAdministrator()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public static async Task<(bool success, string message)> OpenFirewallAsync(int port)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return await OpenWindowsFirewallAsync(port);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return await OpenLinuxFirewallAsync(port);
            }
            return (true, "Platform does not require firewall modification.");
        }

        private static async Task<(bool success, string message)> OpenWindowsFirewallAsync(int port)
        {
            try
            {
                string ruleName = $"FilanShare LAN Port {port}";
                bool isAdmin = IsAdministrator();

                if (isAdmin)
                {
                    // Run netsh directly without elevation prompt
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode == 0)
                        {
                            return (true, $"Aturan firewall '{ruleName}' berhasil ditambahkan.");
                        }
                    }
                    return (false, "Gagal menambahkan aturan firewall.");
                }
                else
                {
                    // Request elevation via runas directly to netsh
                    var psi = new ProcessStartInfo
                    {
                        FileName = "netsh",
                        Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port} profile=any",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        await proc.WaitForExitAsync();
                        
                        // Verify rule is actually open now
                        bool isOpen = await CheckWindowsFirewallAsync(port);
                        if (isOpen)
                        {
                            return (true, LanguageManager.Instance.IsId 
                                ? $"Aturan firewall '{ruleName}' berhasil dibuka dengan hak Administrator."
                                : $"Firewall rule '{ruleName}' successfully opened with Administrator rights.");
                        }
                    }
                    return (false, LanguageManager.Instance.IsId 
                        ? "Firewall masih belum aktif setelah permintaan."
                        : "Firewall rule not active after elevation request.");
                }
            }
            catch (System.ComponentModel.Win32Exception wEx) when (wEx.NativeErrorCode == 1223)
            {
                // User clicked 'No' on UAC prompt
                return (false, LanguageManager.Instance.IsId 
                    ? "Izin Administrator (UAC) dibatalkan oleh pengguna."
                    : "Administrator permission (UAC) was cancelled by user.");
            }
            catch (Exception ex)
            {
                return (false, $"Error membuka firewall: {ex.Message}");
            }
        }

        private static async Task<(bool success, string message)> OpenLinuxFirewallAsync(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "pkexec",
                    Arguments = $"ufw allow {port}/tcp",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0)
                    {
                        return (true, $"Port {port}/tcp berhasil dibuka di UFW Linux.");
                    }
                }
                return (false, "Gagal membuka port via UFW.");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
