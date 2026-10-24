using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace EdgeDrop;

public record TransferProgressReport(
    double Ratio,
    long BytesSent,
    long TotalBytes,
    double BytesPerSecond,
    double RemainingSeconds);

public static class FileSender
{
    private static readonly byte[] MagicBytes = "EDGD"u8.ToArray();
    private const byte ProtocolVersion = 4;

    public static Task SendFileAsync(
        string filePath,
        string peerIp,
        int port,
        IProgress<double>? progress,
        CancellationToken ct = default,
        Settings? settings = null)
    {
        var wrapper = progress == null ? null : new Progress<TransferProgressReport>(r => progress.Report(r.Ratio));
        return SendFileAsync(filePath, peerIp, port, wrapper, ct, settings);
    }

    public static async Task SendFileAsync(
        string filePath,
        string peerIp,
        int port,
        IProgress<TransferProgressReport>? progress = null,
        CancellationToken ct = default,
        Settings? settings = null,
        Func<CancellationToken, Task>? pauseWaiter = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        if (settings != null && !settings.ContainsKnownDevice(peerIp))
        {
            throw new InvalidOperationException($"Device with IP {peerIp} not found in trusted devices list. Please add it under Devices section.");
        }

        var fileInfo = new FileInfo(filePath);
        long fileSize = fileInfo.Length;

        using var client = new TcpClient();
        client.SendTimeout = 35000;
        client.ReceiveTimeout = 35000;

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            await client.ConnectAsync(peerIp, port, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Could not connect to {peerIp}:{port} within 5 seconds. Check IP address, ensure EdgeDrop is running on target PC, and check firewall settings.");
        }

        await using var stream = client.GetStream();

        await stream.WriteAsync(MagicBytes, ct);
        stream.WriteByte(ProtocolVersion);

        var nameBytes = Encoding.UTF8.GetBytes(fileInfo.Name);
        await stream.WriteAsync(BitConverter.GetBytes(nameBytes.Length), ct);
        await stream.WriteAsync(nameBytes, ct);
        await stream.WriteAsync(BitConverter.GetBytes(fileSize), ct);
        await stream.FlushAsync(ct);

        var handshakeBuf = new byte[1];
        using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        handshakeCts.CancelAfter(TimeSpan.FromSeconds(45));
        int handshakeRead;
        try
        {
            handshakeRead = await stream.ReadAsync(handshakeBuf.AsMemory(0, 1), handshakeCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Transfer handshake timed out (45s).");
        }

        if (handshakeRead == 0)
        {
            throw new IOException("Connection closed by recipient (device may not be trusted or handshake timed out).");
        }

        if (handshakeBuf[0] == 0x03)
        {
            throw new IOException("Recipient rejected file transfer.");
        }

        long resumeOffset = 0;
        if (handshakeBuf[0] == 0x04)
        {
            var offsetBuf = new byte[8];
            await ReadExactAsync(stream, offsetBuf, ct);
            resumeOffset = BitConverter.ToInt64(offsetBuf);
            if (resumeOffset < 0 || resumeOffset >= fileSize)
            {
                resumeOffset = 0;
            }
        }
        else if (handshakeBuf[0] != 0x02)
        {
            throw new IOException($"Unknown response from recipient: {handshakeBuf[0]}");
        }

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        long totalSent = 0;
        if (resumeOffset > 0)
        {
            var preBuf = new byte[81920];
            long preHashed = 0;
            while (preHashed < resumeOffset)
            {
                int toRead = (int)Math.Min(preBuf.Length, resumeOffset - preHashed);
                int r = await fs.ReadAsync(preBuf.AsMemory(0, toRead), ct);
                if (r == 0) break;
                sha.AppendData(preBuf.AsSpan(0, r));
                preHashed += r;
            }
            fs.Seek(resumeOffset, SeekOrigin.Begin);
            totalSent = resumeOffset;
        }

        var buffer = new byte[81920];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastReportMs = 0;

        while (totalSent < fileSize)
        {
            ct.ThrowIfCancellationRequested();

            if (pauseWaiter != null)
            {
                await pauseWaiter(ct);
            }

            if (settings != null && settings.SpeedLimitKBps > 0)
            {
                long limitBytesPerSec = settings.SpeedLimitKBps * 1024L;
                long expectedElapsedMs = (totalSent - resumeOffset) * 1000L / limitBytesPerSec;
                long actualElapsedMs = sw.ElapsedMilliseconds;
                if (expectedElapsedMs > actualElapsedMs)
                {
                    int delayMs = (int)Math.Min(500, expectedElapsedMs - actualElapsedMs);
                    if (delayMs > 0)
                    {
                        await Task.Delay(delayMs, ct);
                    }
                }
            }

            int toRead = (int)Math.Min(buffer.Length, fileSize - totalSent);
            int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0) break;

            sha.AppendData(buffer.AsSpan(0, read));
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalSent += read;

            if (fileSize > 0)
            {
                long nowMs = sw.ElapsedMilliseconds;
                if (nowMs - lastReportMs >= 40 || totalSent >= fileSize)
                {
                    lastReportMs = nowMs;
                    double ratio = (double)totalSent / fileSize;
                    double elapsedSec = sw.Elapsed.TotalSeconds;
                    double speed = elapsedSec > 0.05 ? (totalSent - resumeOffset) / elapsedSec : 0;
                    double remainingSec = speed > 0 ? (fileSize - totalSent) / speed : 0;
                    progress?.Report(new TransferProgressReport(ratio, totalSent, fileSize, speed, remainingSec));
                }
            }
        }

        await stream.FlushAsync(ct);

        byte[] finalHash = sha.GetHashAndReset();
        await stream.WriteAsync(finalHash, ct);
        await stream.FlushAsync(ct);

        using var ackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ackCts.CancelAfter(TimeSpan.FromSeconds(20));
        var ackBuf = new byte[1];
        int ackRead;
        try
        {
            ackRead = await stream.ReadAsync(ackBuf.AsMemory(0, 1), ackCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for recipient file integrity confirmation.");
        }

        if (ackRead == 0 || ackBuf[0] != 1)
        {
            throw new IOException("Recipient reported file integrity verification failure.");
        }
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
