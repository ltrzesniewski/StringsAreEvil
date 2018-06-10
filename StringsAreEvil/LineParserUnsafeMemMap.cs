using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace StringsAreEvil
{
    internal unsafe class LineParserUnsafeMemMap
    {
        private readonly List<ValueHolderAsStruct> _list = new List<ValueHolderAsStruct>();
        private readonly byte* _endPtr;
        private byte* _linePtr;

        private const uint Mno = (byte)',' << 24 | (byte)'O' << 16 | (byte)'N' << 8 | (byte)'M';

        private LineParserUnsafeMemMap(MemoryMappedViewAccessor accessor)
        {
            _linePtr = null;
            accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref _linePtr);

            if (_linePtr == null)
                throw new InvalidOperationException();

            _endPtr = _linePtr + accessor.Capacity;
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
            while (ReadLine(out var line))
            {
                var ptr = line.Start;
                var eol = ptr + line.Length;

                if (line.Length < 4 || *(uint*)ptr != Mno)
                    continue;

                ptr += 4;

                var elementId = LineParserUnsafe.ParseInt(ref ptr, eol);
                var vehicleId = LineParserUnsafe.ParseInt(ref ptr, eol);
                var term = LineParserUnsafe.ParseInt(ref ptr, eol);
                var mileage = LineParserUnsafe.ParseInt(ref ptr, eol);
                var value = LineParserUnsafe.ParseDecimal(ref ptr, eol);
                var valueHolder = new ValueHolderAsStruct(elementId, vehicleId, term, mileage, value);
                //_list.Add(valueHolder);
            }
        }

        private bool ReadLine(out UnsafeAsciiStringView result)
        {
            var start = _linePtr;

            while (_linePtr < _endPtr)
            {
                if (*_linePtr++ == '\n')
                    break;
            }

            if (_linePtr == start)
            {
                result = default;
                return false;
            }

            var length = (int)(_linePtr - start);
            if (length > 0 && start[length - 1] == '\r')
                --length;

            result = new UnsafeAsciiStringView(start, length);
            return true;
        }

        private readonly struct UnsafeAsciiStringView
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
    }
}