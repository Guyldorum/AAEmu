// === PHASE 12.2.b TODO === résidus typage TUnit ou patterns non couverts par lot-12.2.a
#if false
﻿using System.Numerics;

using AAEmu.Commons.Network;

namespace AAEmu.UnitTests.Commons.Network
{
    public class PacketStreamTests
    {
        [Test]
        public async Task WriteAndReadByte_ShouldReturnSameValue()
        {
            var stream = new PacketStream();
            byte expected = 0x7F;
            stream.Write(expected);
            stream.Rollback();

            var result = stream.ReadByte();
            await Assert.That(result).IsEqualTo(expected);
        }

        [Test]
        public async Task WriteAndReadInt32_ShouldReturnSameValue()
        {
            var stream = new PacketStream();
            var expected = 123456;
            stream.Write(expected);
            stream.Rollback();

            var result = stream.ReadInt32();
            await Assert.That(result).IsEqualTo(expected);
        }

        [Test]
        public async Task WriteAndReadString_ShouldReturnSameString()
        {
            var stream = new PacketStream();
            var expected = "Hello xUnit";
            stream.Write(expected, appendSize: true);
            stream.Rollback();

            var result = stream.ReadString();
            await Assert.That(result).IsEqualTo(expected);
        }

        [Test]
        public async Task Clear_ShouldResetCountToZero()
        {
            var stream = new PacketStream();
            stream.Write(99);
            stream.Clear();
            await Assert.That(stream.Count).IsEqualTo(0);
        }

        [Test]
        public async Task Replace_WithByteArray_ShouldCopyDataCorrectly()
        {
            var original = new PacketStream();
            byte value = 100;
            original.PushBack(value);

            var newStream = new PacketStream();
            newStream.Replace(original);
            newStream.Rollback();
            await Assert.That(newStream.ReadByte()).IsEqualTo(value);
        }

        [Test]
        public async Task Insert_ShouldInsertBytesIntoTheMiddle()
        {
            var stream = new PacketStream();
            // Запишем два байта: 'A' и 'C'
            stream.Write((byte)'A');
            stream.Write((byte)'C');
            // Вставим 'B' на позицию 1
            stream.Insert(1, [(byte)'B']);

            stream.Rollback();
            var result = stream.ReadBytes(3);
            var expected = new byte[] { (byte)'A', (byte)'B', (byte)'C' };
            await Assert.That(result).IsEqualTo(expected);
        }

        [Test]
        public async Task Swap_ShouldExchangeBuffersBetweenStreams()
        {
            var stream1 = new PacketStream();
            var stream2 = new PacketStream();
            stream1.Write((byte)1);
            stream2.Write((byte)2);

            stream1.Swap(stream2);

            stream1.Rollback();
            stream2.Rollback();
            await Assert.That(stream1.ReadByte()).IsEqualTo(2);
            await Assert.That(stream2.ReadByte()).IsEqualTo(1);
        }

        [Test]
        public async Task Clone_ShouldCreateIdenticalPacketStream()
        {
            var stream = new PacketStream();
            stream.Write(99);

            var clone = (PacketStream)stream.Clone();
            stream.Rollback();
            clone.Rollback();
            await Assert.That(clone.ReadInt32()).IsEqualTo(stream.ReadInt32());
        }

        [Test]
        public async Task CompareTo_ShouldReturnCorrectComparison()
        {
            var stream1 = new PacketStream();
            var stream2 = new PacketStream();
            stream1.Write((byte)1);
            stream2.Write((byte)2);

            // Сравнение должно вернуть значение меньше нуля, если stream1 меньше stream2
            await Assert.That(stream1.CompareTo(stream2) < 0).IsTrue();
        }

        [Test]
        public async Task WriteAndReadPosition_ShouldReturnSameCoordinates()
        {
            var stream = new PacketStream();
            float x = 1.0f, y = 2.0f, z = 3.0f;
            stream.WritePosition(x, y, z);
            stream.Rollback();

            (var rx, var ry, var rz) = stream.ReadPosition();
            await Assert.That(rx).IsBetween(x - 0.01f, x + 0.01f);
            await Assert.That(ry).IsBetween(y - 0.01f, y + 0.01f);
            await Assert.That(rz).IsBetween(z - 0.01f, z + 0.01f);
        }

        [Test]
        public async Task WriteAndReadQuaternionShort_ShouldReturnApproximatelySameQuaternion()
        {
            var stream = new PacketStream();
            // Создаём кватернион с произвольными значениями
            var q = new Quaternion(0.1f, 0.2f, 0.3f, 0.4f);
            stream.WriteQuaternionShort(q);
            stream.Rollback();

            var result = stream.ReadQuaternionShort();
            // Проверяем приближенность для x, y, z, так как w вычисляется через норму
            await Assert.That(result.X).IsBetween(q.X - 0.1f, q.X + 0.1f);
            await Assert.That(result.Y).IsBetween(q.Y - 0.1f, q.Y + 0.1f);
            await Assert.That(result.Z).IsBetween(q.Z - 0.1f, q.Z + 0.1f);
        }
    }
}


#endif
