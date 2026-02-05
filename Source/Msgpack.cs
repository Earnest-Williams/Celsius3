using System;
using System.IO;
using System.Text;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Celsius {
/*
* This implements a subset of MessagePack format
*
* https://github.com/msgpack/msgpack/blob/master/spec.md
*
* because MessagePack-CSharp pulls megabytes of dependencies.
*
*
*    Caution: this implementation completely ignores write errors.
*
*/
namespace Msgpack {
    public static class Limits {
        // converting about 800Mb of UTF8 to UTF16 in one go will probably kill the runtime
        public const uint MaxStringBufferSize = 0x2fffffff;
        // our ancient dotnet doesn't have this constant, so copypaste. also applies to raw buffers.
        public const  int MaxArrayLength = 0x7f3fffff;
        // for utf8 enc/dec stuff
        public const  int MaxStackBufferSize = 4096;
        // completely arbitrary 256M limit. hope clr is up to this
        public const  int MaxMapCount = 0x0fffffff;
    }

    public class MsgpackException : System.Exception
    {
        public MsgpackException() { }
        public MsgpackException(string msg) : base(msg) { }
        public MsgpackException(string msg, System.Exception inner) : base(msg, inner) { }
    }

    public class Reader {
        System.IO.Stream ms;
        public Reader(System.IO.Stream stream) { ms = stream; }

        int ReadTag() {
            int i = ms.ReadByte();
            if (i == -1) { throw new MsgpackException($"Short read by 1 byte"); }
            return i;
        }

        void ExpectTag(int tag) {
            int i = ms.ReadByte();
            if (i == -1) { throw new MsgpackException($"Short read by 1 byte"); }
            if (i != tag) { throw new MsgpackException($"Type mismatch: 0x{i:x2} != 0x{tag:x2}"); }
        }

        public bool ReadBool() {
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

        int StreamReadInt() {
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            return BinaryPrimitives.ReadInt32BigEndian(buf);
        }

        public int ReadInt() {
            ExpectTag(0xd2);
            return StreamReadInt();
        }

        uint StreamReadUInt() {
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            return BinaryPrimitives.ReadUInt32BigEndian(buf);
        }

        public uint ReadUInt() {
            ExpectTag(0xce);
            return StreamReadUInt();
        }

        ushort StreamReadUShort() {
            Span<byte> buf = stackalloc byte[2];
            int i = ms.Read(buf);
            if (i != 2) { throw new MsgpackException($"Short read by {2-i} byte(s)"); };
            return BinaryPrimitives.ReadUInt16BigEndian(buf);
        }

        public ushort ReadUShort() {
            ExpectTag(0xcd);
            return StreamReadUShort();
        }

        // looks like our target's too old t' have float reader/writer
        public float ReadSingle() {
            ExpectTag(0xca);
            Span<byte> buf = stackalloc byte[4];
            int i = ms.Read(buf);
            if (i != 4) { throw new MsgpackException($"Short read by {4-i} byte(s)"); };
            var t = BitConverter.ToInt32(buf);
            if (BitConverter.IsLittleEndian) { t = BinaryPrimitives.ReverseEndianness(t); }
            return BitConverter.Int32BitsToSingle(t);
        }

        public int ReadArrayHeader() {
            int tag = ReadTag();
            int length = -1;
            if ((tag & 0xf0) == 0x90) {
                length = tag & 0x0f;
            } else if (tag == 0xdc) {
                length = StreamReadUShort();
            } else if (tag == 0xdd) {
                uint mp_length = StreamReadUInt();
                if (mp_length > Limits.MaxArrayLength) {
                    throw new MsgpackException($"Array too long: 0x{length:x8} >  0x{Limits.MaxArrayLength:x8}");
                }
                length = Convert.ToInt32(mp_length);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            return length;
        }

        public float[] ReadFloatArray() {
            var length = ReadArrayHeader();
            var rv = new float[length];
            for (int i = 0; i < length; i++) {
                rv[i] = ReadSingle();
            }
            return rv;
        }

        public int[] ReadIntArray() {
            var length = ReadArrayHeader();
            var rv = new int[length];
            for (int i = 0; i < length; i++) {
                rv[i] = ReadInt();
            }
            return rv;
        }

        public string ReadUTF8() {
            int tag = ReadTag();
            int size = -1;
            if ((tag & 0xe0) == 0xa0) {
                size = tag & 0x1f;
            } else if (tag == 0xd9) {
                size = ReadTag(); // ms.ReadByte()+throw
            } else if (tag == 0xda) {
                size = StreamReadUShort();
            } else if (tag == 0xdb) {
                uint mp_size = StreamReadUInt();
                if (mp_size > Limits.MaxStringBufferSize) {
                    throw new MsgpackException($"String buffer too big: 0x{size:x8} > 0x{Limits.MaxStringBufferSize:x8}");
                }
                size = Convert.ToInt32(mp_size);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            if (size < Limits.MaxStackBufferSize) {
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

        int ReadMapHeader() {
            int tag = ReadTag();
            int count = -1;
            if ((tag & 0xf0) == 0x80) {
                count = tag & 0x0f;
            } else if (tag == 0xde) {
                count = StreamReadUShort();
            } else if (tag == 0xdf) {
                uint mp_count = StreamReadUInt();
                if (mp_count > Limits.MaxMapCount) {
                    throw new MsgpackException($"Map too big: 0x{mp_count:x8} > 0x{Limits.MaxMapCount:x8}");
                }
                count = Convert.ToInt32(mp_count);
            } else {
                throw new MsgpackException($"Type mismatch: got 0x{tag:x1}");
            }
            return count;
        }

        public  void ReadMapInto(Dictionary<int, string> dict) {
            int count = ReadMapHeader();
            dict.Clear();
            dict.EnsureCapacity(count);
            for (int i = 0 ; i < count ; i++) {
                var k = ReadInt();
                var v = ReadUTF8();
                dict.Add(k, v);
            }
        }

        public IEnumerable<KeyValuePair<int, string>>ReadMapInt2Str() {
            int count = ReadMapHeader();
            for (int i = 0 ; i < count; i++) {
                var key = ReadInt();
                var value = ReadUTF8();
                yield return new KeyValuePair<int, string>(key, value);
            }
            yield break;
        }
    }

    public class Writer {
        System.IO.Stream ms;
        public Writer(System.IO.Stream stream) { ms = stream; }

        public void WriteUTF8(string str) {
            byte[] utf8 = Encoding.UTF8.GetBytes(str);

            if (utf8.Length < 32) {
                ms.WriteByte(Convert.ToByte(0xa0 | utf8.Length));
            } else if (utf8.Length < 255) {
                ms.WriteByte(0xd9);
                ms.WriteByte(Convert.ToByte(utf8.Length));
            } else if (utf8.Length < 65535) {
                ms.WriteByte(0xda);
                StreamWriteUShort(Convert.ToUInt16(utf8.Length));
            } else {
                ms.WriteByte(0xdb);
                StreamWriteUInt(Convert.ToUInt32(utf8.Length));
            }
            ms.Write(utf8);
        }

        public void WriteBool(bool v) {
            if (!v) { ms.WriteByte(0xc2); } else { ms.WriteByte(0xc3); }
        }

        public void WriteInt(int v) {
            ms.WriteByte(0xd2);
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, v);
            ms.Write(buf);
        }

        void StreamWriteUInt(uint v) {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, v);
            ms.Write(buf);
        }

        public void WriteUInt(uint v) {
            ms.WriteByte(0xce);
            StreamWriteUInt(v);
        }

        void StreamWriteUShort(ushort v) {
            Span<byte> buf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, v);
            ms.Write(buf);
        }

        public void WriteUShort(ushort v) {
            ms.WriteByte(0xcd);
            StreamWriteUShort(v);
        }

        public void WriteSingle(float v) {
            ms.WriteByte(0xca);
            //Span<byte> buf = stackalloc byte[4];
            int t = BitConverter.SingleToInt32Bits(v);
            if (BitConverter.IsLittleEndian) { t = BinaryPrimitives.ReverseEndianness(t); }
            ms.Write(BitConverter.GetBytes(t));
        }

        void WriteArrayHeader(int length) {
            if (length < 16) {
                ms.WriteByte(Convert.ToByte(0x90 | length));
            } else if (length < 65535) {
                ms.WriteByte(0xdc);
                StreamWriteUShort(Convert.ToUInt16(length));
            } else { // msgpack spec allows more elements so it would certainly fit.
                ms.WriteByte(0xdd);
                StreamWriteUInt(Convert.ToUInt32(length));
            }
        }

        public void WriteFloatArray(float[] arr) {
            WriteArrayHeader(arr.Length);
            for (int i = 0; i < arr.Length; i++) {
                WriteSingle(arr[i]);
            }
        }

        public void WriteIntArray(int[] arr) {
            WriteArrayHeader(arr.Length);
            for (int i = 0; i < arr.Length; i++) {
                WriteInt(arr[i]);
            }
        }

        void WriteMapHeader(int count) {
            if (count < 16) {
                ms.WriteByte(Convert.ToByte(0x80 | count));
            } else if (count < 65535) {
                ms.WriteByte(0xde);
                StreamWriteUShort(Convert.ToUInt16(count));
            } else { // msgpack spec allows more elements so it would certainly fit.
                ms.WriteByte(0xdf);
                StreamWriteUInt(Convert.ToUInt32(count));
            }
        }

        public void WriteMapInt2Str(ICollection<KeyValuePair<int,string>> kvcol) {
            WriteMapHeader(kvcol.Count);
            foreach (var kv in kvcol) {
                WriteInt(kv.Key);
                WriteUTF8(kv.Value);
            }
        }

        public void WriteMapInt2Ts<T>(ICollection<KeyValuePair<int, T>> kvcol, Func<T, string> tostr) {
            WriteMapHeader(kvcol.Count);
            foreach (var kv in kvcol) {
                WriteInt(kv.Key);
                WriteUTF8(tostr(kv.Value));
            }
        }
    }
} // ns Msgpack
} // ns Celsius


