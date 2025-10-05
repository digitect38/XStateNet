using System.Text;

namespace XStateNet.Semi.Secs
{
    /// <summary>
    /// SEMI E5 SECS-II message structure
    /// Represents a complete SECS message with stream, function, and data items
    /// </summary>
    public class SecsMessage
    {
        public byte Stream { get; set; }
        public byte Function { get; set; }
        public bool ReplyExpected { get; set; } = true;
        public SecsItem? Data { get; set; }
        public uint SystemBytes { get; set; }

        /// <summary>
        /// Common message identifier (e.g., "S1F1", "S2F41")
        /// </summary>
        public string SxFy => $"S{Stream}F{Function}";

        public SecsMessage(byte stream, byte function, bool replyExpected = true)
        {
            Stream = stream;
            Function = function;
            ReplyExpected = replyExpected;
        }

        /// <summary>
        /// Encode the message to SECS-II binary format
        /// </summary>
        public byte[] Encode()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            if (Data != null)
            {
                Data.Encode(writer);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Decode a message from SECS-II binary format
        /// </summary>
        public static SecsMessage Decode(byte stream, byte function, byte[] data, bool replyExpected = true)
        {
            var message = new SecsMessage(stream, function, replyExpected);

            if (data != null && data.Length > 0)
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);
                message.Data = SecsItem.Decode(reader);
            }

            return message;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{SxFy} {(ReplyExpected ? "W" : "")}");
            if (Data != null)
            {
                sb.Append(Data.ToSml());
            }
            else
            {
                sb.AppendLine(".");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Base class for SECS-II data items
    /// </summary>
    public abstract class SecsItem
    {
        public abstract SecsFormat Format { get; }
        public abstract int Length { get; }

        /// <summary>
        /// Encode this item to binary format
        /// </summary>
        public abstract void Encode(BinaryWriter writer);

        /// <summary>
        /// Convert to SML (SECS Message Language) format
        /// </summary>
        public abstract string ToSml(int indent = 0);

        /// <summary>
        /// Decode a SECS item from binary stream
        /// </summary>
        public static SecsItem Decode(BinaryReader reader)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                throw new EndOfStreamException("Unexpected end of SECS data");

            var formatByte = reader.ReadByte();
            var format = (SecsFormat)(formatByte & 0xFC);
            var lengthBytes = formatByte & 0x03;

            int length = 0;
            switch (lengthBytes)
            {
                case 1:
                    length = reader.ReadByte();
                    break;
                case 2:
                    length = (reader.ReadByte() << 8) | reader.ReadByte();
                    break;
                case 3:
                    length = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
                    break;
            }

            return format switch
            {
                SecsFormat.List => DecodeList(reader, length),
                SecsFormat.Binary => new SecsBinary(reader.ReadBytes(length)),
                SecsFormat.Boolean => new SecsBoolean(reader.ReadBytes(length)),
                SecsFormat.ASCII => new SecsAscii(Encoding.ASCII.GetString(reader.ReadBytes(length))),
                SecsFormat.I1 => DecodeI1(reader, length),
                SecsFormat.I2 => DecodeI2(reader, length),
                SecsFormat.I4 => DecodeI4(reader, length),
                SecsFormat.I8 => DecodeI8(reader, length),
                SecsFormat.U1 => DecodeU1(reader, length),
                SecsFormat.U2 => DecodeU2(reader, length),
                SecsFormat.U4 => DecodeU4(reader, length),
                SecsFormat.U8 => DecodeU8(reader, length),
                SecsFormat.F4 => DecodeF4(reader, length),
                SecsFormat.F8 => DecodeF8(reader, length),
                _ => throw new NotSupportedException($"Unsupported SECS format: {format}")
            };
        }

        private static SecsList DecodeList(BinaryReader reader, int count)
        {
            var items = new List<SecsItem>();
            for (int i = 0; i < count; i++)
            {
                items.Add(Decode(reader));
            }
            return new SecsList(items);
        }

        private static SecsItem DecodeI1(BinaryReader reader, int length)
        {
            if (length == 1)
                return new SecsI1((sbyte)reader.ReadByte());

            var array = new sbyte[length];
            for (int i = 0; i < length; i++)
                array[i] = (sbyte)reader.ReadByte();
            return new SecsI1Array(array);
        }

        private static SecsItem DecodeI2(BinaryReader reader, int length)
        {
            var count = length / 2;
            if (count == 1)
                return new SecsI2((short)((reader.ReadByte() << 8) | reader.ReadByte()));

            var array = new short[count];
            for (int i = 0; i < count; i++)
                array[i] = (short)((reader.ReadByte() << 8) | reader.ReadByte());
            return new SecsI2Array(array);
        }

        private static SecsItem DecodeI4(BinaryReader reader, int length)
        {
            var count = length / 4;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(4);
                return new SecsI4((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            }

            var array = new int[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(4);
                array[i] = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
            }
            return new SecsI4Array(array);
        }

        private static SecsItem DecodeI8(BinaryReader reader, int length)
        {
            var count = length / 8;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(8);
                long value = 0;
                for (int i = 0; i < 8; i++)
                    value = (value << 8) | bytes[i];
                return new SecsI8(value);
            }

            var array = new long[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(8);
                long value = 0;
                for (int j = 0; j < 8; j++)
                    value = (value << 8) | bytes[j];
                array[i] = value;
            }
            return new SecsI8Array(array);
        }

        private static SecsItem DecodeU1(BinaryReader reader, int length)
        {
            if (length == 1)
                return new SecsU1(reader.ReadByte());

            return new SecsU1Array(reader.ReadBytes(length));
        }

        private static SecsItem DecodeU2(BinaryReader reader, int length)
        {
            var count = length / 2;
            if (count == 1)
                return new SecsU2((ushort)((reader.ReadByte() << 8) | reader.ReadByte()));

            var array = new ushort[count];
            for (int i = 0; i < count; i++)
                array[i] = (ushort)((reader.ReadByte() << 8) | reader.ReadByte());
            return new SecsU2Array(array);
        }

        private static SecsItem DecodeU4(BinaryReader reader, int length)
        {
            var count = length / 4;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(4);
                return new SecsU4((uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]));
            }

