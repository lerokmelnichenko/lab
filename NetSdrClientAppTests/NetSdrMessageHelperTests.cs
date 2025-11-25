using NetSdrClientApp.Messages;

namespace NetSdrClientAppTests
{
    public class NetSdrMessageHelperTests
    {
        [Test]
        public void GetControlItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.Ack;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetControlItemMessage(type, code, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var codeBytes = msg.Skip(2).Take(2);
            var parametersBytes = msg.Skip(4);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);
            var actualCode = BitConverter.ToInt16(codeBytes.ToArray());

            using (Assert.EnterMultipleScope())
            {
                //Assert
                Assert.That(headerBytes.Count(), Is.EqualTo(2));
                Assert.That(msg.Length, Is.EqualTo(actualLength));
                Assert.That(type, Is.EqualTo(actualType));

                Assert.That(actualCode, Is.EqualTo((short)code));

                Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
            }
        }

        [Test]
        public void GetDataItemMessageTest()
        {
            //Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem2;
            int parametersLength = 7500;

            //Act
            byte[] msg = NetSdrMessageHelper.GetDataItemMessage(type, new byte[parametersLength]);

            var headerBytes = msg.Take(2);
            var parametersBytes = msg.Skip(2);

            var num = BitConverter.ToUInt16(headerBytes.ToArray());
            var actualType = (NetSdrMessageHelper.MsgTypes)(num >> 13);
            var actualLength = num - ((int)actualType << 13);

            using (Assert.EnterMultipleScope())
            {
                //Assert
                Assert.That(headerBytes.Count(), Is.EqualTo(2));
                Assert.That(msg.Length, Is.EqualTo(actualLength));
                Assert.That(type, Is.EqualTo(actualType));

                Assert.That(parametersBytes.Count(), Is.EqualTo(parametersLength));
            }
        }

        [Test]
        public void GetControlItemMessage_ShouldRoundTrip_WithTranslateMessage()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency;
            var parameters = new byte[] { 0x01, 0x02, 0x03 };

