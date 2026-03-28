using System;
using System.IO;
using Google.Protobuf;
using Nevr.Telemetry.V2;
using ZstdSharp;

namespace Tape
{
    /// <summary>
    /// Reads .tape v2 files which are Zstd-compressed streams of length-delimited
    /// protobuf Envelope messages. Format: CaptureHeader, Frame*, CaptureFooter.
    /// </summary>
    public class TapeReader : IDisposable
    {
        private readonly FileStream _fileStream;
        private readonly DecompressionStream _decompressor;
        private bool _disposed;

        public CaptureHeader Header { get; private set; }
        public CaptureFooter Footer { get; private set; }

        public TapeReader(string filePath)
        {
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            _decompressor = new DecompressionStream(_fileStream);
        }

        /// <summary>
        /// Reads the capture header (first envelope in the stream).
        /// </summary>
        public CaptureHeader ReadHeader()
        {
            var envelope = ReadEnvelope();
            if (envelope == null)
                return null;

            if (envelope.MessageCase != Envelope.MessageOneofCase.Header)
                throw new InvalidDataException("Expected CaptureHeader as first envelope message");

            Header = envelope.Header;
            return Header;
        }

        /// <summary>
        /// Reads the next frame from the stream.
        /// Returns null at end of stream or when the footer is reached.
        /// </summary>
        public Frame ReadFrame()
        {
            var envelope = ReadEnvelope();
            if (envelope == null)
                return null;

            if (envelope.MessageCase == Envelope.MessageOneofCase.Footer)
            {
                Footer = envelope.Footer;
                return null;
            }

            if (envelope.MessageCase != Envelope.MessageOneofCase.Frame)
                throw new InvalidDataException($"Expected Frame envelope, got {envelope.MessageCase}");

            return envelope.Frame;
        }

        private Envelope ReadEnvelope()
        {
            byte[] data = ReadDelimitedMessage();
            if (data == null)
                return null;

            return Envelope.Parser.ParseFrom(data);
        }

        private byte[] ReadDelimitedMessage()
        {
            ulong length = 0;
            int shift = 0;

            while (true)
            {
                int b = _decompressor.ReadByte();
                if (b == -1)
                    return null;

                length |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    break;

                shift += 7;
                if (shift >= 64)
                    throw new InvalidDataException("Varint is too long");
            }

            byte[] data = new byte[length];
            int bytesRead = 0;
            while ((ulong)bytesRead < length)
            {
                int read = _decompressor.Read(data, bytesRead, (int)(length - (ulong)bytesRead));
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading message");
                bytesRead += read;
            }

            return data;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _decompressor?.Dispose();
                _fileStream?.Dispose();
                _disposed = true;
            }
        }
    }
}
