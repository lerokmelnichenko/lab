using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;
using NUnit.Framework;


namespace NetSdrClientAppTests
{
    [TestFixture]
    [Parallelizable(ParallelScope.None)]
    public class UdpClientTests
    {
        private static int GetRandomPort()
        {
            // Примітивний спосіб уникнути конфлікту портів між запуском тестів
            return 40000 + Random.Shared.Next(0, 10000);
        }

        [Test]
        public async Task StartListeningAsync_ShouldRaise_MessageReceived_WhenDatagramArrives()
        {
            // Arrange
            var port = GetRandomPort();
            using var wrapper = new UdpClientWrapper(port);

            byte[]? received = null;
            using var mre = new ManualResetEventSlim(false);

            wrapper.MessageReceived += (sender, data) =>
            {
                received = data;
                mre.Set();
            };

            var listeningTask = wrapper.StartListeningAsync();

            // даємо трохи часу на bind
            await Task.Delay(100);

            var payload = new byte[] { 0x01, 0x02, 0x03 };

            using (var sender = new UdpClient())
            {
                await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));
            }

            // Act
            var signalled = mre.Wait(1000);

            // Stop listening to ensure clean shutdown
            wrapper.StopListening();
            await Task.WhenAny(listeningTask, Task.Delay(1000));

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(signalled, Is.True, "Очікували, що подія MessageReceived спрацює.");
                Assert.That(received, Is.Not.Null, "Очікували отримати дані.");
                Assert.That(payload, Is.EqualTo(received), "Отримані байти мають збігатися з надісланими.");
            }
        }

        [Test]
        public async Task StopListening_ShouldStopLoop_AndCompleteTask()
        {
            // Arrange
            var port = GetRandomPort();
            using var wrapper = new UdpClientWrapper(port);

            var listeningTask = wrapper.StartListeningAsync();

            // Даємо трохи часу запустити цикл
            await Task.Delay(100);

            // Act
            wrapper.StopListening();

            var completed = await Task.WhenAny(listeningTask, Task.Delay(1000)) == listeningTask;

            // Assert
            Assert.That(completed, Is.True, "Після StopListening цикл в StartListeningAsync має завершитись.");
        }

        [Test]
        public void StopListening_CanBeCalledMultipleTimes_WithoutException()
        {
            // Arrange
            var port = GetRandomPort();
            using var wrapper = new UdpClientWrapper(port);

            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                wrapper.StopListening();
                wrapper.StopListening();
                wrapper.StopListening();
            });
        }

        [Test]
        public void Exit_CanBeCalledMultipleTimes_WithoutException()
        {
            // Arrange
            var port = GetRandomPort();
            var wrapper = new UdpClientWrapper(port);

            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                wrapper.Exit();
                wrapper.Exit();
            });

            wrapper.Dispose();
        }

        [Test]
        public void Dispose_ShouldBeSafe_ToCallMultipleTimes()
        {
            // Arrange
            var port = GetRandomPort();
            var wrapper = new UdpClientWrapper(port);

            // Act + Assert
            Assert.DoesNotThrow(() =>
            {
                wrapper.Dispose();
                wrapper.Dispose();
            });
        }

        [Test]
        public void Equals_ShouldReturnTrue_ForSameAddressAndPort()
        {
            // Arrange
            var port = GetRandomPort();
            var a = new UdpClientWrapper(port);
            var b = new UdpClientWrapper(port);

            using (Assert.EnterMultipleScope())
            {
                // Act + Assert
                Assert.That(a.Equals(b), Is.True);
                Assert.That(b.Equals(a), Is.True);
            }
        }

        [Test]
        public void Equals_ShouldReturnFalse_ForDifferentPorts()
        {
            // Arrange
            var a = new UdpClientWrapper(50000);
            var b = new UdpClientWrapper(50001);

            using (Assert.EnterMultipleScope())
            {
                // Act + Assert
                Assert.That(a.Equals(b), Is.False);
                Assert.That(b.Equals(a), Is.False);
            }
        }

        [Test]
        public void GetHashCode_ShouldBeConsistent_AndSameForEqualObjects()
        {
            // Arrange
            var port = GetRandomPort();
            var a = new UdpClientWrapper(port);
            var b = new UdpClientWrapper(port);

            // Act
            var hash1 = a.GetHashCode();
            var hash2 = a.GetHashCode(); // те саме, двічі
            var hashOther = b.GetHashCode(); // інший екземпляр, але той самий порт

            using (Assert.EnterMultipleScope())
            {
                // Assert
                Assert.That(hash2, Is.EqualTo(hash1), "GetHashCode має бути стабільним для одного об'єкта.");
                Assert.That(hashOther, Is.EqualTo(hash1), "Рівні об'єкти мають мати однаковий хеш.");
            }
        }
    }
}