            var array = new uint[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(4);
                array[i] = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            }
            return new SecsU4Array(array);
        }

        private static SecsItem DecodeU8(BinaryReader reader, int length)
        {
            var count = length / 8;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(8);
                ulong value = 0;
                for (int i = 0; i < 8; i++)
                    value = (value << 8) | bytes[i];
                return new SecsU8(value);
            }

            var array = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(8);
                ulong value = 0;
                for (int j = 0; j < 8; j++)
                    value = (value << 8) | bytes[j];
                array[i] = value;
            }
            return new SecsU8Array(array);
        }

        private static SecsItem DecodeF4(BinaryReader reader, int length)
        {
            var count = length / 4;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(4);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                return new SecsF4(BitConverter.ToSingle(bytes, 0));
            }

            var array = new float[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(4);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                array[i] = BitConverter.ToSingle(bytes, 0);
            }
            return new SecsF4Array(array);
        }

        private static SecsItem DecodeF8(BinaryReader reader, int length)
        {
            var count = length / 8;
            if (count == 1)
            {
                var bytes = reader.ReadBytes(8);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                return new SecsF8(BitConverter.ToDouble(bytes, 0));
            }

            var array = new double[count];
            for (int i = 0; i < count; i++)
            {
                var bytes = reader.ReadBytes(8);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                array[i] = BitConverter.ToDouble(bytes, 0);
            }
            return new SecsF8Array(array);
        }

        protected void WriteHeader(BinaryWriter writer, SecsFormat format, int length)
        {
            byte formatByte = (byte)format;

            if (length <= 255)
            {
                formatByte |= 1;
                writer.Write(formatByte);
                writer.Write((byte)length);
            }
            else if (length <= 65535)
            {
                formatByte |= 2;
                writer.Write(formatByte);
                writer.Write((byte)(length >> 8));
                writer.Write((byte)length);
            }
            else
            {
                formatByte |= 3;
                writer.Write(formatByte);
                writer.Write((byte)(length >> 16));
                writer.Write((byte)(length >> 8));
                writer.Write((byte)length);
            }
        }
    }

    /// <summary>
    /// SECS-II data formats
    /// </summary>
    public enum SecsFormat : byte
    {
        List = 0x00,
        Binary = 0x20,
        Boolean = 0x24,
        ASCII = 0x40,
        JIS8 = 0x44,
        I8 = 0x60,
        I1 = 0x64,
        I2 = 0x68,
        I4 = 0x70,
        F8 = 0x80,
        F4 = 0x90,
        U8 = 0xA0,
        U1 = 0xA4,
        U2 = 0xA8,
        U4 = 0xB0
    }
}