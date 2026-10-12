using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileSender.Helpers;
using FileSender.Models;
using Newtonsoft.Json;

namespace FileSender.Services
{
    public class FileHttpClient
    {
        private HttpClient CreateClient(string username, string password, int timeoutSeconds = 60)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
            {
                string authString = $"{username}:{password}";
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(authString));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);
            }

            return client;
        }

        public async Task<PingResponse> PingAsync(string host, int port, string user, string pass)
        {
            string url = $"http://{host}:{port}/api/ping";
            using (var client = CreateClient(user, pass, timeoutSeconds: 5))
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<PingResponse>(json);
            }
        }

        public async Task<BrowseResponse> BrowseAsync(string host, int port, string user, string pass, string relativePath)
        {
            string url = $"http://{host}:{port}/api/browse?path={Uri.EscapeDataString(relativePath ?? "")}";
            using (var client = CreateClient(user, pass, timeoutSeconds: 15))
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonConvert.DeserializeObject<BrowseResponse>(json);
            }
        }

        public async Task DownloadFileAsync(
            string host, 
            int port, 
            string user, 
            string pass, 
            string remoteRelativePath, 
            string localDestinationPath, 
            IProgress<TransferProgressInfo> progress, 
            CancellationToken cancellationToken)
        {
            string url = $"http://{host}:{port}/api/download?path={Uri.EscapeDataString(remoteRelativePath)}";
            string fileName = Path.GetFileName(remoteRelativePath);

            using (var client = CreateClient(user, pass, timeoutSeconds: 7200)) // 2 hours max for large transfers
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1;

                string destDir = Path.GetDirectoryName(localDestinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fileStream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    byte[] buffer = new byte[128 * 1024];
                    long totalRead = 0;
                    var sw = Stopwatch.StartNew();
                    long lastReportedBytes = 0;
                    double lastReportTime = 0;

                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                        totalRead += bytesRead;

                        double elapsed = sw.Elapsed.TotalSeconds;
                        if (elapsed - lastReportTime >= 0.25 || totalRead == totalBytes)
                        {
                            double timeDiff = elapsed - lastReportTime;
                            long byteDiff = totalRead - lastReportedBytes;
                            double speed = timeDiff > 0 ? byteDiff / timeDiff : 0;

                            progress?.Report(new TransferProgressInfo
                            {
                                FileName = fileName,
                                BytesTransferred = totalRead,
                                TotalBytes = totalBytes,
                                SpeedBytesPerSec = speed,
                                FormattedSpeed = $"{NetworkHelper.FormatBytes((long)speed)}/s",
                                FormattedProgress = totalBytes > 0 
                                    ? $"{NetworkHelper.FormatBytes(totalRead)} / {NetworkHelper.FormatBytes(totalBytes)}"
                                    : NetworkHelper.FormatBytes(totalRead),
                                Status = "Mengunduh...",
                                IsCompleted = false
                            });

                            lastReportedBytes = totalRead;
                            lastReportTime = elapsed;
                        }
                    }

                    progress?.Report(new TransferProgressInfo
                    {
                        FileName = fileName,
                        BytesTransferred = totalRead,
                        TotalBytes = totalBytes,
                        SpeedBytesPerSec = 0,
                        FormattedSpeed = "Selesai",
                        FormattedProgress = $"{NetworkHelper.FormatBytes(totalRead)} / {NetworkHelper.FormatBytes(totalBytes)}",
                        Status = "Selesai",
                        IsCompleted = true
                    });
                }
            }
        }

        public async Task UploadFileAsync(
            string host,
            int port,
            string user,
            string pass,
            string localFilePath,
            string remoteTargetDirectory,
            IProgress<TransferProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException("File lokal tidak ditemukan.", localFilePath);
            }

            var fi = new FileInfo(localFilePath);
            string fileName = fi.Name;
            long totalBytes = fi.Length;

            string url = $"http://{host}:{port}/api/upload?path={Uri.EscapeDataString(remoteTargetDirectory ?? "")}&fileName={Uri.EscapeDataString(fileName)}";

            using (var client = CreateClient(user, pass, timeoutSeconds: 7200))
            using (var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var progressStream = new ProgressStream(fileStream, totalBytes, fileName, progress))
            using (var content = new StreamContent(progressStream, 128 * 1024))
            {
                content.Headers.ContentLength = totalBytes;
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                progress?.Report(new TransferProgressInfo
                {
                    FileName = fileName,
                    BytesTransferred = totalBytes,
                    TotalBytes = totalBytes,
                    SpeedBytesPerSec = 0,
                    FormattedSpeed = "Selesai",
                    FormattedProgress = $"{NetworkHelper.FormatBytes(totalBytes)} / {NetworkHelper.FormatBytes(totalBytes)}",
                    Status = "Selesai",
                    IsCompleted = true
                });
            }
        }

        public async Task<bool> DeleteFileAsync(string host, int port, string user, string pass, string relativePath)
        {
            string url = $"http://{host}:{port}/api/delete?path={Uri.EscapeDataString(relativePath ?? "")}";
            using (var client = CreateClient(user, pass, timeoutSeconds: 15))
            {
                var response = await client.DeleteAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                return true;
            }
        }

        private class ProgressStream : Stream
        {
            private readonly Stream _innerStream;
            private readonly long _totalBytes;
            private readonly string _fileName;
            private readonly IProgress<TransferProgressInfo> _progress;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private long _totalRead = 0;
            private long _lastReportedBytes = 0;
            private double _lastReportTime = 0;

            public ProgressStream(Stream innerStream, long totalBytes, string fileName, IProgress<TransferProgressInfo> progress)
            {
                _innerStream = innerStream;
                _totalBytes = totalBytes;
                _fileName = fileName;
                _progress = progress;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => _innerStream.CanSeek;
            public override bool CanWrite => _innerStream.CanWrite;
            public override long Length => _innerStream.Length;
            public override long Position
            {
                get => _innerStream.Position;
                set => _innerStream.Position = value;
            }

            public override void Flush() => _innerStream.Flush();

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = _innerStream.Read(buffer, offset, count);
                Report(read);
                return read;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                int read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
                Report(read);
                return read;
            }

            private void Report(int read)
            {
                if (read > 0)
                {
                    _totalRead += read;
                    double elapsed = _sw.Elapsed.TotalSeconds;
                    if (elapsed - _lastReportTime >= 0.25 || _totalRead == _totalBytes)
                    {
                        double timeDiff = elapsed - _lastReportTime;
                        long byteDiff = _totalRead - _lastReportedBytes;
                        double speed = timeDiff > 0 ? byteDiff / timeDiff : 0;

                        _progress?.Report(new TransferProgressInfo
                        {
                            FileName = _fileName,
                            BytesTransferred = _totalRead,
                            TotalBytes = _totalBytes,
                            SpeedBytesPerSec = speed,
                            FormattedSpeed = $"{NetworkHelper.FormatBytes((long)speed)}/s",
                            FormattedProgress = $"{NetworkHelper.FormatBytes(_totalRead)} / {NetworkHelper.FormatBytes(_totalBytes)}",
                            Status = "Mengunggah...",
                            IsCompleted = false
                        });

                        _lastReportedBytes = _totalRead;
                        _lastReportTime = elapsed;
                    }
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
            public override void SetLength(long value) => _innerStream.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        }
    }
}
