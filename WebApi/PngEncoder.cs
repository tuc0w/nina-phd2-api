using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    /// <summary>
    /// Minimal PNG encoder for 8-bit grayscale images.
    /// No external dependencies – uses ZLibStream (built-in since .NET 6).
    /// </summary>
    internal static class PngEncoder {

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>
        /// Encodes raw 8-bit grayscale pixels into a valid PNG byte array.
        /// </summary>
        /// <param name="pixels">Row-major grayscale pixel data (width × height bytes).</param>
        /// <param name="width">Image width in pixels.</param>
        /// <param name="height">Image height in pixels.</param>
        public static byte[] Encode(byte[] pixels, int width, int height) {
            using var ms = new MemoryStream();
            ms.Write(Signature);
            WriteChunk(ms, "IHDR", BuildIhdr(width, height));
            WriteChunk(ms, "IDAT", BuildIdat(pixels, width, height));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }

        /// <summary>
        /// Decodes raw 16-bit little-endian grayscale pixels (as returned by PHD2 get_star_image),
        /// auto-stretches to the full 0-255 range, and encodes as an 8-bit grayscale PNG.
        /// </summary>
        /// <param name="raw16">Raw pixel bytes – each pixel is 2 bytes (uint16 LE), width × height pixels.</param>
        public static byte[] EncodeFrom16Bit(byte[] raw16, int width, int height) {
            int pixelCount = width * height;
            var pixels16 = new ushort[pixelCount];
            for (int i = 0; i < pixelCount; i++)
                pixels16[i] = (ushort)(raw16[i * 2] | (raw16[i * 2 + 1] << 8));

            ushort min = ushort.MaxValue, max = ushort.MinValue;
            foreach (var v in pixels16) {
                if (v < min) min = v;
                if (v > max) max = v;
            }

            var pixels8 = new byte[pixelCount];
            if (max > min) {
                float range = max - min;
                for (int i = 0; i < pixelCount; i++)
                    pixels8[i] = (byte)((pixels16[i] - min) * 255f / range);
            }

            return Encode(pixels8, width, height);
        }

        private static byte[] BuildIhdr(int width, int height) {
            var data = new byte[13];
            WriteBigEndian(data, 0, width);
            WriteBigEndian(data, 4, height);
            data[8]  = 8; // bit depth
            data[9]  = 0; // colour type: grayscale
            data[10] = 0; // compression method
            data[11] = 0; // filter method
            data[12] = 0; // interlace method
            return data;
        }

        private static byte[] BuildIdat(byte[] pixels, int width, int height) {
            // Prepend filter byte 0x00 (None) to every scanline
            var filtered = new byte[height * (width + 1)];
            for (int y = 0; y < height; y++) {
                filtered[y * (width + 1)] = 0x00;
                Buffer.BlockCopy(pixels, y * width, filtered, y * (width + 1) + 1, width);
            }

            using var ms = new MemoryStream();
            using (var zlib = new ZLibStream(ms, CompressionMode.Compress, leaveOpen: true))
                zlib.Write(filtered, 0, filtered.Length);

            return ms.ToArray();
        }

        private static void WriteChunk(Stream stream, string type, byte[] data) {
            var typeBytes = Encoding.ASCII.GetBytes(type);

            var lenBuf = new byte[4];
            WriteBigEndian(lenBuf, 0, data.Length);
            stream.Write(lenBuf);

            stream.Write(typeBytes);
            stream.Write(data);

            var crcInput = new byte[4 + data.Length];
            Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
            Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);

            var crcBuf = new byte[4];
            WriteBigEndian(crcBuf, 0, (int)ComputeCrc32(crcInput));
            stream.Write(crcBuf);
        }

        private static void WriteBigEndian(byte[] buf, int offset, int value) {
            buf[offset]     = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static uint ComputeCrc32(byte[] data) {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data) {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
