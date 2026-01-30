using System;
using System.Collections.Generic;
using System.IO;
using System.Buffers.Binary;
using System.Text;

using Verse;

namespace Celsius
{
    /*
     * This implements a subset of MessagePack format
     *
     * https://github.com/msgpack/msgpack/blob/master/spec.md
     *
     * because MessagePack-CSharp pulls megabytes of dependencies.
     *
     *
     * Caution: this implementation completely ignores write errors.
     *
     */

    public class MsgpackException : Exception
    {
        public MsgpackException() { }
        public MsgpackException(string msg) : base(msg) { }
        public MsgpackException(string msg, Exception inner) : base(msg, inner) { }
    }

    public static class Msgpack
    {
        // converting about 800Mb of UTF8 to UTF16 in one go will probably kill the runtime
        public const uint MaxStringBufferSize = 0x2fffffff;
        // our ancient dotnet doesn't have this constant, so copypaste
        public const int MaxArrayLength = 0x7f3fffff;
        // for utf8 enc/dec stuff
        public const int MaxStackBufferSize = 4096;
        // completely arbitrary 256M limit. hope clr is up to this
        public const int MaxMapPairCount = 0x0fffffff;

        static int ReadTag(this MemoryStream ms) {
            int i = ms.ReadByte();
            if (i == -1) { throw new MsgpackException($"Short read by 1 byte"); }
            return i;
        }

        static void ExpectTag(this MemoryStream ms, int tag) {
            int i = ms.ReadByte();
            if (i == -1) { throw new MsgpackException($"Short read by 1 byte"); }
            if (i != tag) { throw new MsgpackException($"Type mismatch: 0x{i:x2} != 0x{tag:x2}"); }
        }

        public static bool ReadBool(this MemoryStream ms) {
            int i = ms.ReadByte();
            switch(i) {
                case 0xc2:
                    return false;
                case 0xc3:
                    return true;
                case -1:
                    throw new MsgpackException($"Short read by 1 byte");
                default:
                    throw new MsgpackException($"Type mismatch: got 0x{i:x2}");
            }
        }

        public static void WriteBool(this MemoryStream ms, bool v) {
            if (!v) { ms.WriteByte(0xc2); } else { ms.WriteByte(0xc3); }
        }

        public static int ReadInt(this MemoryStream ms) {
            ms.ExpectTag(0xd2);
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            return BinaryPrimitives.ReadInt32BigEndian(buf);
        }

        public static void WriteInt(this MemoryStream ms, int v) {
            ms.WriteByte(0xd2);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, v);
            ms.Write(buf);
        }

        public static uint ReadUInt(this MemoryStream ms) {
            ms.ExpectTag(0xce);
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            return BinaryPrimitives.ReadUInt32BigEndian(buf);
        }

