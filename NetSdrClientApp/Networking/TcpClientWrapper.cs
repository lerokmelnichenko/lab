using System.Net.Sockets;
using System.Text;

namespace NetSdrClientApp.Networking
{
    public class TcpClientWrapper : ITcpClient, IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        public bool Connected => _tcpClient != null && _tcpClient.Connected && _stream != null;

        public event EventHandler<byte[]>? MessageReceived;

        public TcpClientWrapper(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public void Connect()
        {
            if (Connected)
            {
                Console.WriteLine($"Already connected to {_host}:{_port}");
                return;
            }

            ResetCancellationToken();

            _tcpClient = new TcpClient();

            try
            {
                _cts = new CancellationTokenSource();
                _tcpClient.Connect(_host, _port);
                _stream = _tcpClient.GetStream();
                Console.WriteLine($"Connected to {_host}:{_port}");
                _ = StartListeningAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                ResetCancellationToken();

                SafeDisposeStream(ref _stream);
                SafeDisposeClient(ref _tcpClient);

                Console.WriteLine("Disconnected.");
            }
            catch
            {
                // не перекидаємо далі, як і в Dispose
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // dispose managed resources
                Disconnect();
            }

            // free unmanaged resources (if any) here

            _disposed = true;
        }
        

        public Task SendMessageAsync(byte[] data)
        {
            return SendCoreAsync(data);
        }

        public Task SendMessageAsync(string str)
        {
            var data = Encoding.UTF8.GetBytes(str);
            return SendCoreAsync(data);
        }

        private async Task SendCoreAsync(byte[] data)
        {
            if (!CanWrite())
            {
                throw new InvalidOperationException("Not connected to a server.");
            }

            Console.WriteLine(
                "Message sent: " +
                data.Select(b => Convert.ToString(b, toBase: 16))
                    .Aggregate((l, r) => $"{l} {r}")
            );

            await _stream!.WriteAsync(data, 0, data.Length);
        }

        private async Task StartListeningAsync()
        {
            if (!CanRead())
            {
                throw new InvalidOperationException("Not connected to a server.");
            }

            try
            {
                Console.WriteLine("Starting listening for incoming messages.");

                while (!_cts!.Token.IsCancellationRequested)
                {
                    var buffer = new byte[8194];
                    int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                    if (bytesRead > 0)
                    {
                        var data = buffer.AsSpan(0, bytesRead).ToArray();
                        MessageReceived?.Invoke(this, data);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // очікувано при Cancel
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in listening loop: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("Listener stopped.");
            }
        }

        private void ResetCancellationToken()
        {
            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        private static void SafeDisposeStream(ref NetworkStream? stream)
        {
            try
            {
                stream?.Close();
                stream?.Dispose();
            }
            catch
            {
                // ігноруємо
            }
            finally
            {
                stream = null;
            }
        }

        private static void SafeDisposeClient(ref TcpClient? client)
        {
            try
            {
                client?.Close();
                client?.Dispose();
            }
            catch
            {
                // ігноруємо
            }
            finally
            {
                client = null;
            }
        }

        private bool CanWrite() =>
            Connected && _stream != null && _stream.CanWrite;

        private bool CanRead() =>
            Connected && _stream != null && _stream.CanRead;
    }
}
