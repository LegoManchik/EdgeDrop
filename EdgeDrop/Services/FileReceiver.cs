using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EdgeDrop;

public class FileReceiver
{
    private static readonly byte[] MagicBytes = "EDGD"u8.ToArray();
    private readonly int _port;
    private readonly string _saveFolder;
    private readonly Settings _settings;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    public Func<string, string, long, Task<bool>>? AcceptFileRequested;
    public event Action<string>? FileReceived;
    public event Action<string>? Error;
    public event Action<string, long>? ReceiveStarted;
    public event Action<string, TransferProgressReport>? ReceiveProgress;
    public event Action<string>? ReceiveCompleted;
    public event Action<string>? ReceiveCancelled;
    private CancellationTokenSource? _activeReceiveCts;

    public void CancelCurrentReceive()
    {
        try { _activeReceiveCts?.Cancel(); } catch { }
    }

    public void ApproveSender(string remoteIp, TimeSpan? duration = null)
    {
        SenderApprovalManager.Approve(remoteIp, duration);
    }

    public void RevokeSenderApproval(string remoteIp)
    {
        SenderApprovalManager.Revoke(remoteIp);
    }

    public static bool IsDangerousFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs" or ".msi" or ".reg" or ".scr" or ".com" or ".pif" or ".hta" or ".cpl" or ".jar";
    }

    public FileReceiver(int port, string saveFolder, Settings? settings = null)
    {
        _port = port;

        _saveFolder = saveFolder;

        _settings = settings ?? Settings.Load();

        Directory.CreateDirectory(_saveFolder);
    }

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.IPv6Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Server.DualMode = true;
            _listener.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"Failed to start dual-mode IPv6 listener: {ex.Message}. Falling back to IPv4.", "Receiver");

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();
        }

        Task.Run(() => CleanOldPartFiles(_saveFolder));

        _ = AcceptLoopAsync(_cts.Token);
    }

    public static void CleanOldPartFiles(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;

            var threshold = DateTime.UtcNow.AddHours(-24);

            foreach (var file in Directory.EnumerateFiles(folder, "*.edpart"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                    {
                        File.Delete(file);
                    }
                }

                catch { }
            }
        }

        catch { }
    }

    public void Stop()
    {
        _cts.Cancel();

        try { _listener?.Stop(); } catch { }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);

                _ = HandleClientAsync(client, ct);
            }

            catch (OperationCanceledException) { break; }

            catch (Exception ex) { Error?.Invoke($"Accept: {ex.Message}"); }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _activeReceiveCts = clientCts;

        var actToken = clientCts.Token;

        try
        {
            using (client)
            {
                client.ReceiveTimeout = 40000;

                client.SendTimeout = 20000;

                var endpoint = client.Client.RemoteEndPoint as IPEndPoint;

                string remoteIp = endpoint?.Address.IsIPv4MappedToIPv6 == true

                    ? endpoint.Address.MapToIPv4().ToString()

                    : endpoint?.Address.ToString() ?? "";

                if (!_settings.ContainsKnownDevice(remoteIp))
                {
                    Error?.Invoke($"Connection rejected from unknown device {remoteIp} (not in trusted list)");

                    client.Close();

                    return;
                }

                lock (_settings.DevicesLock)
                {
                    var dev = _settings.Devices.FirstOrDefault(d => string.Equals(d.Ip?.Trim(), remoteIp, StringComparison.OrdinalIgnoreCase));

                    if (dev != null)
                    {
                        dev.IsOnline = true;

                        dev.LastSeen = DateTime.UtcNow;
                    }
                }

                await using var stream = client.GetStream();

                var magicBuf = new byte[4];

                await ReadExactAsync(stream, magicBuf, ct);

                if (magicBuf.AsSpan().SequenceEqual("EDPI"u8) || magicBuf.AsSpan().SequenceEqual("PING"u8))
                {
                    await stream.WriteAsync("PONG"u8.ToArray(), ct);

                    return;
                }

                if (!magicBuf.AsSpan().SequenceEqual(MagicBytes))
                {
                    throw new InvalidDataException("Unknown transfer protocol");
                }

                int version = stream.ReadByte();

                if (version != 2 && version != 3 && version != 4)
                {
                    throw new InvalidDataException($"Unsupported protocol version: {version}");
                }

                var lenBuf = new byte[4];

                await ReadExactAsync(stream, lenBuf, ct);

                int nameLen = BitConverter.ToInt32(lenBuf);

                if (nameLen <= 0 || nameLen > 4096)

                    throw new InvalidDataException("Invalid file name length");

                var nameBuf = new byte[nameLen];

                await ReadExactAsync(stream, nameBuf, ct);

                string rawFileName = Encoding.UTF8.GetString(nameBuf);

                string fileName = Path.GetFileName(rawFileName);

                var sizeBuf = new byte[8];

                await ReadExactAsync(stream, sizeBuf, ct);

                long size = BitConverter.ToInt64(sizeBuf);

                if (size < 0) throw new InvalidDataException("Invalid file size");

                if (_settings.MaxIncomingFileSizeMB > 0 && size > _settings.MaxIncomingFileSizeMB * 1024L * 1024L)
                {
                    throw new InvalidOperationException($"File size ({size / (1024 * 1024)} MB) exceeds limit ({_settings.MaxIncomingFileSizeMB} MB)");
                }

                CheckFreeDiskSpace(_saveFolder, size);

                byte[]? expectedHash = null;

                if (version < 4)
                {
                    expectedHash = new byte[32];

                    await ReadExactAsync(stream, expectedHash, ct);
                }

                var (target, pathReservation) = FileNameHelper.ReserveUniquePath(_saveFolder, fileName);

                using (pathReservation)
                {
                    string tempTarget = target + ".edpart";

                    bool transferSucceeded = false;

                    long resumeOffset = 0;

                    try
                    {
                        if (version >= 3)
                        {
                            bool isDangerous = IsDangerousFile(fileName);

                            bool shouldPrompt = _settings.RequireAcceptIncoming || (_settings.SafeModeEnabled && isDangerous);

                            if (shouldPrompt && SenderApprovalManager.IsApproved(remoteIp))
                            {
                                shouldPrompt = false;
                            }

                            bool accepted = true;

                            if (shouldPrompt && AcceptFileRequested != null)
                            {
                                try
                                {
                                    using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(35));

                                    var acceptTask = AcceptFileRequested.Invoke(fileName, remoteIp, size);

                                    var winner = await Task.WhenAny(acceptTask, Task.Delay(35000, acceptCts.Token));

                                    if (winner == acceptTask)
                                    {
                                        accepted = await acceptTask;
                                    }

                                    else
                                    {
                                        accepted = false;
                                    }
                                }

                                catch
                                {
                                    accepted = false;
                                }

                                if (accepted)
                                {
                                    SenderApprovalManager.Approve(remoteIp);
                                }

                                else
                                {
                                    SenderApprovalManager.Revoke(remoteIp);
                                }
                            }

                            if (!accepted)
                            {
                                await stream.WriteAsync(new byte[] { 0x03 }, ct);

                                await stream.FlushAsync(ct);

                                return;
                            }

                            if (version >= 4 && File.Exists(tempTarget))
                            {
                                try
                                {
                                    long existingLen = new FileInfo(tempTarget).Length;

                                    if (existingLen > 0 && existingLen < size)
                                    {
                                        resumeOffset = existingLen;
                                    }

                                    else if (existingLen >= size)
                                    {
                                        File.Delete(tempTarget);
                                    }
                                }

                                catch
                                {
                                    resumeOffset = 0;
                                }
                            }

                            if (version >= 4 && resumeOffset > 0)
                            {
                                await stream.WriteAsync(new byte[] { 0x04 }, actToken);

                                await stream.WriteAsync(BitConverter.GetBytes(resumeOffset), actToken);

                                await stream.FlushAsync(actToken);
                            }

                            else
                            {
                                await stream.WriteAsync(new byte[] { 0x02 }, actToken);

                                await stream.FlushAsync(actToken);
                            }
                        }

                        ReceiveStarted?.Invoke(fileName, size);

                        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                        if (resumeOffset > 0)
                        {
                            await using (var existingFs = new FileStream(tempTarget, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var preBuf = new byte[81920];

                                long preCount = 0;

                                while (preCount < resumeOffset)
                                {
                                    int toRead = (int)Math.Min(preBuf.Length, resumeOffset - preCount);

                                    int r = await existingFs.ReadAsync(preBuf.AsMemory(0, toRead), ct);

                                    if (r == 0) break;

                                    sha.AppendData(preBuf.AsSpan(0, r));

                                    preCount += r;
                                }
                            }
                        }

                        FileMode fileMode = resumeOffset > 0 ? FileMode.Append : FileMode.Create;

                        await using (var fs = new FileStream(tempTarget, fileMode, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];

                            long remaining = size - resumeOffset;

                            long totalReceived = resumeOffset;

                            var sw = System.Diagnostics.Stopwatch.StartNew();

                            long lastReportMs = 0;

                            while (remaining > 0)
                            {
                                int toRead = (int)Math.Min(buffer.Length, remaining);

                                int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), actToken);

                                if (read == 0) throw new EndOfStreamException("Connection terminated before file transfer completed");

                                await fs.WriteAsync(buffer.AsMemory(0, read), actToken);

                                sha.AppendData(buffer.AsSpan(0, read));

                                remaining -= read;

                                totalReceived += read;

                                if (size > 0)
                                {
                                    long nowMs = sw.ElapsedMilliseconds;

                                    if (nowMs - lastReportMs >= 80 || remaining == 0)
                                    {
                                        lastReportMs = nowMs;

                                        double ratio = Math.Clamp((double)totalReceived / size, 0.0, 1.0);

                                        double elapsedSec = sw.Elapsed.TotalSeconds;

                                        double speed = elapsedSec > 0.05 ? (totalReceived - resumeOffset) / elapsedSec : 0;

                                        double remainingSec = speed > 0 && remaining > 0 ? remaining / speed : 0;

                                        ReceiveProgress?.Invoke(fileName, new TransferProgressReport(ratio, totalReceived, size, speed, remainingSec));
                                    }
                                }
                            }
                        }

                        byte[] actualHash = sha.GetHashAndReset();

                        if (version >= 4)
                        {
                            expectedHash = new byte[32];

                            await ReadExactAsync(stream, expectedHash, ct);
                        }

                        if (expectedHash == null || !actualHash.AsSpan().SequenceEqual(expectedHash))
                        {
                            try { File.Delete(tempTarget); } catch { }

                            await stream.WriteAsync(new byte[] { 0 }, ct);

                            await stream.FlushAsync(ct);

                            throw new CryptographicException("SHA-256 checksum mismatch: file corrupted in transit");
                        }

                        File.Move(tempTarget, target);

                        transferSucceeded = true;

                        await stream.WriteAsync(new byte[] { 1 }, ct);

                        await stream.FlushAsync(ct);

                        FileReceived?.Invoke(target);

                        ReceiveCompleted?.Invoke(fileName);

                        SenderApprovalManager.Approve(remoteIp);
                    }

                    finally
                    {
                        if (!transferSucceeded)
                        {
                            ReceiveCancelled?.Invoke(fileName);

                            if (File.Exists(tempTarget))
                            {
                                try
                                {
                                    var fi = new FileInfo(tempTarget);

                                    if (fi.Length == 0 || actToken.IsCancellationRequested)
                                    {
                                        File.Delete(tempTarget);
                                    }
                                }

                                catch { }
                            }
                        }
                    }
                }
            }
        }

        catch (OperationCanceledException) { }

        catch (EndOfStreamException)

        { }

        catch (IOException ioEx) when (ioEx.InnerException is SocketException)

        { }

        catch (Exception ex)
        {
            Error?.Invoke(ex.Message);
        }

        finally
        {
            _activeReceiveCts = null;
        }
    }

    private static void CheckFreeDiskSpace(string folder, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));

            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);

                if (drive.IsReady)
                {
                    long requiredWithMargin = requiredBytes + (10L * 1024 * 1024);

                    if (drive.AvailableFreeSpace < requiredWithMargin)
                    {
                        throw new IOException($"Insufficient free disk space ({drive.AvailableFreeSpace / 1024 / 1024} MB available, {requiredBytes / 1024 / 1024} MB required)");
                    }
                }
            }
        }

        catch (IOException)
        {
            throw;
        }

        catch

        { }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);

            if (read == 0) throw new EndOfStreamException("Connection terminated before transfer finished");

            offset += read;
        }
    }
}