        public static void WriteUInt(this MemoryStream ms, uint v) {
            ms.WriteByte(0xce);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, v);
            ms.Write(buf);
        }

        public static ushort ReadUShort(this MemoryStream ms) {
            ms.ExpectTag(0xcd);
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            return BinaryPrimitives.ReadUInt16BigEndian(buf);
        }

        public static void WriteUShort(this MemoryStream ms, ushort v) {
            ms.WriteByte(0xcd);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt16BigEndian(buf, v);
            ms.Write(buf);
        }

        // looks like our target's too old t' have float reader/writer
        public static float ReadSingle(this MemoryStream ms) {
            ms.ExpectTag(0xca);
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            var t = BitConverter.ToInt32(buf);
            if (BitConverter.IsLittleEndian) { t = BinaryPrimitives.ReverseEndianness(t); }
            return BitConverter.Int32BitsToSingle(t);
        }

        public static void WriteSingle(this MemoryStream ms, float v) {
            ms.WriteByte(0xca);
            //Span<byte> buf = stackalloc byte[4];
            int t = BitConverter.SingleToInt32Bits(v);
            if (BitConverter.IsLittleEndian) { t = BinaryPrimitives.ReverseEndianness(t); }
            ms.Write(BitConverter.GetBytes(t));
        }

        public static int ReadArrayHeader(this MemoryStream ms) {
            int tag = ms.ReadTag();
            int length = -1;
            if ((tag & 0xf0) == 0x90) {
                length = tag & 0x0f;
            } else if (tag == 0xdc) {
                length = ms.ReadUShort();
            } else if (tag == 0xdd) {
                uint mp_length = ms.ReadUInt();
                if (mp_length > MaxArrayLength) {
                    throw new MsgpackException($"Array too long: 0x{length:x8} >  0x{MaxArrayLength:x8}");
                }
                length = Convert.ToInt32(mp_length);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            return length;
        }

        public static float[] ReadFloatArray(this MemoryStream ms) {
            var length = ms.ReadArrayHeader();
            var rv = new float[length];
            for (int i = 0; i < length; i++) {
                rv[i] = ms.ReadSingle();
            }
            return rv;
        }

        public static void WriteArrayHeader(this MemoryStream ms, int length) {
            if (length < 16) {
                ms.WriteByte(Convert.ToByte(0x90 | length));
            } else if (length < 65535) {
                ms.WriteByte(0xdc);
                ms.WriteUShort(Convert.ToUInt16(length));
            } else { // msgpack spec allows more elements so it would certainly fit.
                ms.WriteByte(0xdd);
                ms.WriteUInt(Convert.ToUInt32(length));
            }
        }

        public static void WriteFloatArray(this MemoryStream ms, float[] arr) {
            ms.WriteArrayHeader(arr.Length);
            for (int i = 0; i < arr.Length; i++) {
                ms.WriteSingle(arr[i]);
            }
        }

        public static int[] ReadIntArray(this MemoryStream ms) {
            var length = ms.ReadArrayHeader();
            var rv = new int[length];
            for (int i = 0; i < length; i++) {
                rv[i] = ms.ReadInt();
            }
            return rv;
        }

        public static void WriteIntArray(this MemoryStream ms, int[] arr) {
            ms.WriteArrayHeader(arr.Length);
            for (int i = 0; i < arr.Length; i++) {
                ms.WriteInt(arr[i]);
            }
        }

        // ehh. now let's struggle with encodings.
        public static string ReadUTF8(this MemoryStream ms) {
            int tag = ms.ReadTag();
            int size = -1;
            if ((tag & 0xe0) == 0xa0) {
                size = tag & 0x1f;
            } else if (tag == 0xd9) {
                size = ms.ReadTag(); // ReadByte()+throw
            } else if (tag == 0xda) {
                size = ms.ReadUShort();
            } else if (tag == 0xdb) {
                uint mp_size = ms.ReadUInt();
                if (mp_size > MaxStringBufferSize) {
                    throw new MsgpackException($"String buffer too big: 0x{size:x8} > 0x{MaxStringBufferSize:x8}");
                }
                size = Convert.ToInt32(mp_size);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            if (size < MaxStackBufferSize) {
                Span<byte> buf = stackalloc byte[size];
                int i = ms.Read(buf);
                if (i != size) { throw new MsgpackException($"Short read by {size-i} byte(s)"); };
                return Encoding.UTF8.GetString(buf);
            } else {
                var buf = new byte[size];
                int i = ms.Read(buf);
                if (i != size) { throw new MsgpackException($"Short read by {size-i} byte(s)"); };
                return Encoding.UTF8.GetString(buf);
            }
        }

        public static void WriteUTF8(this MemoryStream ms, string str) {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);

            if (utf8.Length < 32) {
                ms.WriteByte(Convert.ToByte(0xa0 | utf8.Length));
            } else if (utf8.Length < 255) {
                ms.WriteByte(0xd9);
                ms.WriteByte(Convert.ToByte(utf8.Length));
            } else if (utf8.Length < 65535) {
                ms.WriteByte(0xda);
                ms.WriteUShort(Convert.ToUInt16(utf8.Length));
            } else {
                ms.WriteByte(0xdb);
                ms.WriteUInt(Convert.ToUInt32(utf8.Length));
            }
            ms.Write(utf8);
        }

        // returns count of kv pairs
        static int ReadMapHeader(this MemoryStream ms) {
            int tag = ms.ReadTag();
            int count = -1;
            if ((tag & 0xf0) == 0x80) {
                count = tag & 0x0f;
            } else if (tag == 0xde) {
                count = ms.ReadUShort();
            } else if (tag == 0xdf) {
                uint mp_count = ms.ReadUInt();
                if (mp_count > MaxMapPairCount) {
                    throw new MsgpackException($"Map too big: 0x{mp_count:x8} > 0x{MaxMapPairCount:x8}");
                }
                count = Convert.ToInt32(mp_count);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            return count;
        }

        static void WriteMapHeader(this MemoryStream ms, int count) {
            if (count < 16) {
                ms.WriteByte(Convert.ToByte(0x80 | count));
            } else if (count < 65535) {
                ms.WriteByte(0xde);
                ms.WriteUShort(Convert.ToUInt16(count));
            } else { // msgpack spec allows more elements so it would certainly fit.
                ms.WriteByte(0xdf);
                ms.WriteUInt(Convert.ToUInt32(count));
            }
        }

        public static void ReadMap(this MemoryStream ms, Dictionary<int, string> dict) {
            int count = ms.ReadMapHeader();
            dict.Clear();
            dict.EnsureCapacity(count);
            for (int i = 0 ; i < count ; i++) {
                var k = ms.ReadInt();
                var v = ms.ReadUTF8();
                dict.Add(k, v);
            }
        }

        public static void WriteMapInt2Str(this MemoryStream ms, Dictionary<int, string> dict) {
            ms.WriteMapHeader(dict.Count);
            foreach ( var kv in dict ) {
                ms.WriteInt(kv.Key);
                ms.WriteUTF8(kv.Value);
            }
        }

        public static IEnumerable<KeyValuePair<int, string>>ReadMapInt2Str(this MemoryStream ms) {
            int count = ms.ReadMapHeader();
            for (int i = 0 ; i < count; i++) {
                var key = ms.ReadInt();
                var value = ms.ReadUTF8();
                yield return new KeyValuePair<int, string>(key, value);
            }
            yield break;
        }

        public static void WriteMapInt2Str(this MemoryStream ms, Dictionary<int, TerrainDef> dict) {
            ms.WriteMapHeader(dict.Count);
            foreach (var kv in dict) {
                ms.WriteInt(kv.Key);
                ms.WriteUTF8(kv.Value.ToString());
            }
        }
    }

    public static class DataUtility
    {
        public static string ArrayToString(float[] array)
        {
            if (array == null)
                return null;
            List<byte> bytes = new List<byte>(array.Length * sizeof(float));
            for (int i = 0; i < array.Length; i++)
                bytes.AddRange(BitConverter.GetBytes(array[i]));
            return Convert.ToBase64String(bytes.ToArray());
        }

        public static float[] StringToArray(string str)
        {
            byte[] bytes = Convert.FromBase64String(str);
            float[] array = new float[bytes.Length / sizeof(float)];
            for (int i = 0; i < array.Length; i++)
                array[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
            return array;
        }

        public static float[] Transpose(float[] array, int width)
        {
            float[] result = new float[array.Length];
            int height = array.Length / width;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    result[x * height + y] = array[y * width + x];
            return result;
        }
    }
}
