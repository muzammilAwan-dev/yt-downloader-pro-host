using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace YTDLPHost.Services
{
    public class SingleInstanceManager : IDisposable
    {
        private const string MutexName = "Global\\YTDownloaderPro_SingleInstance";
        private const string PipeName = "YTDLPHost_Pipe";
        private Mutex? _mutex;
        private bool _ownsMutex;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;

        public event EventHandler<string>? UrlReceived;

        public bool IsFirstInstance { get; private set; }

        public bool Initialize()
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            IsFirstInstance = _ownsMutex;

            if (IsFirstInstance)
            {
                StartPipeServer();
                return true;
            }

            return false;
        }

        private void StartPipeServer()
        {
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => RunPipeServer(_cts.Token));
        }

        private async Task RunPipeServer(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await pipeServer.WaitForConnectionAsync(cancellationToken);

                    using var reader = new StreamReader(pipeServer);
                    var url = await reader.ReadLineAsync(cancellationToken);

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        Application.Current?.Dispatcher.BeginInvoke(
                            DispatcherPriority.Normal,
                            () => UrlReceived?.Invoke(this, url.Trim()));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
        }

        public static async Task<bool> SendUrlToRunningInstanceAsync(string url)
        {
            try
            {
                await using var pipeClient = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                await pipeClient.ConnectAsync(3000);

                await using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                await writer.WriteLineAsync(url);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();

            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            _cts?.Dispose();

            if (_ownsMutex && _mutex != null)
            {
                _mutex.ReleaseMutex();
            }

            _mutex?.Dispose();
        }
    }
}
