using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;

namespace StringsAreEvil
{
    internal unsafe class LineParserUnsafeMemMap
    {
        private readonly List<ValueHolderAsStruct> _list = new List<ValueHolderAsStruct>();
        private MemoryMappedCsvReader _reader;

        private const uint Mno = (byte)',' << 24 | (byte)'O' << 16 | (byte)'N' << 8 | (byte)'M';

        private LineParserUnsafeMemMap(MemoryMappedViewAccessor accessor)
        {
            _reader = new MemoryMappedCsvReader(accessor);
        }

        public static void Process()
        {
            using (var mmf = MemoryMappedFile.CreateFromFile(@"..\..\example-input.csv", FileMode.Open))
            using (var accessor = mmf.CreateViewAccessor())
            {
                var parser = new LineParserUnsafeMemMap(accessor);
                parser.Parse();
                //parser.Dump();
            }
        }

        private void Dump()
        {
            using (var writer = new StreamWriter("results-unsafe-mm.csv"))
            {
                foreach (var line in _list)
                    writer.WriteLine(line);
            }
        }

        private void Parse()
        {
            while (_reader.ReadLine(out var line))
            {
                var ptr = line.Start;
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

        private static int ParseInt(ref byte* ptr, byte* eol)
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

        private static decimal ParseDecimal(ref byte* ptr, byte* eol)
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

            var scale = 1 + lastNonZero - fractionalPartStart;
            for (var p = fractionalPartStart; p <= lastNonZero; ++p)
                mantissa = mantissa * 10 + (ulong)(*p - '0');

            return new decimal((int)mantissa, (int)(mantissa >> 32), 0, negative, (byte)scale);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowFormatException() => throw new FormatException("Invalid value");
    }

    public readonly unsafe struct UnsafeAsciiStringView
    {
        public readonly byte* Start;
        public readonly int Length;

        public UnsafeAsciiStringView(byte* start, int length)
        {
            Start = start;
            Length = length;
        }

        public override string ToString() => Start != null ? Encoding.ASCII.GetString(Start, Length) : string.Empty;
    }

    internal unsafe struct MemoryMappedCsvReader
    {
        private byte* _ptr;
        private readonly byte* _endPtr;

        public MemoryMappedCsvReader(MemoryMappedViewAccessor accessor)
        {
            _ptr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _ptr);

            if (_ptr == null)
                throw new InvalidOperationException();

            _endPtr = _ptr + accessor.Capacity;
        }

        public bool ReadLine(out UnsafeAsciiStringView result)
        {
            var start = _ptr;

            while (_ptr < _endPtr)
            {
                if (*_ptr++ == '\n')
                    break;
            }

            if (_ptr == start)
            {
                result = default;
                return false;
            }

            var length = (int)(_ptr - start);
            if (length > 0 && start[length - 1] == '\r')
                --length;

            result = new UnsafeAsciiStringView(start, length);
            return true;
        }
    }
}