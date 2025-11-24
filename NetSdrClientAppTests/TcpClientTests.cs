using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;
using NUnit.Framework;

namespace NetSdrClientAppTests
{
    [TestFixture]
    [Parallelizable(ParallelScope.None)]
    public class TcpClientWrapperTests
    {
        private static TcpListener StartTestServer(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        [Test]
        public void Connect_WhenNoServerRunning_ShouldNotThrow_AndRemainDisconnected()
        {
            // Arrange: використовуємо порт, де ніхто не слухає
            int port = 65000;
            var client = new TcpClientWrapper("127.0.0.1", port);

            // Act
            Assert.DoesNotThrow(() => client.Connect());

            // Assert
            Assert.IsFalse(client.Connected);
            client.Dispose();
        }

        [Test]
        public async Task Connect_WhenServerRunning_ShouldConnectAndBeConnected()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            // Act
            clientWrapper.Connect();

            // Сервер приймає підключення
            var serverClient = await listener.AcceptTcpClientAsync();

            // Трошки почекаємо, щоб wrapper встиг створити stream
            await Task.Delay(100);

            // Assert
            Assert.IsTrue(clientWrapper.Connected, "Після успішного підключення Connected має бути true.");

            clientWrapper.Disconnect();
            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public async Task Connect_WhenAlreadyConnected_ShouldNotThrow()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            clientWrapper.Connect();
            var serverClient = await listener.AcceptTcpClientAsync();

            await Task.Delay(50);

            // Act + Assert
            Assert.DoesNotThrow(() => clientWrapper.Connect(), "Повторний Connect при вже активному з'єднанні не має падати.");
            Assert.IsTrue(clientWrapper.Connected);

            clientWrapper.Disconnect();
            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public async Task SendMessageAsync_ByteArray_ShouldSendDataToServer()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            clientWrapper.Connect();
            var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();

            await Task.Delay(50);

            var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };

            // Act
            await clientWrapper.SendMessageAsync(payload);

            // Читаємо на стороні сервера
            var buffer = new byte[1024];
            int read = await serverStream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            Assert.That(read, Is.EqualTo(payload.Length));
            var received = new byte[read];
            Array.Copy(buffer, received, read);
            CollectionAssert.AreEqual(payload, received);

            clientWrapper.Disconnect();
            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public void SendMessageAsync_ByteArray_WhenNotConnected_ShouldThrow()
        {
            // Arrange
            var clientWrapper = new TcpClientWrapper("127.0.0.1", 65000);
            var payload = new byte[] { 0x01 };

            // Act + Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await clientWrapper.SendMessageAsync(payload);
            });

            clientWrapper.Dispose();
        }

        [Test]
        public async Task SendMessageAsync_String_ShouldSendUtf8ToServer()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            clientWrapper.Connect();
            var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();

            await Task.Delay(50);

            string message = "Hello, TCP!";
            var expectedBytes = Encoding.UTF8.GetBytes(message);

            // Act
            await clientWrapper.SendMessageAsync(message);

            // Читаємо на стороні сервера
            var buffer = new byte[1024];
            int read = await serverStream.ReadAsync(buffer, 0, buffer.Length);

            // Assert
            Assert.That(read, Is.EqualTo(expectedBytes.Length));
            var received = new byte[read];
            Array.Copy(buffer, received, read);
            CollectionAssert.AreEqual(expectedBytes, received);

            clientWrapper.Disconnect();
            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public async Task StartListeningAsync_ShouldRaiseMessageReceived_WhenServerSendsData()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            byte[]? received = null;
            using var mre = new ManualResetEventSlim(false);

            clientWrapper.MessageReceived += (sender, data) =>
            {
                received = data;
                mre.Set();
            };

            clientWrapper.Connect();
            var serverClient = await listener.AcceptTcpClientAsync();
            using var serverStream = serverClient.GetStream();

            await Task.Delay(100); // Дати час StartListeningAsync підняти цикл

            var payload = new byte[] { 0xAA, 0xBB, 0xCC };

            // Act — сервер шле дані клієнту
            await serverStream.WriteAsync(payload, 0, payload.Length);
            await serverStream.FlushAsync();

            var signalled = mre.Wait(1000);

            // Assert
            Assert.IsTrue(signalled, "Очікували, що подія MessageReceived спрацює.");
            Assert.IsNotNull(received);
            CollectionAssert.AreEqual(payload, received);

            clientWrapper.Disconnect();
            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public async Task Disconnect_ShouldCloseConnection_AndSetConnectedFalse()
        {
            // Arrange
            var listener = StartTestServer(out int port);
            var clientWrapper = new TcpClientWrapper("127.0.0.1", port);

            clientWrapper.Connect();
            var serverClient = await listener.AcceptTcpClientAsync();

            await Task.Delay(50);
            Assert.IsTrue(clientWrapper.Connected);

            // Act
            clientWrapper.Disconnect();

            // Assert
            Assert.IsFalse(clientWrapper.Connected);

            serverClient.Close();
            listener.Stop();
        }

        [Test]
        public void Disconnect_CanBeCalledMultipleTimes_WithoutException()
        {
            // Arrange
            var clientWrapper = new TcpClientWrapper("127.0.0.1", 65000);

            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                clientWrapper.Disconnect();
                clientWrapper.Disconnect();
            });

            clientWrapper.Dispose();
        }

        [Test]
        public void Dispose_CanBeCalledMultipleTimes_WithoutException()
        {
            // Arrange
            var clientWrapper = new TcpClientWrapper("127.0.0.1", 65000);

            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                clientWrapper.Dispose();
                clientWrapper.Dispose();
            });
        }
    }
}
