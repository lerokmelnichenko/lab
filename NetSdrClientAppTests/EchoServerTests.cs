using System.Net;
using System.Net.Sockets;
using System.Text;
using EchoServer;

namespace NetSdrClientAppTests
{
    [TestFixture]
    [Parallelizable(ParallelScope.None)] // щоб тести з сокетами не билися між собою
    public class EchoServerTests
    {
        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        [Test]
        public async Task StartAsync_ShouldEchoBackData()
        {
            // Arrange
            int port = GetFreePort();
            var server = new EchoServer.EchoServer(port);

            // запускаємо сервер у бекграунді
            var serverTask = Task.Run(() => server.StartAsync());

            // даємо серверу піднятись
            await Task.Delay(100);

            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", port);
            using var stream = client.GetStream();

            var message = "Hello, Echo!";
            var data = Encoding.UTF8.GetBytes(message);

            // Act: шлемо дані
            await stream.WriteAsync(data, 0, data.Length);

            // читаємо відповідь
            var buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            var received = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            // зупиняємо сервер
            client.Close();
            server.Stop();
            await Task.WhenAny(serverTask, Task.Delay(1000));

            // Assert
            Assert.That(message, Is.EqualTo(received));
        }

        [Test]
        public async Task Stop_ShouldStopServerLoop()
        {
            // Arrange
            int port = GetFreePort();
            var server = new EchoServer.EchoServer(port);

            var serverTask = Task.Run(() => server.StartAsync());

            await Task.Delay(100);

            // Act
            server.Stop();

            var completed = await Task.WhenAny(serverTask, Task.Delay(1000)) == serverTask;

            // Assert
            Assert.That(completed, Is.True, "Після Stop() метод StartAsync має завершитись.");
        }
    }

    [TestFixture]
    [Parallelizable(ParallelScope.None)]
    public class UdpTimedSenderTests
    {
        private static int GetFreeUdpPort()
        {
            var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            int port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
            udp.Close();
            return port;
        }

        [Test]
        public async Task StartSending_ShouldSendAtLeastOneDatagram()
        {
            // Arrange
            int port = GetFreeUdpPort();

            using var udpListener = new UdpClient(port);
            udpListener.Client.ReceiveTimeout = 2000; // 2 секунди

            using var sender = new UdpTimedSender("127.0.0.1", port);

            var receiveTask = Task.Run(async () =>
            {
                try
                {
                    // чекаємо один пакет
                    var result = await udpListener.ReceiveAsync();
                    return result.Buffer.Length;
                }
                catch (Exception)
                {
                    return 0;
                }
            });

            // Act
            sender.StartSending(100); // кожні 100 мс

            var completed = await Task.WhenAny(receiveTask, Task.Delay(2000)) == receiveTask;
            sender.StopSending();

            int receivedLen = completed ? receiveTask.Result : 0;

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(completed, Is.True, "За 2 секунди мав прилетіти хоча б один UDP пакет.");
                Assert.That(receivedLen, Is.GreaterThan(0), "Пакет не мав бути порожнім.");
            }
        }

        [Test]
        public void StartSending_Twice_ShouldThrowInvalidOperationException()
        {
            // Arrange
            using var sender = new UdpTimedSender("127.0.0.1", 60000);

            // Act
            sender.StartSending(100);

            // Assert
            Assert.Throws<InvalidOperationException>(() => sender.StartSending(100));
        }

        [Test]
        public void StopSending_WithoutStart_ShouldNotThrow()
        {
            // Arrange
            using var sender = new UdpTimedSender("127.0.0.1", 60000);

            // Act + Assert
            Assert.DoesNotThrow(() => sender.StopSending());
        }

        [Test]
        public void Dispose_ShouldStopWithoutException()
        {
            // Arrange + Act + Assert
            Assert.DoesNotThrow(() =>
            {
                using var sender = new UdpTimedSender("127.0.0.1", 60000);
                sender.StartSending(100);
                sender.Dispose(); // Dispose викличе StopSending і закриє UdpClient
            });
        }
    }
}
