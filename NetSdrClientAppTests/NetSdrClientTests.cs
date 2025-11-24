using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using NUnit.Framework;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrClientTests
    {
        private Mock<ITcpClient> _tcpClientMock = null!;
        private Mock<IUdpClient> _udpClientMock = null!;

        private EventHandler<byte[]>? _tcpMessageHandler;
        private EventHandler<byte[]>? _udpMessageHandler;

        private NetSdrClient _client = null!;

        [SetUp]
        public void SetUp()
        {
            _tcpClientMock = new Mock<ITcpClient>(MockBehavior.Strict);
            _udpClientMock = new Mock<IUdpClient>(MockBehavior.Strict);

            // За замовчуванням — не підключений
            _tcpClientMock
                .SetupGet(c => c.Connected)
                .Returns(false);

            // Підписка/відписка на TCP MessageReceived
            _tcpClientMock
                .SetupAdd(c => c.MessageReceived += It.IsAny<EventHandler<byte[]>>())
                .Callback<EventHandler<byte[]>>(h => _tcpMessageHandler += h);

            _tcpClientMock
                .SetupRemove(c => c.MessageReceived -= It.IsAny<EventHandler<byte[]>>())
                .Callback<EventHandler<byte[]>>(h => _tcpMessageHandler -= h);

            // Підписка/відписка на UDP MessageReceived
            _udpClientMock
                .SetupAdd(c => c.MessageReceived += It.IsAny<EventHandler<byte[]>>())
                .Callback<EventHandler<byte[]>>(h => _udpMessageHandler += h);

            _udpClientMock
                .SetupRemove(c => c.MessageReceived -= It.IsAny<EventHandler<byte[]>>())
                .Callback<EventHandler<byte[]>>(h => _udpMessageHandler -= h);

            // Емуляція відправки TCP-повідомлення:
            // одразу ж шлем "відповідь" назад через event, щоб розбудити TaskCompletionSource
            _tcpClientMock
                .Setup(c => c.SendMessageAsync(It.IsAny<byte[]>()))
                .Returns<byte[]>(msg =>
                {
                    _tcpMessageHandler?.Invoke(_tcpClientMock.Object, new byte[] { 0xAA, 0xBB });
                    return Task.CompletedTask;
                });

            // UDP start/stop — просто пусті імплементації
            _udpClientMock
                .Setup(c => c.StartListeningAsync())
                .Returns(Task.CompletedTask);

            _udpClientMock
                .Setup(c => c.StopListening());

            _udpClientMock
                .Setup(c => c.Exit());

            _tcpClientMock.Setup(c => c.Connect());
            _tcpClientMock.Setup(c => c.Disconnect());

            _client = new NetSdrClient(_tcpClientMock.Object, _udpClientMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists("samples.bin"))
            {
                File.Delete("samples.bin");
            }
        }

        [Test]
        public async Task ConnectAsync_WhenNotConnected_ShouldConnectAndSendPreSetupMessages()
        {
            // Перший виклик Connected -> false (зайдємо в if)
            // Далі (для SendTcpRequest) -> true, true, true
            _tcpClientMock.SetupSequence(c => c.Connected)
                .Returns(false) // перевірка в ConnectAsync
                .Returns(true)  // перевірка в 1-му SendTcpRequest
                .Returns(true)  // у 2-му
                .Returns(true); // у 3-му

            // Act
            await _client.ConnectAsync();

            // Assert
            _tcpClientMock.Verify(c => c.Connect(), Times.Once);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
        }

        [Test]
        public async Task ConnectAsync_WhenAlreadyConnected_ShouldNotSendAnything()
        {
            // Arrange
            _tcpClientMock.SetupGet(c => c.Connected).Returns(true);

            // Act
            await _client.ConnectAsync();

            // Assert
            _tcpClientMock.Verify(c => c.Connect(), Times.Never);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        }

        [Test]
        public void Disconect_ShouldCallTcpDisconnect()
        {
            // Act
            _client.Disconect();

            // Assert
            _tcpClientMock.Verify(c => c.Disconnect(), Times.Once);
        }

        [Test]
        public async Task StartIQAsync_WhenNotConnected_ShouldNotSendAndNotStartUdp()
        {
            // Arrange: Connected = false

            // Act
            await _client.StartIQAsync();

            // Assert
            Assert.IsFalse(_client.IQStarted);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
            _udpClientMock.Verify(c => c.StartListeningAsync(), Times.Never);
        }

        [Test]
        public async Task StartIQAsync_WhenConnected_ShouldSendReceiverStateAndStartUdp()
        {
            // Arrange
            _tcpClientMock.SetupGet(c => c.Connected).Returns(true);

            // Act
            await _client.StartIQAsync();

            // Assert
            Assert.IsTrue(_client.IQStarted);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
            _udpClientMock.Verify(c => c.StartListeningAsync(), Times.Once);
        }

        [Test]
        public async Task StopIQAsync_WhenNotConnected_ShouldNotSendAndNotStopUdp()
        {
            // Arrange: Connected = false

            // Act
            await _client.StopIQAsync();

            // Assert
            Assert.IsFalse(_client.IQStarted);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
            _udpClientMock.Verify(c => c.StopListening(), Times.Never);
        }

        [Test]
        public async Task StopIQAsync_WhenConnected_ShouldSendReceiverStateAndStopUdp()
        {
            // Arrange
            _tcpClientMock.SetupGet(c => c.Connected).Returns(true);
            _client.IQStarted = true;

            // Act
            await _client.StopIQAsync();

            // Assert
            Assert.IsFalse(_client.IQStarted);
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
            _udpClientMock.Verify(c => c.StopListening(), Times.Once);
        }

        [Test]
        public async Task ChangeFrequencyAsync_WhenConnected_ShouldSendControlItemMessage()
        {
            // Arrange
            _tcpClientMock.SetupGet(c => c.Connected).Returns(true);
            _tcpClientMock.Invocations.Clear(); // щоб не плутатись з попередніми викликами

            long hz = 123_456_789;
            int channel = 1;

            // Act
            await _client.ChangeFrequencyAsync(hz, channel);

            // Assert
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        }

        [Test]
        public async Task Tcp_Response_ShouldCompletePendingRequest()
        {
            // Arrange
            _tcpClientMock.SetupGet(c => c.Connected).Returns(true);
            _tcpClientMock.Invocations.Clear();

            // Act — будь-який метод, що використовує SendTcpRequest
            await _client.ChangeFrequencyAsync(1_000_000, 0);

            // Якщо тест не завис, значить TaskCompletionSource закрився по події
            _tcpClientMock.Verify(c => c.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
        }

        [Test]
        public void Udp_MessageReceived_ShouldWriteSamplesToFile()
        {
            // Arrange
            if (File.Exists("samples.bin"))
                File.Delete("samples.bin");

            // робимо валідний data item з двома 16-бітними семплами 1 і 2
            ushort seq = 0;
            var seqBytes = BitConverter.GetBytes(seq);

            var iqPayload = new byte[]
            {
                0x01, 0x00, // 1
                0x02, 0x00  // 2
            };

            var parameters = seqBytes.Concat(iqPayload).ToArray();

            var udpMsg = NetSdrMessageHelper.GetDataItemMessage(
                MsgTypes.DataItem0,
                parameters);

            // Act — емулюємо отримання UDP-пакету
            _udpMessageHandler?.Invoke(_udpClientMock.Object, udpMsg);

            // Assert
            Assert.IsTrue(File.Exists("samples.bin"), "samples.bin має бути створений.");

            var bytes = File.ReadAllBytes("samples.bin");

            // принаймні 4 байти (2 short)
            Assert.GreaterOrEqual(bytes.Length, 4, "Очікуємо хоча б 2 семпли * 2 байти.");

            var sample1 = BitConverter.ToInt16(bytes, 0);
            var sample2 = BitConverter.ToInt16(bytes, 2);

            Assert.That(sample1, Is.EqualTo(1));
            Assert.That(sample2, Is.EqualTo(2));
        }

    }
}
