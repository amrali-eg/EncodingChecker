using System;
using System.IO;
using System.Text;

namespace EncodingUtils
{
    /// http://architectshack.com/TextFileEncodingDetector.ashx
    /// https://github.com/AutoItConsulting/text-encoding-detect/blob/master/TextEncodingDetect-C%23/TextEncodingDetect/TextEncodingDetect.cs
    /// https://github.com/ashtuchkin/iconv-lite/blob/master/encodings/utf16.js
    /// https://sourceforge.net/p/jedit/feature-requests/396/
    /// https://stackoverflow.com/questions/1025332/determine-a-strings-encoding-in-c-sharp
    /// https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-istextunicode
    public static class Utf16Detector
    {
        /// <summary>
        /// Detect the UTF character encoding form this byte array.
        /// It searches for BOM from bytes[0].
        /// </summary>
        /// <param name="bytes">The byte array containing the text</param>
        /// <returns>UTF Encoding or null if not available.</returns>
        public static Encoding DetectFromBytes(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            using (MemoryStream stream = new MemoryStream(bytes))
            {
                return DetectFromStream(stream);
            }
        }

        /// <summary>
        /// Detect the UTF character encoding form this byte array.
        /// It searches for BOM from bytes[offset].
        /// </summary>
        /// <param name="bytes">The byte array containing the text</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin reading the data from</param>
        /// <param name="len">The maximum number of bytes to be read</param>
        /// <returns></returns>
        public static Encoding DetectFromBytes(byte[] bytes, int offset, int len)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (len < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(len));
            }
            if (bytes.Length < offset + len)
            {
                throw new ArgumentException($"{nameof(len)} is greater than the number of bytes from {nameof(offset)} to the end of the array.");
            }

