using System.Text;

namespace XStateNet.Semi.Secs
{
    /// <summary>
    /// SECS-II List item
    /// </summary>
    public class SecsList : SecsItem
    {
        private readonly List<SecsItem> _items;

        public override SecsFormat Format => SecsFormat.List;
        public override int Length => _items.Count;
        public IReadOnlyList<SecsItem> Items => _items;

        public SecsList(params SecsItem[] items)
        {
            _items = new List<SecsItem>(items);
        }

        public SecsList(IEnumerable<SecsItem> items)
        {
            _items = new List<SecsItem>(items);
        }

        public void Add(SecsItem item) => _items.Add(item);

        public SecsItem this[int index] => _items[index];

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, _items.Count);
            foreach (var item in _items)
            {
                item.Encode(writer);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var sb = new StringBuilder();
            var indentStr = new string(' ', indent * 2);
            sb.AppendLine($"{indentStr}<L[{_items.Count}]");
            foreach (var item in _items)
            {
                sb.Append(item.ToSml(indent + 1));
            }
            sb.AppendLine($"{indentStr}>");
            return sb.ToString();
        }
    }

    /// <summary>
    /// SECS-II ASCII string
    /// </summary>
    public class SecsAscii : SecsItem
    {
        public string Value { get; }

        public override SecsFormat Format => SecsFormat.ASCII;
        public override int Length => Encoding.ASCII.GetByteCount(Value);

        public SecsAscii(string value)
        {
            Value = value ?? string.Empty;
        }

        public override void Encode(BinaryWriter writer)
        {
            var bytes = Encoding.ASCII.GetBytes(Value);
            WriteHeader(writer, Format, bytes.Length);
            writer.Write(bytes);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<A[{Length}] \"{Value}\">\n";
        }

        public static implicit operator SecsAscii(string value) => new(value);
        public static implicit operator string(SecsAscii item) => item.Value;
    }

    /// <summary>
    /// SECS-II Binary data
    /// </summary>
    public class SecsBinary : SecsItem
    {
        public byte[] Value { get; }

        public override SecsFormat Format => SecsFormat.Binary;
        public override int Length => Value.Length;

        public SecsBinary(byte[] value)
        {
            Value = value ?? Array.Empty<byte>();
        }

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length);
            writer.Write(Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var hex = BitConverter.ToString(Value).Replace("-", " ");
            return $"{indentStr}<B[{Length}] {hex}>\n";
        }
    }

    /// <summary>
    /// SECS-II Boolean
    /// </summary>
    public class SecsBoolean : SecsItem
    {
        public bool[] Value { get; }

        public override SecsFormat Format => SecsFormat.Boolean;
        public override int Length => Value.Length;

        public SecsBoolean(params bool[] value)
        {
            Value = value ?? Array.Empty<bool>();
        }

        public SecsBoolean(byte[] bytes)
        {
            Value = bytes.Select(b => b != 0).ToArray();
        }

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length);
            foreach (var b in Value)
            {
                writer.Write((byte)(b ? 1 : 0));
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value.Select(b => b ? "T" : "F"));
            return $"{indentStr}<Boolean[{Length}] {values}>\n";
        }

        public static implicit operator SecsBoolean(bool value) => new(value);
    }

    // Numeric types - I1 (signed 1-byte integer)
    public class SecsI1 : SecsItem
    {
        public sbyte Value { get; }

        public override SecsFormat Format => SecsFormat.I1;
        public override int Length => 1;

        public SecsI1(sbyte value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 1);
            writer.Write((byte)Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<I1 {Value}>\n";
        }

        public static implicit operator SecsI1(sbyte value) => new(value);
        public static implicit operator sbyte(SecsI1 item) => item.Value;
    }

    public class SecsI1Array : SecsItem
    {
        public sbyte[] Value { get; }

        public override SecsFormat Format => SecsFormat.I1;
        public override int Length => Value.Length;

        public SecsI1Array(sbyte[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length);
            foreach (var v in Value)
                writer.Write((byte)v);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<I1[{Length}] {values}>\n";
        }
    }

    // I2 (signed 2-byte integer)
    public class SecsI2 : SecsItem
    {
        public short Value { get; }

        public override SecsFormat Format => SecsFormat.I2;
        public override int Length => 2;

        public SecsI2(short value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 2);
            writer.Write((byte)(Value >> 8));
            writer.Write((byte)Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<I2 {Value}>\n";
        }

        public static implicit operator SecsI2(short value) => new(value);
        public static implicit operator short(SecsI2 item) => item.Value;
    }

    public class SecsI2Array : SecsItem
    {
        public short[] Value { get; }

        public override SecsFormat Format => SecsFormat.I2;
        public override int Length => Value.Length * 2;

        public SecsI2Array(short[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 2);
            foreach (var v in Value)
            {
                writer.Write((byte)(v >> 8));
                writer.Write((byte)v);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<I2[{Value.Length}] {values}>\n";
        }
    }

    // I4 (signed 4-byte integer)
    public class SecsI4 : SecsItem
    {
        public int Value { get; }

        public override SecsFormat Format => SecsFormat.I4;
        public override int Length => 4;

        public SecsI4(int value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 4);
            writer.Write((byte)(Value >> 24));
            writer.Write((byte)(Value >> 16));
            writer.Write((byte)(Value >> 8));
            writer.Write((byte)Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<I4 {Value}>\n";
        }

        public static implicit operator SecsI4(int value) => new(value);
        public static implicit operator int(SecsI4 item) => item.Value;
    }

    public class SecsI4Array : SecsItem
    {
        public int[] Value { get; }

        public override SecsFormat Format => SecsFormat.I4;
        public override int Length => Value.Length * 4;

        public SecsI4Array(int[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 4);
            foreach (var v in Value)
            {
                writer.Write((byte)(v >> 24));
                writer.Write((byte)(v >> 16));
                writer.Write((byte)(v >> 8));
                writer.Write((byte)v);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<I4[{Value.Length}] {values}>\n";
        }
    }

    // I8 (signed 8-byte integer)
    public class SecsI8 : SecsItem
    {
        public long Value { get; }

        public override SecsFormat Format => SecsFormat.I8;
        public override int Length => 8;

        public SecsI8(long value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 8);
            for (int i = 7; i >= 0; i--)
                writer.Write((byte)(Value >> (i * 8)));
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<I8 {Value}>\n";
        }

        public static implicit operator SecsI8(long value) => new(value);
        public static implicit operator long(SecsI8 item) => item.Value;
    }

    public class SecsI8Array : SecsItem
    {
        public long[] Value { get; }

        public override SecsFormat Format => SecsFormat.I8;
        public override int Length => Value.Length * 8;

        public SecsI8Array(long[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 8);
            foreach (var v in Value)
            {
                for (int i = 7; i >= 0; i--)
                    writer.Write((byte)(v >> (i * 8)));
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<I8[{Value.Length}] {values}>\n";
        }
    }

    // Unsigned integer types
    public class SecsU1 : SecsItem
    {
        public byte Value { get; }

        public override SecsFormat Format => SecsFormat.U1;
        public override int Length => 1;

        public SecsU1(byte value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 1);
            writer.Write(Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<U1 {Value}>\n";
        }

        public static implicit operator SecsU1(byte value) => new(value);
        public static implicit operator byte(SecsU1 item) => item.Value;
    }

    public class SecsU1Array : SecsItem
    {
        public byte[] Value { get; }

        public override SecsFormat Format => SecsFormat.U1;
        public override int Length => Value.Length;

        public SecsU1Array(byte[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length);
            writer.Write(Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<U1[{Value.Length}] {values}>\n";
        }
    }

    public class SecsU2 : SecsItem
    {
        public ushort Value { get; }

        public override SecsFormat Format => SecsFormat.U2;
        public override int Length => 2;

        public SecsU2(ushort value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 2);
            writer.Write((byte)(Value >> 8));
            writer.Write((byte)Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<U2 {Value}>\n";
        }

        public static implicit operator SecsU2(ushort value) => new(value);
        public static implicit operator ushort(SecsU2 item) => item.Value;
    }

    public class SecsU2Array : SecsItem
    {
        public ushort[] Value { get; }

        public override SecsFormat Format => SecsFormat.U2;
        public override int Length => Value.Length * 2;

        public SecsU2Array(ushort[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 2);
            foreach (var v in Value)
            {
                writer.Write((byte)(v >> 8));
                writer.Write((byte)v);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<U2[{Value.Length}] {values}>\n";
        }
    }

    public class SecsU4 : SecsItem
    {
        public uint Value { get; }

        public override SecsFormat Format => SecsFormat.U4;
        public override int Length => 4;

        public SecsU4(uint value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 4);
            writer.Write((byte)(Value >> 24));
            writer.Write((byte)(Value >> 16));
            writer.Write((byte)(Value >> 8));
            writer.Write((byte)Value);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<U4 {Value}>\n";
        }

        public static implicit operator SecsU4(uint value) => new(value);
        public static implicit operator uint(SecsU4 item) => item.Value;
    }

    public class SecsU4Array : SecsItem
    {
        public uint[] Value { get; }
        public uint[] Values => Value; // Alias for compatibility

        public override SecsFormat Format => SecsFormat.U4;
        public override int Length => Value.Length * 4;

        public SecsU4Array(uint[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 4);
            foreach (var v in Value)
            {
                writer.Write((byte)(v >> 24));
                writer.Write((byte)(v >> 16));
                writer.Write((byte)(v >> 8));
                writer.Write((byte)v);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<U4[{Value.Length}] {values}>\n";
        }
    }

    public class SecsU8 : SecsItem
    {
        public ulong Value { get; }

        public override SecsFormat Format => SecsFormat.U8;
        public override int Length => 8;

        public SecsU8(ulong value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 8);
            for (int i = 7; i >= 0; i--)
                writer.Write((byte)(Value >> (i * 8)));
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<U8 {Value}>\n";
        }

        public static implicit operator SecsU8(ulong value) => new(value);
        public static implicit operator ulong(SecsU8 item) => item.Value;
    }

    public class SecsU8Array : SecsItem
    {
        public ulong[] Value { get; }

        public override SecsFormat Format => SecsFormat.U8;
        public override int Length => Value.Length * 8;

        public SecsU8Array(ulong[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 8);
            foreach (var v in Value)
            {
                for (int i = 7; i >= 0; i--)
                    writer.Write((byte)(v >> (i * 8)));
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<U8[{Value.Length}] {values}>\n";
        }
    }

    // Floating point types
    public class SecsF4 : SecsItem
    {
        public float Value { get; }

        public override SecsFormat Format => SecsFormat.F4;
        public override int Length => 4;

        public SecsF4(float value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 4);
            var bytes = BitConverter.GetBytes(Value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            writer.Write(bytes);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<F4 {Value}>\n";
        }

        public static implicit operator SecsF4(float value) => new(value);
        public static implicit operator float(SecsF4 item) => item.Value;
    }

    public class SecsF4Array : SecsItem
    {
        public float[] Value { get; }

        public override SecsFormat Format => SecsFormat.F4;
        public override int Length => Value.Length * 4;

        public SecsF4Array(float[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 4);
            foreach (var v in Value)
            {
                var bytes = BitConverter.GetBytes(v);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                writer.Write(bytes);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<F4[{Value.Length}] {values}>\n";
        }
    }

    public class SecsF8 : SecsItem
    {
        public double Value { get; }

        public override SecsFormat Format => SecsFormat.F8;
        public override int Length => 8;

        public SecsF8(double value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, 8);
            var bytes = BitConverter.GetBytes(Value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            writer.Write(bytes);
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            return $"{indentStr}<F8 {Value}>\n";
        }

        public static implicit operator SecsF8(double value) => new(value);
        public static implicit operator double(SecsF8 item) => item.Value;
    }

    public class SecsF8Array : SecsItem
    {
        public double[] Value { get; }

        public override SecsFormat Format => SecsFormat.F8;
        public override int Length => Value.Length * 8;

        public SecsF8Array(double[] value) => Value = value;

        public override void Encode(BinaryWriter writer)
        {
            WriteHeader(writer, Format, Value.Length * 8);
            foreach (var v in Value)
            {
                var bytes = BitConverter.GetBytes(v);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(bytes);
                writer.Write(bytes);
            }
        }

        public override string ToSml(int indent = 0)
        {
            var indentStr = new string(' ', indent * 2);
            var values = string.Join(" ", Value);
            return $"{indentStr}<F8[{Value.Length}] {values}>\n";
        }
    }
}