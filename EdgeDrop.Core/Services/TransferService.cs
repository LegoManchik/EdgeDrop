using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;

namespace EdgeDrop;

public class TransferService
{
    public static Action<IEnumerable, object>? EnableCollectionSynchronization { get; set; }

    private readonly Settings _settings;
    private FileReceiver? _receiver;
    private WebServer? _webServer;
    private readonly ObservableCollection<QueueItem> _queue = new();
    private readonly object _queueLock = new();
    private bool _isProcessingQueue;
    public bool IsProcessingQueue => _isProcessingQueue;
    private bool _isReceiving;
    public bool IsReceiving => _isReceiving;
    private CancellationTokenSource? _currentCts;
    private string? _currentTransferId;
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    public bool IsPaused => !_pauseEvent.IsSet;
    public event Action<bool>? PauseChanged;
    public ObservableCollection<QueueItem> Queue => _queue;
    public FileReceiver? Receiver => _receiver;
    public event Action<string>? StatusChanged;
    public event Action<string, string>? FileSent;
    public event Action<string, string>? FileReceived;
    public event Action<string>? TransferStarted;
    public event Action? TransferCompleted;
    public event Action<string, TransferProgressReport>? TransferProgress;
    public event Action<string, string>? TransferError;
    public event Action<string>? GeneralError;
    public Func<string, string, long, Task<bool>>? AcceptFileRequested;

    public TransferService(Settings settings)
    {
        _settings = settings;

        EnableCollectionSynchronization?.Invoke(_queue, _queueLock);
    }

    public void AttachWebServer(WebServer? webServer)
    {
        _webServer = webServer;

        if (_webServer != null)
        {
            _webServer.UploadStarted += (fileName, totalBytes) =>
            {
                _isReceiving = true;

                TransferStarted?.Invoke($"Receiving: {fileName}");
            };

            _webServer.UploadProgress += (fileName, report) =>
            {
                TransferProgress?.Invoke($"Receiving: {fileName}", report);
            };

            _webServer.UploadCompleted += fileName =>
            {
                _isReceiving = false;

                TransferCompleted?.Invoke();
            };

            _webServer.UploadCancelled += fileName =>
            {
                _isReceiving = false;

                TransferCompleted?.Invoke();
            };

            _webServer.Uploaded += path =>
            {
                string name = Path.GetFileName(path);

                FileReceived?.Invoke(name, path);
            };

            _webServer.Error += err => GeneralError?.Invoke($"Web: {err}");

            _webServer.AcceptFileRequested = (name, ip, size) =>

                AcceptFileRequested?.Invoke(name, ip, size) ?? Task.FromResult(true);
        }
    }

    public void DetachWebServer()
    {
        _webServer = null;
    }

    public void StartListening(int? port = null)
    {
        StopListening();

        int listenPort = port ?? _settings.Port;

        _receiver = new FileReceiver(listenPort, _settings.SaveFolder, _settings);

        _receiver.ReceiveStarted += (fileName, totalBytes) =>
        {
            _isReceiving = true;

            TransferStarted?.Invoke($"Receiving: {fileName}");
        };

        _receiver.ReceiveProgress += (fileName, report) =>
        {
            TransferProgress?.Invoke($"Receiving: {fileName}", report);
        };

        _receiver.ReceiveCompleted += fileName =>
        {
            _isReceiving = false;

            TransferCompleted?.Invoke();
        };

        _receiver.ReceiveCancelled += fileName =>
        {
            _isReceiving = false;

            TransferCompleted?.Invoke();
        };

        _receiver.FileReceived += path =>
        {
            string name = Path.GetFileName(path);

            FileReceived?.Invoke(name, path);
        };

        _receiver.Error += err =>
        {
            GeneralError?.Invoke($"Receiver: {err}");

            if (_isProcessingQueue || !string.IsNullOrEmpty(_currentTransferId))
            {
                TransferError?.Invoke("", err);
            }
        };

        _receiver.AcceptFileRequested = (name, ip, size) =>

            AcceptFileRequested?.Invoke(name, ip, size) ?? Task.FromResult(true);

        _receiver.Start();
    }

    public void ApproveSender(string? remoteIp)
    {
        SenderApprovalManager.Approve(remoteIp);
    }

    public void RevokeSenderApproval(string? remoteIp)
    {
        SenderApprovalManager.Revoke(remoteIp);
    }

    public void StopListening()
    {
        if (_receiver != null)
        {
            _receiver.Stop();

            _receiver = null;
        }
    }

    public void TogglePause()
    {
        if (_pauseEvent.IsSet)
        {
            _pauseEvent.Reset();

            PauseChanged?.Invoke(true);

            StatusChanged?.Invoke("Transfer paused");
        }

        else
        {
            _pauseEvent.Set();

            PauseChanged?.Invoke(false);

            StatusChanged?.Invoke("Transfer resumed");
        }
    }