            // Act
            var message = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                message,
                out var parsedType,
                out var parsedCode,
                out var sequenceNumber,
                out var body);

            // Assert
            Assert.That(success, Is.True, "TranslateMessage should succeed for valid control message.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(parsedCode, Is.EqualTo(code));
                Assert.That(sequenceNumber, Is.Zero, "Sequence number for control item messages should stay 0.");
            }
            Assert.That(body, Is.EqualTo(parameters), "Body should match original parameters.");
        }

        [Test]
        public void GetDataItemMessage_ShouldRoundTrip_WithTranslateMessage()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            ushort expectedSequence = 0x1234;

            // Перші 2 байти в parameters — це sequence number,
            // TranslateMessage відріже їх в sequenceNumber.
            var sequenceBytes = BitConverter.GetBytes(expectedSequence);
            var payload = new byte[] { 0x10, 0x20, 0x30 };

            var parameters = sequenceBytes.Concat(payload).ToArray();

            // Act
            var message = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                message,
                out var parsedType,
                out var itemCode,
                out var sequenceNumber,
                out var body);

            // Assert
            Assert.That(success, Is.True, "TranslateMessage should succeed for valid data message.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(itemCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
                Assert.That(sequenceNumber, Is.EqualTo(expectedSequence));
            }
            Assert.That(body, Is.EqualTo(payload), "Body should match original parameters.");
        }

        [Test]
        public void TranslateMessage_ShouldReturnFalse_WhenControlItemCodeIsUnknown()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            ushort unknownCode = 0xFFFF; // немає в enum ControlItemCodes
            var codeBytes = BitConverter.GetBytes(unknownCode);
            var body = new byte[] { 0xAA, 0xBB };

            int lengthWithHeader = 2 /*header*/ + codeBytes.Length + body.Length;
            ushort headerValue = (ushort)(lengthWithHeader + ((int)type << 13));
            var headerBytes = BitConverter.GetBytes(headerValue);

            var message = headerBytes
                .Concat(codeBytes)
                .Concat(body)
                .ToArray();

            // Act
            var success = NetSdrMessageHelper.TranslateMessage(
                message,
                out var parsedType,
                out var parsedCode,
                out var sequenceNumber,
                out var parsedBody);

            // Assert
            Assert.That(success, Is.False, "TranslateMessage should fail for unknown control item code.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(parsedCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None),
                    "When code is unknown, itemCode should remain None.");
                Assert.That(sequenceNumber, Is.Zero);
            }
            Assert.That(body, Is.EqualTo(parsedBody));
        }

        [Test]
        public void TranslateMessage_ShouldReturnFalse_WhenBodyLengthDoesNotMatchHeader()
        {
            // Arrange
            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverState;
            var parameters = new byte[] { 0x01, 0x02, 0x03 };

            var fullMessage = NetSdrMessageHelper.GetControlItemMessage(type, code, parameters);

            // "ламаємо" повідомлення — обрізаємо останній байт
            var truncated = fullMessage.Take(fullMessage.Length - 1).ToArray();

            // Act
            var success = NetSdrMessageHelper.TranslateMessage(
                truncated,
                out var parsedType,
                out var parsedCode,
                out var _,
                out var _);

            // Assert
            Assert.That(success, Is.False, "TranslateMessage should fail if фактична довжина не збігається з довжиною в шапці.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(parsedCode, Is.EqualTo(code));
            }
        }

        [Test]
        public void GetDataItemMessage_MaxLength_ShouldUseSpecialHeaderEncoding_AndTranslateBack()
        {
            // Цей тест перевіряє edge-case:
            // DataItem з довжиною msg = 8192 байт -> lengthWithHeader = 8194,
            // що більше за _maxMessageLength (8191),
            // але для DataItem це легально, бо GetHeader кодує 0 в полі довжини.

            var type = NetSdrMessageHelper.MsgTypes.DataItem0;
            ushort seq = 0; // нехай 0

            // parameters = 2 байти sequence + 8190 байтів тіла = 8192
            var sequenceBytes = BitConverter.GetBytes(seq);
            var payload = new byte[8190];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i % 256);

            var parameters = sequenceBytes.Concat(payload).ToArray();

            // Act
            var message = NetSdrMessageHelper.GetDataItemMessage(type, parameters);

            var success = NetSdrMessageHelper.TranslateMessage(
                message,
                out var parsedType,
                out var itemCode,
                out var sequenceNumber,
                out var body);

            // Assert
            Assert.That(success, Is.True, "Max-length data item message should still be valid.");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(parsedType, Is.EqualTo(type));
                Assert.That(itemCode, Is.EqualTo(NetSdrMessageHelper.ControlItemCodes.None));
                Assert.That(sequenceNumber, Is.EqualTo(seq));
                Assert.That(body.Length, Is.EqualTo(8190), "Body length should be 8190 байт (8192 - 2 байти sequence).");
            }
            Assert.That(payload, Is.EqualTo(body));
        }

        [Test]
        public void GetControlItemMessage_ShouldThrowArgumentException_WhenMessageTooLong()
        {
            // Для звичайних (не DataItem) повідомлень:
            // lengthWithHeader = msgLength + 2 має бути <= 8191.
            // Візьмемо msgLength = 8190 => lengthWithHeader = 8192 > 8191 -> має бути виняток.

            var type = NetSdrMessageHelper.MsgTypes.SetControlItem;
            var code = NetSdrMessageHelper.ControlItemCodes.ReceiverFrequency;

            // msgLength = 2 (itemCode) + parameters.Length
            // Хочемо msgLength = 8190 => parameters.Length = 8188
            var parameters = new byte[8188];

            Assert.Throws<ArgumentException>(
                () => NetSdrMessageHelper.GetControlItemMessage(type, code, parameters),
                "Message length exceeds allowed value should throw ArgumentException.");
        }

        [Test]
        public void GetSamples_ShouldReturnCorrectValues_For8BitSamples()
        {
            // Arrange
            ushort sampleSizeBits = 8;
            var body = new byte[] { 1, 2, 3 };

            // Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList();

            // Assert
            Assert.That(new[] { 1, 2, 3 }, Is.EqualTo(samples));
        }

        [Test]
        public void GetSamples_ShouldReturnCorrectValues_For16BitSamples()
        {
            // Arrange
            ushort sampleSizeBits = 16;
            // Два 16-бітні значення: 0x0001, 0x0002
            var body = new byte[]
            {
                0x01, 0x00, // 1
                0x02, 0x00  // 2
            };

            // Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList();

            // Assert
            Assert.That(new[] { 1, 2 }, Is.EqualTo(samples));
        }

        [Test]
        public void GetSamples_ShouldReturnCorrectValues_For24BitSamples()
        {
            // Arrange
            ushort sampleSizeBits = 24;
            // Два 24-бітні значення: [1,0,0] і [2,0,0] -> 1 і 2
            var body = new byte[]
            {
                0x01, 0x00, 0x00,
                0x02, 0x00, 0x00
            };

            // Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList();

            // Assert
            Assert.That(new[] { 1, 2 }, Is.EqualTo(samples));
        }

        [Test]
        public void GetSamples_ShouldReturnCorrectValues_For32BitSamples()
        {
            // Arrange
            ushort sampleSizeBits = 32;
            var value1 = 123456;
            var value2 = -7890;

            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(value1));
            bytes.AddRange(BitConverter.GetBytes(value2));

            var body = bytes.ToArray();

            // Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList();

            // Assert
            Assert.That(new[] { value1, value2 }, Is.EqualTo(samples));
        }

        [Test]
        public void GetSamples_ShouldIgnoreIncompleteTail()
        {
            // Arrange
            ushort sampleSizeBits = 16;
            // 2 повних байти + "хвіст" 1 байт
            var body = new byte[]
            {
                0x01, 0x00, // повний sample = 1
                0xFF        // неповний хвіст, має ігноруватись
            };

            // Act
            var samples = NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList();

            // Assert
            Assert.That(new[] { 1 }, Is.EqualTo(samples), "Неповний блок в кінці має ігноруватися.");
        }

        [Test]
        public void GetSamples_ShouldThrow_WhenSampleSizeMoreThan32Bits()
        {
            // Arrange
            ushort sampleSizeBits = 40;
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NetSdrMessageHelper.GetSamples(sampleSizeBits, body).ToList());
        }
    }
}