            using (MemoryStream stream = new MemoryStream(bytes, offset, len))
            {
                return DetectFromStream(stream);
            }
        }

        /// <summary>
        /// Detect the UTF character encoding by reading the stream.
        ///
        /// Note: stream position is not reset before and after.
        /// </summary>
        /// <param name="stream">The steam. </param>
        /// <returns>UTF Encoding or null if not available.</returns>
        public static Encoding DetectFromStream(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            return DetectFromStream(stream, null);
        }

        /// <summary>
        /// Detect the UTF character encoding by reading the stream.
        ///
        /// Note: stream position is not reset before and after.
        /// </summary>
        /// <param name="stream">The steam. </param>
        /// <param name="maxBytesToRead">max bytes to read from <paramref name="stream"/>. If <c>null</c>, then no max</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxBytesToRead"/> 0 or lower.</exception>
        /// <returns></returns>
        public static Encoding DetectFromStream(Stream stream, int? maxBytesToRead)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (maxBytesToRead <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytesToRead));
            }

            if (!maxBytesToRead.HasValue || maxBytesToRead > stream.Length)
                maxBytesToRead = (int)stream.Length;

            // First read only what we need for BOM detection
            byte[] buffer = new byte[maxBytesToRead.Value];
            int numBytesRead = stream.Read(buffer, 0, Math.Min(maxBytesToRead.Value, 4));

            Encoding encoding = CheckUtfSignature(buffer, numBytesRead);
            if (encoding != null)
            {
                return encoding;
            }

            // BOM Detection failed, going for heuristics now.
            if (numBytesRead < maxBytesToRead)
                numBytesRead += stream.Read(buffer, numBytesRead, maxBytesToRead.Value - numBytesRead);

            encoding = CheckUtf16Ascii(buffer, numBytesRead);
            if (encoding != null)
            {
                return encoding;
            }

            encoding = CheckUtf16ControlChars(buffer, numBytesRead);
            return encoding;
        }

        /// <summary>
        /// Detect the UTF character encoding of this file.
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <returns>UTF Encoding or null if not available.</returns>
        public static Encoding DetectFromFile(string filePath)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return DetectFromStream(fs);
            }
        }

        /// <summary>
        /// Detect the UTF character encoding of this file.
        /// </summary>
        /// <param name="filePath">Path to file</param>
        /// <param name="maxBytesToRead">max bytes to read from <paramref name="filePath"/>. If <c>null</c>, then no max</param>
        /// <returns></returns>
        public static Encoding DetectFromFile(string filePath, int? maxBytesToRead)
        {
            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (maxBytesToRead <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytesToRead));
            }

            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return DetectFromStream(fs, maxBytesToRead);
            }
        }

        /// <summary>
        /// Checks for a BOM sequence in a byte buffer.
        /// </summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="size">The size of the byte buffer.</param>
        /// <returns>UTF Encoding or null if no BOM.</returns>
        private static Encoding CheckUtfSignature(byte[] buffer, int size)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (size < 2)
            {
                return null;
            }

            // Check for BOM
            if (buffer[0] == 0xff && buffer[1] == 0xfe && (size < 4 || buffer[2] != 0 || buffer[3] != 0)) return Encoding.Unicode;
            if (buffer[0] == 0xfe && buffer[1] == 0xff && (size < 4 || buffer[2] != 0 || buffer[3] != 0)) return Encoding.BigEndianUnicode;

            if (size >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf) return Encoding.UTF8;

            if (size >= 4 && buffer[0] == 0xff && buffer[1] == 0xfe && buffer[2] == 0x00 && buffer[3] == 0x00) return Encoding.UTF32;
            if (size >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xfe && buffer[3] == 0xff) return Encoding.GetEncoding("utf-32BE");

            return null;
        }

        /// <summary>
        /// Checks if a buffer contains text that looks like utf-16 by scanning for
        /// UTF-16 text characters that are from the English ISO-8859-1 subset.
        /// </summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="size">The size of the byte buffer.</param>
        /// <returns>UTF16 Encoding or null if not available.</returns>
        private static Encoding CheckUtf16Ascii(byte[] buffer, int size)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (size < 2)
            {
                return null;
            }

            // Reduce size by 1 so we don't need to worry about bounds checking for pairs of bytes
            size--;

            const double threshold = 0.5; // To allow some UTF-16 characters that aren't from English ISO-8859-1 subset while still detecting the encoding.
            const double limit = 0.1;

            var leAsciiChars = 0;
            var beAsciiChars = 0;

            var pos = 0;
            while (pos < size)
            {
                byte ch1 = buffer[pos++];
                byte ch2 = buffer[pos++];

                // Count even nulls
                if (ch1 == 0 && ch2 != 0)
                {
                    beAsciiChars++;
                }

                // Count odd nulls
                if (ch1 != 0 && ch2 == 0)
                {
                    leAsciiChars++;
                }
            }

            // Restore size
            size++;

            double leAsciiCharsPct = leAsciiChars * 2.0 / size;
            double beAsciiCharsPct = beAsciiChars * 2.0 / size;

            // Make decisions.
            if (leAsciiCharsPct > threshold && beAsciiCharsPct < limit)
            {
                return Encoding.Unicode;
            }

            if (beAsciiCharsPct > threshold && leAsciiCharsPct < limit)
            {
                return Encoding.BigEndianUnicode;
            }

            // Couldn't decide
            return null;
        }

        /// <summary>
        /// Checks if a buffer contains text that looks like utf-16 by scanning for
        /// newline chars that would be present even in non-english text.
        /// </summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="size">The size of the byte buffer.</param>
        /// <returns>UTF16 Encoding or null if not available.</returns>
        private static Encoding CheckUtf16ControlChars(byte[] buffer, int size)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (size < 2)
            {
                return null;
            }

            // Reduce size by 1 so we don't need to worry about bounds checking for pairs of bytes
            size--;

            var leControlChars = 0;
            var beControlChars = 0;

            var pos = 0;
            while (pos < size)
            {
                byte ch1 = buffer[pos++];
                byte ch2 = buffer[pos++];

                if (ch1 == 0)
                {
                    if (ch2 == '\r' || ch2 == '\n' || ch2 == ' ' || ch2 == '\t')
                    {
                        ++beControlChars;
                    }
                }
                else if (ch2 == 0)
                {
                    if (ch1 == '\r' || ch1 == '\n' || ch1 == ' ' || ch1 == '\t')
                    {
                        ++leControlChars;
                    }
                }

                // If we are getting both LE and BE control chars then this file is not utf16
                if (leControlChars > 0 && beControlChars > 0)
                {
                    return null;
                }
            }

            if (leControlChars > 0)
            {
                return Encoding.Unicode;
            }

            return beControlChars > 0 ? Encoding.BigEndianUnicode : null;
        }
    }
}
