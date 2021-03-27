using System;
using System.IO;
using System.Text;
using UtfUnknown;

namespace EncodingUtils
{
    public static class TextEncoding
    {
        /// <summary>
        /// https://netvignettes.wordpress.com/2011/07/03/how-to-detect-encoding/
        /// </summary>
        private static readonly DecoderExceptionFallback DecoderExceptionFallback = new DecoderExceptionFallback();
        public static bool Validate(this Encoding encoding, byte[] bytes, int offset = 0, int? length = null)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException(nameof(encoding));
            }
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            length = length ?? bytes.Length;
            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), @"Offset is out of range.");
            }
            if (length < 0 || length > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(length), @"Length is out of range.");
            }
            else if ((offset + length) > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), @"The specified range is outside of the specified buffer.");
            }
            var decoder = encoding.GetDecoder();
            decoder.Fallback = DecoderExceptionFallback;
            try
            {
                decoder.GetCharCount(bytes, offset, length.Value);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        ///  Get the System.Text.Encoding of this file.
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <returns>System.Text.Encoding (can be null if not available or not supported by .NET).</returns>
        public static Encoding GetFileEncoding(string filePath)
        {
            return GetFileEncoding(filePath, null);
        }

        /// <summary>
        ///  Get the System.Text.Encoding of this file.
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="maxBytesToRead">max bytes to read from <paramref name="filePath"/>. If <c>null</c>, then no max</param>
        /// <returns>System.Text.Encoding (can be null if not available or not supported by .NET).</returns>
        public static Encoding GetFileEncoding(string filePath, int? maxBytesToRead)
        {
            using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // Check for possible UTF-16 encoding (LE or BE).
                Encoding encoding = Utf16Detector.DetectFromStream(stream, maxBytesToRead);
                if (encoding != null)
                {
                    return encoding;
                }
                // https://github.com/CharsetDetector/UTF-unknown
                stream.Position = 0L;
                return CharsetDetector.DetectFromStream(stream, maxBytesToRead).Detected?.Encoding;
            }
        }
    }
}