    public async Task WaitIfPausedAsync(CancellationToken ct)
    {
        while (!_pauseEvent.IsSet)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(100, ct);
        }
    }

    public void SendFiles(DeviceItem? device, IEnumerable<string> files, bool isWebTarget = false)
    {
        lock (_queueLock)
        {
            foreach (var file in files)
            {
                if (File.Exists(file))
                {
                    var fi = new FileInfo(file);

                    _queue.Add(new QueueItem
                    {
                        FilePath = file,
                        FileSizeBytes = fi.Length,
                        TargetDevice = device,

                        IsWebTarget = isWebTarget
                    });
                }

                else if (Directory.Exists(file))
                {
                    _queue.Add(new QueueItem
                    {
                        FilePath = file,
                        SourceDirectoryPath = file,
                        FileSizeBytes = 0,
                        TargetDevice = device,
                        IsWebTarget = isWebTarget,
                        IsTemporaryZip = true,

                        IsDirectory = true
                    });
                }

                else
                {
                    StatusChanged?.Invoke($"Skipped (not found): {Path.GetFileName(file)}");
                }
            }
        }

        _ = ProcessQueue();
    }

    public void CancelTransfer(string? id = null)
    {
        if (!_pauseEvent.IsSet)
        {
            _pauseEvent.Set();

            PauseChanged?.Invoke(false);
        }

        if (string.IsNullOrEmpty(id) || id == _currentTransferId)
        {
            _currentCts?.Cancel();
        }

        else
        {
            lock (_queueLock)
            {
                var item = _queue.FirstOrDefault(x => x.Id == id);

                if (item != null)
                {
                    if (item.IsTemporaryZip && File.Exists(item.FilePath))
                    {
                        try { File.Delete(item.FilePath); } catch { }
                    }

                    _queue.Remove(item);
                }
            }
        }
    }

    public void CancelAllTransfers()
    {
        lock (_queueLock)
        {
            foreach (var item in _queue)
            {
                if (item.IsTemporaryZip && File.Exists(item.FilePath))
                {
                    try { File.Delete(item.FilePath); } catch { }
                }
            }

            _queue.Clear();
        }

        CancelTransfer();

        _receiver?.CancelCurrentReceive();

        _isReceiving = false;
    }

    public async Task ProcessQueue()
    {
        if (_isProcessingQueue) return;

        _isProcessingQueue = true;

        try
        {
            while (true)
            {
                QueueItem current;

                lock (_queueLock)
                {
                    if (_queue.Count == 0) break;

                    current = _queue[0];

                    _queue.RemoveAt(0);
                }

                _currentTransferId = current.Id;

                if (current.IsDirectory && !string.IsNullOrWhiteSpace(current.SourceDirectoryPath))
                {
                    string dirName = Path.GetFileName(current.SourceDirectoryPath.TrimEnd('\\', '/'));

                    if (string.IsNullOrWhiteSpace(dirName)) dirName = "Folder";

                    string tempZip = Path.Combine(Path.GetTempPath(), $"{dirName}_{Guid.NewGuid():N}.zip");

                    current.FilePath = tempZip;

                    TransferStarted?.Invoke($"{dirName}.zip");

                    StatusChanged?.Invoke($"Archiving folder {dirName}...");

                    _currentCts = new CancellationTokenSource();

                    try
                    {
                        await Task.Run(() =>
                        {
                            ZipFile.CreateFromDirectory(current.SourceDirectoryPath, tempZip, CompressionLevel.Fastest, includeBaseDirectory: true);
                        }, _currentCts.Token);

                        if (File.Exists(tempZip))
                        {
                            current.FileSizeBytes = new FileInfo(tempZip).Length;
                        }
                    }

                    catch (OperationCanceledException)
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }

                        StatusChanged?.Invoke($"Archiving cancelled: {dirName}");

                        continue;
                    }

                    catch (Exception ex)
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }

                        GeneralError?.Invoke($"Error archiving folder {dirName}: {ex.Message}");

                        TransferError?.Invoke(dirName, $"Archiving error: {ex.Message}");

                        continue;
                    }

                    finally
                    {
                        _currentCts?.Dispose();

                        _currentCts = null;
                    }
                }

                string file = current.FilePath;

                string fileName = current.FileName;

                TransferStarted?.Invoke(fileName);

                StatusChanged?.Invoke($"Sending: {fileName}...");

                try
                {
                    if (current.IsWebTarget)
                    {
                        if (_webServer != null && _webServer.HasActiveSessions)
                        {
                            _webServer.OfferFileToPhone(file);

                            StatusChanged?.Invoke($"Sent to mobile device: {fileName}");

                            FileSent?.Invoke(fileName, file);
                        }

                        else
                        {
                            throw new InvalidOperationException("No active mobile/browser sessions.");
                        }
                    }

                    else
                    {
                        string targetIp = _settings.LoopbackMode

                            ? "127.0.0.1"

                            : (current.TargetDevice != null && !string.IsNullOrWhiteSpace(current.TargetDevice.Ip) ? current.TargetDevice.Ip.Trim() : _settings.PeerIp);

                        int targetPort = (current.TargetDevice != null && current.TargetDevice.Port > 0) ? current.TargetDevice.Port : _settings.Port;

                        _currentCts = new CancellationTokenSource();

                        var progress = new Progress<TransferProgressReport>(p =>
                        {
                            TransferProgress?.Invoke(fileName, p);
                        });

                        await FileSender.SendFileAsync(file, targetIp, targetPort, progress, ct: _currentCts.Token, settings: _settings, pauseWaiter: WaitIfPausedAsync);

                        StatusChanged?.Invoke($"Sent: {fileName}");

                        FileSent?.Invoke(fileName, file);
                    }
                }

                catch (OperationCanceledException)
                {
                    StatusChanged?.Invoke($"Transfer cancelled: {fileName}");

                    TransferError?.Invoke(fileName, "Transfer cancelled by user.");
                }

                catch (Exception ex)
                {
                    StatusChanged?.Invoke($"Error: {ex.Message}");

                    TransferError?.Invoke(fileName, ex.Message);
                }

                finally
                {
                    _currentCts?.Dispose();

                    _currentCts = null;

                    _currentTransferId = null;

                    if (current.IsTemporaryZip && File.Exists(current.FilePath))
                    {
                        try { File.Delete(current.FilePath); } catch { }
                    }
                }
            }
        }

        finally
        {
            _isProcessingQueue = false;

            TransferCompleted?.Invoke();
        }
    }
}
