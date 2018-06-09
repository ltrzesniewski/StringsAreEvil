using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace StringsAreEvil
{
    internal unsafe class LineParserUnsafe
    {
        private readonly AsciiStreamReader _reader;
        private readonly List<ValueHolderAsStruct> _list = new List<ValueHolderAsStruct>();

        private const uint Mno = (byte)',' << 24 | (byte)'O' << 16 | (byte)'N' << 8 | (byte)'M';

        private LineParserUnsafe(AsciiStreamReader reader)
        {
            _reader = reader;
        }

        public static void Process()
        {
            using (var stream = File.OpenRead(@"..\..\example-input.csv"))
            using (var reader = new AsciiStreamReader(stream))
            {
                var parser = new LineParserUnsafe(reader);
                parser.Parse();
                //parser.Dump();
            }
        }

        private void Dump()
        {
            using (var writer = new StreamWriter("results-unsafe.csv"))
            {
                foreach (var line in _list)
                    writer.WriteLine(line);
            }
        }

        private void Parse()
        {
            while (_reader.ReadLine(out var line))
            {
                fixed (byte* pBuf = &line.Buffer[0])
                {
                    var ptr = pBuf + line.Offset;
                    var eol = ptr + line.Length;

                    if (*(uint*)ptr != Mno)
                        continue;

                    ptr += 4;

                    var elementId = ParseInt(ref ptr, eol);
                    var vehicleId = ParseInt(ref ptr, eol);
                    var term = ParseInt(ref ptr, eol);
                    var mileage = ParseInt(ref ptr, eol);
                    var value = ParseDecimal(ref ptr, eol);
                    var valueHolder = new ValueHolderAsStruct(elementId, vehicleId, term, mileage, value);
                    //_list.Add(valueHolder);
                }
            }
        }

        internal static int ParseInt(ref byte* ptr, byte* eol)
        {
            var multiplier = 1;
            var result = 0;

            if (ptr >= eol)
                ThrowFormatException();

            if (*ptr == '-')
            {
                ++ptr;
                multiplier = -1;
            }

            var digitStart = ptr;

            while (true)
            {
                if (ptr >= eol)
                    break;

                var c = *ptr++;

                if (c >= '0' && c <= '9')
                {
                    result = result * 10 + (c - '0');
                    continue;
                }

                if (c == ',' || c == '\r' || c == '\n')
                    break;

                ThrowFormatException();
            }

            if (ptr == digitStart || ptr - digitStart > 9) // 9 = int.MaxValue.ToString().Length - 1
                ThrowFormatException();

            return result * multiplier;
        }

        internal static decimal ParseDecimal(ref byte* ptr, byte* eol)
        {
            if (ptr >= eol)
                ThrowFormatException();

            var negative = false;

            if (ptr[0] == '-')
            {
                ++ptr;
                negative = true;
            }

            var mantissa = 0UL;
            var mantissaStart = ptr;

            while (true)
            {
                if (ptr >= eol)
                    break;

                var c = *ptr++;

                if (c >= '0' && c <= '9')
                {
                    mantissa = mantissa * 10 + (ulong)(c - '0');
                    continue;
                }

                if (c == '.')
                    goto fractionalPart;

                if (c == ',' || c == '\r' || c == '\n')
                    break;

                ThrowFormatException();
            }

            if (ptr == mantissaStart || ptr - mantissaStart > 18) // 18 = long.MaxValue.ToString().Length - 1
                ThrowFormatException();

            return negative ? -(decimal)mantissa : mantissa;

            fractionalPart:

            var fractionalPartStart = ptr;
            byte* lastNonZero = null;

            while (true)
            {
                if (ptr >= eol)
                    break;

                var c = *ptr;
                if (c >= '0' && c <= '9')
                {
                    if (c != '0')
                        lastNonZero = ptr;

                    ++ptr;
                    continue;
                }

                if (c == ',' || c == '\r' || c == '\n')
                {
                    ++ptr;
                    break;
                }

                ThrowFormatException();
            }

            if (lastNonZero == null)
                return negative ? -(decimal)mantissa : mantissa;

            if (lastNonZero - mantissaStart > 19) // 19 = long.MaxValue.ToString().Length - 1 + 1 for the dot
                ThrowFormatException();

            var scale = 1 + lastNonZero - fractionalPartStart;
            for (var p = fractionalPartStart; p <= lastNonZero; ++p)
                mantissa = mantissa * 10 + (ulong)(*p - '0');

            return new decimal((int)mantissa, (int)(mantissa >> 32), 0, negative, (byte)scale);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFormatException() => throw new FormatException("Invalid value");
    }

    public readonly struct AsciiStringView
    {
        public readonly byte[] Buffer;
        public readonly int Offset;
        public readonly int Length;

        public AsciiStringView(byte[] buffer, int offset, int length)
        {
            Buffer = buffer;
            Offset = offset;
            Length = length;
        }

        public override string ToString() => Buffer != null ? Encoding.ASCII.GetString(Buffer, Offset, Length) : string.Empty;
    }

    internal unsafe class AsciiStreamReader : IDisposable
    {
        private readonly Stream _stream;
        private byte[] _buffer = new byte[4096];
        private int _nextLinePos;
        private int _bufFillPos;
        private bool _end;

        public AsciiStreamReader(Stream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            _end = true;
            _nextLinePos = 0;
            _bufFillPos = 0;

            _stream.Dispose();
        }

        public bool ReadLine(out AsciiStringView result)
        {
            var offset = _nextLinePos;
            var idx = offset;

            for (; idx < _bufFillPos; ++idx)
            {
                if (_buffer[idx] != '\n')
                    continue;

                goto success;
            }

            if (_end)
                goto end;

            ShiftBuffer();
            idx -= offset;
            offset = 0;

            while (true)
            {
                if (idx == _bufFillPos && !FillBuffer())
                {
                    _end = true;

                    if (offset == idx)
                        goto end;

                    goto success;
                }

                if (_buffer[idx] != '\n')
                {
                    ++idx;
                    continue;
                }

                goto success;
            }

            success:
            _nextLinePos = idx + 1;
            var length = idx - offset;
            if (length > 0 && _buffer[idx - 1] == '\r')
                --length;

            result = new AsciiStringView(_buffer, offset, length);
            return true;

            end:
            result = default;
            return false;
        }

        private void ShiftBuffer()
        {
            if (_bufFillPos > _nextLinePos)
            {
                fixed (byte* buf = &_buffer[0])
                {
                    Buffer.MemoryCopy(buf + _nextLinePos, buf, _buffer.Length, _bufFillPos - _nextLinePos);
                }
            }

            _bufFillPos -= _nextLinePos;
            _nextLinePos = 0;
        }

        private bool FillBuffer()
        {
            if (_bufFillPos == _buffer.Length)
            {
                var newBuf = new byte[_buffer.Length * 2];

                fixed (byte* src = &_buffer[0])
                fixed (byte* dst = &newBuf[0])
                {
                    Buffer.MemoryCopy(src, dst, newBuf.Length, _buffer.Length);
                }

                _buffer = newBuf;
            }

            var read = _stream.Read(_buffer, _bufFillPos, _buffer.Length - _bufFillPos);
            if (read == 0)
                return false;

            _bufFillPos += read;
            return true;
        }
    }
}