using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FileSender.Helpers;
using FileSender.Models;
using Newtonsoft.Json;

namespace FileSender.Services
{
    public class FileHttpServer
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public string SharedDirectory { get; set; } = "";
        public int Port { get; set; } = 5050;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "123456";
        public bool AllowDownload { get; set; } = true;
        public bool AllowUpload { get; set; } = true;
        public List<UserAccount> Users { get; set; } = new List<UserAccount>();

        public bool IsRunning => _isRunning;

        public event Action<LogEntry>? OnLog;
        public event Action<string, string, long>? OnTransferStarted; // clientIp, fileName, size
        public event Action<string, string, long>? OnTransferCompleted; // clientIp, fileName, bytes
        public event Action<string, string>? OnClientActivity; // clientIp, action description

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            OnLog?.Invoke(new LogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            });
        }

        public void Start()
        {
            if (_isRunning) return;

            if (string.IsNullOrWhiteSpace(SharedDirectory) || !Directory.Exists(SharedDirectory))
            {
                throw new DirectoryNotFoundException("Folder yang dibagikan tidak valid atau tidak ditemukan.");
            }

            _cts = new CancellationTokenSource();
            try
            {
                _listener = new TcpListener(IPAddress.IPv6Any, Port);
                _listener.Server.DualMode = true;
                _listener.Start();
            }
            catch
            {
                _listener = new TcpListener(IPAddress.Any, Port);
                _listener.Start();
            }
            _isRunning = true;

            string startLogMsg = LanguageManager.Instance.IsId
                ? $"Server aktif pada port {Port} di SEMUA interface (0.0.0.0). Folder: {SharedDirectory}"
                : $"Server active on port {Port} on ALL network interfaces (0.0.0.0). Folder: {SharedDirectory}";
            Log(startLogMsg, LogLevel.Success);

            Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { }

            Log(LanguageManager.Instance.IsId ? "Server telah dihentikan." : "Server has been stopped.", LogLevel.Warning);
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, token));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Log($"Kesalahan listener: {ex.Message}", LogLevel.Error);
                    }
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            string clientIp = "Unknown";
            try
            {
                var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                if (endpoint != null)
                {
                    var addr = endpoint.Address;
                    if (addr.IsIPv4MappedToIPv6)
                    {
                        addr = addr.MapToIPv4();
                    }
                    clientIp = addr.ToString();
                }
            }
            catch { }

            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new BufferedStream(stream))
                {
                    // Read request header line by line
                    var headerLines = new List<string>();
                    var lineBuffer = new StringBuilder();

                    int b;
                    while ((b = reader.ReadByte()) != -1)
                    {
                        if (b == '\n')
                        {
                            string line = lineBuffer.ToString().TrimEnd('\r');
                            if (string.IsNullOrEmpty(line))
                            {
                                // End of headers
                                break;
                            }
                            headerLines.Add(line);
                            lineBuffer.Clear();
                        }
                        else
                        {
                            lineBuffer.Append((char)b);
                        }
                    }

                    if (headerLines.Count == 0) return;

                    string requestLine = headerLines[0];
                    string[] reqParts = requestLine.Split(' ');
                    if (reqParts.Length < 2) return;

                    string method = reqParts[0].ToUpperInvariant();
                    string rawUri = reqParts[1];

                    // Parse Headers into Dictionary
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < headerLines.Count; i++)
                    {
                        int colonIdx = headerLines[i].IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string key = headerLines[i].Substring(0, colonIdx).Trim();
                            string val = headerLines[i].Substring(colonIdx + 1).Trim();
                            headers[key] = val;
                        }
                    }

                    // Check Authentication
                    var authUser = GetAuthenticatedUser(headers);
                    if (authUser == null && HasUsersConfigured())
                    {
                        await SendUnauthorizedResponseAsync(stream).ConfigureAwait(false);
                        Log(LanguageManager.Instance.IsId 
                            ? $"Akses ditolak dari {clientIp}: Autentikasi salah/kosong." 
                            : $"Access denied from {clientIp}: Authentication invalid or missing.", LogLevel.Warning);
                        return;
                    }

                    // Parse URL and Query Parameters
                    string pathOnly = rawUri;
                    string queryOnly = "";
                    int qIdx = rawUri.IndexOf('?');
                    if (qIdx >= 0)
                    {
                        pathOnly = rawUri.Substring(0, qIdx);
                        queryOnly = rawUri.Substring(qIdx + 1);
                    }

                    var queryParams = ParseQueryString(queryOnly);

                    // Route Handlers
                    bool isId = LanguageManager.Instance.IsId;
                    if (pathOnly.Equals("/api/ping", StringComparison.OrdinalIgnoreCase))
                    {
                        string desc = isId ? $"Ping / Cek Status ({authUser?.Username ?? "Guest"})" : $"Ping / Status Check ({authUser?.Username ?? "Guest"})";
                        OnClientActivity?.Invoke(clientIp, desc);
                        await HandlePingAsync(stream, authUser).ConfigureAwait(false);
                    }
                    else if (pathOnly.Equals("/api/browse", StringComparison.OrdinalIgnoreCase))
                    {
                        string reqPath = queryParams.ContainsKey("path") ? queryParams["path"] : "";
                        string actionDesc;
                        if (isId)
                        {
                            actionDesc = string.IsNullOrEmpty(reqPath) ? "Buka folder utama" : $"Jelajah: /{reqPath}";
                        }
                        else
                        {
                            actionDesc = string.IsNullOrEmpty(reqPath) ? "Open root folder" : $"Browse: /{reqPath}";
                        }
                        OnClientActivity?.Invoke(clientIp, $"{actionDesc} ({authUser?.Username ?? "Guest"})");
                        await HandleBrowseAsync(stream, queryParams, authUser).ConfigureAwait(false);
                    }
                    else if (pathOnly.Equals("/api/download", StringComparison.OrdinalIgnoreCase))
                    {
                        string reqPath = queryParams.ContainsKey("path") ? Path.GetFileName(queryParams["path"]) : "File";
                        string actionDesc = isId ? $"Unduh: {reqPath}" : $"Download: {reqPath}";
                        OnClientActivity?.Invoke(clientIp, $"{actionDesc} ({authUser?.Username ?? "Guest"})");
                        await HandleDownloadAsync(stream, queryParams, clientIp, authUser).ConfigureAwait(false);
                    }
                    else if (pathOnly.Equals("/api/upload", StringComparison.OrdinalIgnoreCase) && method == "POST")
                    {
                        string fileName = queryParams.ContainsKey("fileName") ? queryParams["fileName"] : "File";
                        string actionDesc = isId ? $"Unggah: {fileName}" : $"Upload: {fileName}";
                        OnClientActivity?.Invoke(clientIp, $"{actionDesc} ({authUser?.Username ?? "Guest"})");
                        await HandleUploadAsync(stream, reader, headers, queryParams, clientIp, authUser).ConfigureAwait(false);
                    }
                    else if (pathOnly.Equals("/api/delete", StringComparison.OrdinalIgnoreCase) && (method == "DELETE" || method == "POST"))
                    {
                        string fileName = queryParams.ContainsKey("path") ? Path.GetFileName(queryParams["path"]) : "File";
                        string actionDesc = isId ? $"Hapus: {fileName}" : $"Delete: {fileName}";
                        OnClientActivity?.Invoke(clientIp, $"{actionDesc} ({authUser?.Username ?? "Guest"})");
                        await HandleDeleteAsync(stream, queryParams, clientIp, authUser).ConfigureAwait(false);
                    }
                    else if (pathOnly == "/" || pathOnly.StartsWith("/web", StringComparison.OrdinalIgnoreCase))
                    {
                        string actionDesc = isId ? "Akses Web Portal" : "Web Portal Access";
                        OnClientActivity?.Invoke(clientIp, $"{actionDesc} ({authUser?.Username ?? "Guest"})");
                        await HandleWebUiAsync(stream, queryParams, authUser).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendResponseAsync(stream, 404, "Not Found", "text/plain", isId ? "Endpoint tidak ditemukan." : "Endpoint not found.");
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently ignore aborted client connections
                if (_isRunning && !(ex is IOException) && !(ex is SocketException))
                {
                    Log($"Kesalahan saat menangani klien {clientIp}: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private bool HasUsersConfigured()
        {
            if (Users != null && Users.Count > 0) return true;
            return !string.IsNullOrEmpty(Username) || !string.IsNullOrEmpty(Password);
        }

        private UserAccount? GetAuthenticatedUser(Dictionary<string, string> headers)
        {
            if (!HasUsersConfigured())
            {
                return new UserAccount
                {
                    Username = "Guest",
                    Password = "",
                    CanRead = AllowDownload,
                    CanWrite = AllowUpload,
                    CanDelete = true
                };
            }

            if (headers.TryGetValue("Authorization", out string? authHeader) && !string.IsNullOrWhiteSpace(authHeader))
            {
                if (authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    string encoded = authHeader.Substring(6).Trim();
                    try
                    {
                        byte[] credBytes = Convert.FromBase64String(encoded);
                        string cred = Encoding.UTF8.GetString(credBytes);
                        int separator = cred.IndexOf(':');
                        if (separator >= 0)
                        {
                            string user = cred.Substring(0, separator);
                            string pass = cred.Substring(separator + 1);

                            if (Users != null && Users.Count > 0)
                            {
                                var matched = Users.Find(u =>
                                    string.Equals(u.Username, user, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(u.Password, pass, StringComparison.Ordinal));
                                if (matched != null) return matched;
                            }

                            if (!string.IsNullOrEmpty(Username) &&
                                string.Equals(user, Username, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(pass, Password, StringComparison.Ordinal))
                            {
                                return new UserAccount
                                {
                                    Username = Username,
                                    Password = Password,
                                    CanRead = AllowDownload,
                                    CanWrite = AllowUpload,
                                    CanDelete = true
                                };
                            }
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private async Task SendUnauthorizedResponseAsync(Stream stream)
        {
            string body = "401 Unauthorized - FilanShare Memerlukan Username dan Password.";
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

            string header = "HTTP/1.1 401 Unauthorized\r\n" +
                            "WWW-Authenticate: Basic realm=\"FilanShare\"\r\n" +
                            "Content-Type: text/plain; charset=utf-8\r\n" +
                            $"Content-Length: {bodyBytes.Length}\r\n" +
                            "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private async Task SendResponseAsync(Stream stream, int statusCode, string statusText, string contentType, string content)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
            string header = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                            $"Content-Type: {contentType}; charset=utf-8\r\n" +
                            $"Content-Length: {bodyBytes.Length}\r\n" +
                            "Access-Control-Allow-Origin: *\r\n" +
                            "Connection: close\r\n\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private async Task HandlePingAsync(Stream stream, UserAccount? user)
        {
            var ping = new PingResponse
            {
                Success = true,
                ServerName = "FilanShare",
                AllowUpload = user != null ? user.CanWrite : AllowUpload,
                AllowDownload = user != null ? user.CanRead : AllowDownload,
                AllowDelete = user != null ? user.CanDelete : true,
                SharedFolderName = Path.GetFileName(SharedDirectory)
            };

            string json = JsonConvert.SerializeObject(ping);
            await SendResponseAsync(stream, 200, "OK", "application/json", json).ConfigureAwait(false);
        }

        private async Task HandleBrowseAsync(Stream stream, Dictionary<string, string> query, UserAccount? user)
        {
            if (user != null && !user.CanRead)
            {
                await SendResponseAsync(stream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new BrowseResponse
                {
                    Success = false,
                    Message = $"Akses ditolak: Akun '{user.Username}' tidak memiliki izin membaca (Read)."
                })).ConfigureAwait(false);
                return;
            }

            string reqPath = query.ContainsKey("path") ? query["path"] : "";
            reqPath = (reqPath ?? "").Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            string targetDir = string.IsNullOrEmpty(reqPath) 
                ? SharedDirectory 
                : Path.Combine(SharedDirectory, reqPath);

            // Path traversal defense
            if (!NetworkHelper.IsSubPathOf(SharedDirectory, targetDir) && !string.Equals(Path.GetFullPath(SharedDirectory), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
            {
                await SendResponseAsync(stream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new BrowseResponse
                {
                    Success = false,
                    Message = "Akses direktori di luar root dilarang."
                })).ConfigureAwait(false);
                return;
            }

            if (!Directory.Exists(targetDir))
            {
                await SendResponseAsync(stream, 404, "Not Found", "application/json", JsonConvert.SerializeObject(new BrowseResponse
                {
                    Success = false,
                    Message = "Direktori tidak ditemukan."
                })).ConfigureAwait(false);
                return;
            }

            var response = new BrowseResponse
            {
                Success = true,
                CurrentPath = reqPath.Replace('\\', '/'),
                ParentPath = GetParentRelativePath(reqPath),
                AllowDownload = user != null ? user.CanRead : AllowDownload,
                AllowUpload = user != null ? user.CanWrite : AllowUpload,
                AllowDelete = user != null ? user.CanDelete : true
            };

            try
            {
                var dirInfo = new DirectoryInfo(targetDir);

                // Add subdirectories
                foreach (var d in dirInfo.GetDirectories())
                {
                    if ((d.Attributes & FileAttributes.Hidden) != 0) continue;

                    string rel = string.IsNullOrEmpty(reqPath) ? d.Name : $"{reqPath}/{d.Name}".Replace('\\', '/');
                    response.Items.Add(new FileItem
                    {
                        Name = d.Name,
                        RelativePath = rel,
                        IsDirectory = true,
                        Size = 0,
                        FormattedSize = "-",
                        LastModified = d.LastWriteTime,
                        Extension = ""
                    });
                }

                // Add files
                foreach (var f in dirInfo.GetFiles())
                {
                    if ((f.Attributes & FileAttributes.Hidden) != 0) continue;

                    string rel = string.IsNullOrEmpty(reqPath) ? f.Name : $"{reqPath}/{f.Name}".Replace('\\', '/');
                    response.Items.Add(new FileItem
                    {
                        Name = f.Name,
                        RelativePath = rel,
                        IsDirectory = false,
                        Size = f.Length,
                        FormattedSize = NetworkHelper.FormatBytes(f.Length),
                        LastModified = f.LastWriteTime,
                        Extension = f.Extension.TrimStart('.').ToLowerInvariant()
                    });
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                response.Success = false;
                response.Message = "Izin Windows ditolak (Access Denied): Server tidak memiliki hak membaca folder ini.";
                Log($"[Akses Ditolak] Windows menolak izin baca untuk: {targetDir} ({uex.Message})", LogLevel.Error);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"Gagal membaca folder: {ex.Message}";
                Log($"[Error Browse] Gagal membaca folder: {ex.Message}", LogLevel.Error);
            }

            string json = JsonConvert.SerializeObject(response);
            await SendResponseAsync(stream, 200, "OK", "application/json", json).ConfigureAwait(false);
        }

        private async Task HandleDownloadAsync(Stream stream, Dictionary<string, string> query, string clientIp, UserAccount? user)
        {
            if (user != null && !user.CanRead)
            {
                await SendResponseAsync(stream, 403, "Forbidden", "text/plain", $"Akses ditolak: Akun '{user.Username}' tidak memiliki izin mengunduh (Read).").ConfigureAwait(false);
                return;
            }

            if (!AllowDownload && user == null)
            {
                await SendResponseAsync(stream, 403, "Forbidden", "text/plain", "Download dinonaktifkan oleh server.").ConfigureAwait(false);
                return;
            }

            string reqPath = query.ContainsKey("path") ? query["path"] : "";
            reqPath = (reqPath ?? "").Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            string targetFile = Path.Combine(SharedDirectory, reqPath);

            // Path traversal defense
            if (!NetworkHelper.IsSubPathOf(SharedDirectory, targetFile))
            {
                await SendResponseAsync(stream, 403, "Forbidden", "text/plain", "Akses file dilarang.").ConfigureAwait(false);
                return;
            }

            if (!File.Exists(targetFile))
            {
                await SendResponseAsync(stream, 404, "Not Found", "text/plain", "File tidak ditemukan.").ConfigureAwait(false);
                return;
            }

            var fi = new FileInfo(targetFile);
            string safeFileName = Uri.EscapeDataString(fi.Name);

            try
            {
                using (var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    string header = $"HTTP/1.1 200 OK\r\n" +
                                    $"Content-Type: application/octet-stream\r\n" +
                                    $"Content-Disposition: attachment; filename=\"{fi.Name}\"; filename*=UTF-8''{safeFileName}\r\n" +
                                    $"Content-Length: {fi.Length}\r\n" +
                                    "Accept-Ranges: none\r\n" +
                                    "Connection: close\r\n\r\n";

                    byte[] headerBytes = Encoding.ASCII.GetBytes(header);
                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);

                    Log(LanguageManager.Instance.IsId 
                        ? $"[Download Mulai] {clientIp} ({user?.Username ?? "Guest"}) mengunduh '{fi.Name}' ({NetworkHelper.FormatBytes(fi.Length)})"
                        : $"[Download Started] {clientIp} ({user?.Username ?? "Guest"}) downloading '{fi.Name}' ({NetworkHelper.FormatBytes(fi.Length)})", LogLevel.Info);
                    OnTransferStarted?.Invoke(clientIp, fi.Name, fi.Length);

                    byte[] buffer = new byte[128 * 1024]; // 128 KB buffer for fast LAN transfers
                    long totalSent = 0;

                    int bytesRead;
                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await stream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                        totalSent += bytesRead;
                    }

                    await stream.FlushAsync().ConfigureAwait(false);
                    Log(LanguageManager.Instance.IsId 
                        ? $"[Download Selesai] {clientIp} ({user?.Username ?? "Guest"}) selesai mengunduh '{fi.Name}' ({NetworkHelper.FormatBytes(totalSent)})"
                        : $"[Download Completed] {clientIp} ({user?.Username ?? "Guest"}) finished downloading '{fi.Name}' ({NetworkHelper.FormatBytes(totalSent)})", LogLevel.Success);
                    OnTransferCompleted?.Invoke(clientIp, fi.Name, totalSent);
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                Log($"[Download Ditolak] Izin akses Windows ditolak untuk file '{fi.Name}': {uex.Message}", LogLevel.Error);
                await SendResponseAsync(stream, 403, "Forbidden", "text/plain", "Izin Windows ditolak: File tidak dapat dibaca karena dibatasi oleh permission Windows (Access Denied).").ConfigureAwait(false);
            }
            catch (IOException ioEx)
            {
                Log($"[Download Gagal] File '{fi.Name}' sedang dikunci oleh aplikasi lain: {ioEx.Message}", LogLevel.Error);
                await SendResponseAsync(stream, 409, "Conflict", "text/plain", $"File sedang dikunci atau digunakan oleh proses lain: {ioEx.Message}").ConfigureAwait(false);
            }
        }

        private async Task HandleUploadAsync(Stream networkStream, BufferedStream reader, Dictionary<string, string> headers, Dictionary<string, string> query, string clientIp, UserAccount? user)
        {
            if (user != null && !user.CanWrite)
            {
                await SendResponseAsync(networkStream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = $"Akses ditolak: Akun '{user.Username}' tidak memiliki izin mengunggah (Write)."
                })).ConfigureAwait(false);
                return;
            }

            if (!AllowUpload && user == null)
            {
                await SendResponseAsync(networkStream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = "Upload dinonaktifkan oleh server."
                })).ConfigureAwait(false);
                return;
            }

            string reqPath = query.ContainsKey("path") ? query["path"] : "";
            string fileName = query.ContainsKey("fileName") ? query["fileName"] : "";

            if (string.IsNullOrWhiteSpace(fileName))
            {
                await SendResponseAsync(networkStream, 400, "Bad Request", "text/plain", "Parameter fileName harus disediakan.").ConfigureAwait(false);
                return;
            }

            reqPath = (reqPath ?? "").Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string targetFolder = string.IsNullOrEmpty(reqPath) ? SharedDirectory : Path.Combine(SharedDirectory, reqPath);

            // Path traversal defense
            if (!NetworkHelper.IsSubPathOf(SharedDirectory, targetFolder) && !string.Equals(Path.GetFullPath(SharedDirectory), Path.GetFullPath(targetFolder), StringComparison.OrdinalIgnoreCase))
            {
                await SendResponseAsync(networkStream, 403, "Forbidden", "text/plain", "Akses folder dilarang.").ConfigureAwait(false);
                return;
            }

            // Clean file name
            fileName = Path.GetFileName(fileName);
            string destinationFilePath = Path.Combine(targetFolder, fileName);

            long contentLength = -1;
            if (headers.TryGetValue("Content-Length", out string clStr))
            {
                long.TryParse(clStr, out contentLength);
            }

            if (contentLength <= 0)
            {
                await SendResponseAsync(networkStream, 411, "Length Required", "text/plain", "Content-Length diperlukan.").ConfigureAwait(false);
                return;
            }

            Log(LanguageManager.Instance.IsId 
                ? $"[Upload Mulai] {clientIp} mengunggah '{fileName}' ({NetworkHelper.FormatBytes(contentLength)})"
                : $"[Upload Started] {clientIp} uploading '{fileName}' ({NetworkHelper.FormatBytes(contentLength)})", LogLevel.Info);
            OnTransferStarted?.Invoke(clientIp, fileName, contentLength);

            byte[] buffer = new byte[128 * 1024];
            long bytesRemaining = contentLength;

            try
            {
                if (!Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                using (var fs = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    while (bytesRemaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesRemaining);
                        int read = await reader.ReadAsync(buffer, 0, toRead).ConfigureAwait(false);
                        if (read <= 0) break;

                        await fs.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        bytesRemaining -= read;
                    }
                }

                Log(LanguageManager.Instance.IsId 
                    ? $"[Upload Selesai] '{fileName}' berhasil disimpan dari {clientIp}"
                    : $"[Upload Completed] '{fileName}' successfully received from {clientIp}", LogLevel.Success);
                OnTransferCompleted?.Invoke(clientIp, fileName, contentLength);

                await SendResponseAsync(networkStream, 200, "OK", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = true,
                    Message = $"File '{fileName}' berhasil diunggah."
                })).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException uex)
            {
                Log($"[Upload Ditolak] Izin menulis ditolak oleh Windows di '{targetFolder}': {uex.Message}", LogLevel.Error);
                await SendResponseAsync(networkStream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = "Izin Windows ditolak (Access Denied): Server tidak memiliki izin menulis di folder ini."
                })).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[Upload Gagal] Kesalahan saat menyimpan '{fileName}': {ex.Message}", LogLevel.Error);
                await SendResponseAsync(networkStream, 500, "Internal Server Error", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = $"Gagal menyimpan file: {ex.Message}"
                })).ConfigureAwait(false);
            }
        }

        private async Task HandleDeleteAsync(Stream stream, Dictionary<string, string> query, string clientIp, UserAccount? user)
        {
            if (user != null && !user.CanDelete)
            {
                await SendResponseAsync(stream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = $"Akses ditolak: Akun '{user.Username}' tidak memiliki izin menghapus file (Delete)."
                })).ConfigureAwait(false);
                return;
            }

            string reqPath = query.ContainsKey("path") ? query["path"] : "";
            reqPath = (reqPath ?? "").Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

            if (string.IsNullOrEmpty(reqPath))
            {
                await SendResponseAsync(stream, 400, "Bad Request", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = "Path file yang akan dihapus tidak ditentukan."
                })).ConfigureAwait(false);
                return;
            }

            string targetFile = Path.Combine(SharedDirectory, reqPath);
            if (!NetworkHelper.IsSubPathOf(SharedDirectory, targetFile))
            {
                await SendResponseAsync(stream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = "Akses direktori di luar root dilarang."
                })).ConfigureAwait(false);
                return;
            }

            try
            {
                if (File.Exists(targetFile))
                {
                    File.Delete(targetFile);
                    string fileName = Path.GetFileName(targetFile);
                    Log(LanguageManager.Instance.IsId 
                        ? $"[Hapus File] '{fileName}' dihapus oleh {clientIp} (User: {user?.Username ?? "Guest"})"
                        : $"[Delete File] '{fileName}' deleted by {clientIp} (User: {user?.Username ?? "Guest"})", LogLevel.Warning);

                    await SendResponseAsync(stream, 200, "OK", "application/json", JsonConvert.SerializeObject(new
                    {
                        Success = true,
                        Message = $"File '{fileName}' berhasil dihapus."
                    })).ConfigureAwait(false);
                }
                else if (Directory.Exists(targetFile))
                {
                    Directory.Delete(targetFile, true);
                    string dirName = Path.GetFileName(targetFile);
                    Log(LanguageManager.Instance.IsId 
                        ? $"[Hapus Folder] Folder '{dirName}' dihapus oleh {clientIp} (User: {user?.Username ?? "Guest"})"
                        : $"[Delete Folder] Folder '{dirName}' deleted by {clientIp} (User: {user?.Username ?? "Guest"})", LogLevel.Warning);

                    await SendResponseAsync(stream, 200, "OK", "application/json", JsonConvert.SerializeObject(new
                    {
                        Success = true,
                        Message = $"Folder '{dirName}' berhasil dihapus."
                    })).ConfigureAwait(false);
                }
                else
                {
                    await SendResponseAsync(stream, 404, "Not Found", "application/json", JsonConvert.SerializeObject(new
                    {
                        Success = false,
                        Message = "File atau folder tidak ditemukan di server."
                    })).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                Log($"[Hapus Ditolak] Izin Windows ditolak untuk '{targetFile}': {uex.Message}", LogLevel.Error);
                await SendResponseAsync(stream, 403, "Forbidden", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = "Izin Windows ditolak (Access Denied): Server tidak memiliki izin menghapus file ini."
                })).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log($"[Hapus Gagal] Gagal menghapus '{targetFile}': {ex.Message}", LogLevel.Error);
                await SendResponseAsync(stream, 500, "Internal Server Error", "application/json", JsonConvert.SerializeObject(new
                {
                    Success = false,
                    Message = $"Gagal menghapus file: {ex.Message}"
                })).ConfigureAwait(false);
            }
        }

        private async Task HandleWebUiAsync(Stream stream, Dictionary<string, string> query, UserAccount? user)
        {
            string reqPath = query.ContainsKey("path") ? query["path"] : "";
            reqPath = (reqPath ?? "").Trim('/', '\\');

            // Render a modern, sleek web portal
            string html = GenerateWebUiHtml(reqPath, user);
            await SendResponseAsync(stream, 200, "OK", "text/html", html).ConfigureAwait(false);
        }

        private string GenerateWebUiHtml(string currentRelPath, UserAccount? user)
        {
            string cleanDir = (currentRelPath ?? "").Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            string targetDir = string.IsNullOrEmpty(cleanDir) ? SharedDirectory : Path.Combine(SharedDirectory, cleanDir);

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='id'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.Append("<title>FilanShare Web Explorer</title>");
            sb.Append("<style>");
            sb.Append("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#0f172a;color:#f8fafc;margin:0;padding:24px;}");
            sb.Append(".card{max-width:960px;margin:0 auto;background:#1e293b;border-radius:12px;border:1px solid #334155;overflow:hidden;box-shadow:0 10px 25px rgba(0,0,0,0.5);}");
            sb.Append(".header{padding:20px 24px;background:#1e293b;border-bottom:1px solid #334155;display:flex;justify-content:space-between;align-items:center;}");
            sb.Append(".title{font-size:20px;font-weight:700;color:#38bdf8;display:flex;align-items:center;gap:10px;}");
            sb.Append(".badge{background:#0369a1;color:#fff;font-size:12px;padding:4px 10px;border-radius:999px;}");
            sb.Append(".nav{padding:14px 24px;background:#0f172a;display:flex;gap:10px;align-items:center;border-bottom:1px solid #334155;}");
            sb.Append(".btn{background:#2563eb;color:#fff;padding:8px 16px;border-radius:6px;text-decoration:none;font-size:14px;font-weight:500;display:inline-flex;align-items:center;gap:6px;}");
            sb.Append(".btn:hover{background:#1d4ed8;}");
            sb.Append(".table{width:100%;border-collapse:collapse;font-size:14px;}");
            sb.Append("th{text-align:left;padding:12px 20px;background:#1e293b;color:#94a3b8;font-weight:600;border-bottom:1px solid #334155;}");
            sb.Append("td{padding:14px 20px;border-bottom:1px solid #334155;}");
            sb.Append("tr:hover{background:#334155;}");
            sb.Append(".link{color:#38bdf8;text-decoration:none;font-weight:500;}");
            sb.Append(".link:hover{text-decoration:underline;}");
            sb.Append(".size{color:#94a3b8;font-family:monospace;}");
            sb.Append("</style></head><body>");

            sb.Append("<div class='card'>");
            sb.Append("<div class='header'>");
            sb.Append("<div class='title'>⚡ FilanShare Web Explorer</div>");
            sb.Append($"<span class='badge'>User: {HttpUtility.HtmlEncode(user?.Username ?? "Guest")}</span>");
            sb.Append("</div>");

            sb.Append("<div class='nav'>");
            string parentPath = GetParentRelativePath(cleanDir);
            if (!string.IsNullOrEmpty(currentRelPath))
            {
                sb.Append($"<a href='/?path={Uri.EscapeDataString(parentPath)}' class='btn'>⬅ Kembali ke Atas</a>");
            }
            sb.Append($"<span style='color:#94a3b8;'>Lokasi: <strong style='color:#f8fafc;'>/{HttpUtility.HtmlEncode(currentRelPath)}</strong></span>");
            sb.Append("</div>");

            if (user != null && !user.CanRead)
            {
                sb.Append("<div style='padding:24px;color:#f87171;'>⚠️ Akun Anda tidak memiliki izin membaca file (Read Permission).</div>");
                sb.Append("</div></body></html>");
                return sb.ToString();
            }

            sb.Append("<table class='table'>");
            sb.Append("<thead><tr><th>Nama</th><th style='width:120px;'>Ukuran</th><th style='width:180px;'>Waktu Modifikasi</th><th style='width:120px;'>Aksi</th></tr></thead>");
            sb.Append("<tbody>");

            try
            {
                if (Directory.Exists(targetDir) && (NetworkHelper.IsSubPathOf(SharedDirectory, targetDir) || string.Equals(Path.GetFullPath(SharedDirectory), Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)))
                {
                    var di = new DirectoryInfo(targetDir);

                    foreach (var d in di.GetDirectories())
                    {
                        if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
                        string sub = string.IsNullOrEmpty(currentRelPath) ? d.Name : $"{currentRelPath}/{d.Name}";
                        sb.Append("<tr>");
                        sb.Append($"<td>📁 <a class='link' href='/?path={Uri.EscapeDataString(sub)}'>{HttpUtility.HtmlEncode(d.Name)}/</a></td>");
                        sb.Append("<td class='size'>-</td>");
                        sb.Append($"<td class='size'>{d.LastWriteTime:yyyy-MM-dd HH:mm}</td>");
                        sb.Append($"<td><a class='btn' style='padding:4px 10px;' href='/?path={Uri.EscapeDataString(sub)}'>Buka</a></td>");
                        sb.Append("</tr>");
                    }

                    foreach (var f in di.GetFiles())
                    {
                        if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                        string fileRel = string.IsNullOrEmpty(currentRelPath) ? f.Name : $"{currentRelPath}/{f.Name}";
                        sb.Append("<tr>");
                        sb.Append($"<td>📄 {HttpUtility.HtmlEncode(f.Name)}</td>");
                        sb.Append($"<td class='size'>{NetworkHelper.FormatBytes(f.Length)}</td>");
                        sb.Append($"<td class='size'>{f.LastWriteTime:yyyy-MM-dd HH:mm}</td>");
                        sb.Append($"<td><a class='btn' style='padding:4px 10px;background:#059669;' href='/api/download?path={Uri.EscapeDataString(fileRel)}'>Unduh</a></td>");
                        sb.Append("</tr>");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.Append($"<tr><td colspan='4' style='color:#ef4444;'>Error: {HttpUtility.HtmlEncode(ex.Message)}</td></tr>");
            }

            sb.Append("</tbody></table>");
            sb.Append("</div></body></html>");

            return sb.ToString();
        }

        private static string GetParentRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = path.Trim('/', '\\').Replace('\\', '/');
            int lastSlash = path.LastIndexOf('/');
            return lastSlash > 0 ? path.Substring(0, lastSlash) : "";
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return dict;

            string[] pairs = query.Split('&');
            foreach (var p in pairs)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                int eq = p.IndexOf('=');
                if (eq >= 0)
                {
                    string k = Uri.UnescapeDataString(p.Substring(0, eq));
                    string v = Uri.UnescapeDataString(p.Substring(eq + 1));
                    dict[k] = v;
                }
                else
                {
                    dict[Uri.UnescapeDataString(p)] = "";
                }
            }
            return dict;
        }
    }
}
