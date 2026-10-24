using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace EdgeDrop;

public partial class App : System.Windows.Application
{
    private const string PipePrefix = "EdgeDrop_Ipc_";
    private static Mutex? _singleInstanceMutex;
    private static CancellationTokenSource? _ipcServerCts;

    protected override async void OnStartup(StartupEventArgs e)
    {
        string pipeName = PipePrefix + Environment.UserName;

        _singleInstanceMutex = new Mutex(true, "Global\\EdgeDrop_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                await client.ConnectAsync(500);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                string message = e.Args.Length > 0 ? string.Join("\t", e.Args) : "--send-to\tselect\t.";
                await writer.WriteLineAsync(message);
            }
            catch
            {

            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        StartIpcServer(pipeName);
    }

    private void StartIpcServer(string pipeName)
    {
        _ipcServerCts = new CancellationTokenSource();
        var ct = _ipcServerCts.Token;

        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server);
                    string? line = await reader.ReadLineAsync(ct);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[] args = line.Split('\t');
                        Dispatcher.Invoke(() =>
                        {
                            if (MainWindow is MainWindow main)
                            {
                                main.HandleCommandLineArgs(args);
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(300, ct).ConfigureAwait(false); } catch { break; }
                }
            }
        }, ct);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ipcServerCts?.Cancel();
        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }
}
