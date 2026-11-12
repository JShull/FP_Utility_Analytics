// Copyright (c) 2026 John B. Shull. See LICENSE.md.
namespace FuzzPhyte.Utility.Analytics.WorkBench
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;

    /// <summary>Byte-based CSV scanner so multiline records have exact seekable source offsets.</summary>
    internal sealed class FPWorkbenchCsvRecords
    {
        private readonly Stream stream;
        private readonly FPWorkbenchCsvProfile profile;
        private readonly Encoding encoding;
        private readonly CancellationToken cancellation;
        private readonly List<byte> field = new List<byte>(128);
        public FPWorkbenchCsvRecords(Stream stream, FPWorkbenchCsvProfile profile, CancellationToken cancellation)
        {
            this.stream = stream; this.profile = profile; this.cancellation = cancellation;
            encoding = Encoding.GetEncoding(profile.EncodingCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        public string[] Read()
        {
            cancellation.ThrowIfCancellationRequested();
            int first = stream.ReadByte();
            if (first < 0) return null;
            var values = new List<string>(); field.Clear();
            bool quoted = false, afterQuote = false, started = false;
            int length = 0, b = first;
            while (true)
            {
                if ((length & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                if (++length > profile.MaximumRecordBytes) throw new InvalidDataException("CSV logical record exceeds its configured byte bound.");
                if (b < 0)
                {
                    if (quoted) throw new InvalidDataException("Unterminated quoted CSV field.");
                    Add(values); return values.ToArray();
                }
                if (quoted)
                {
                    if (b == '"') { quoted = false; afterQuote = true; }
                    else field.Add((byte)b);
                }
                else if (afterQuote && b == '"') { field.Add((byte)'"'); quoted = true; afterQuote = false; }
                else if (b == profile.Delimiter)
                {
                    Add(values); started = false; afterQuote = false;
                }
                else if (b == '\r' || b == '\n')
                {
                    if (b == '\r')
                    {
                        long position = stream.Position;
                        if (stream.ReadByte() != '\n') stream.Position = position;
                    }
                    Add(values); return values.ToArray();
                }
                else if (afterQuote) throw new InvalidDataException("Unexpected character after a closing CSV quote.");
                else if (b == '"')
                {
                    if (started) throw new InvalidDataException("Quote within an unquoted CSV field.");
                    quoted = true; started = true;
                }
                else { field.Add((byte)b); started = true; }
                b = stream.ReadByte();
            }
        }
        private void Add(List<string> values)
        {
            if (values.Count >= profile.MaximumColumns) throw new InvalidDataException("CSV column count exceeds its configured bound.");
            values.Add(encoding.GetString(field.ToArray())); field.Clear();
        }
    }
}
