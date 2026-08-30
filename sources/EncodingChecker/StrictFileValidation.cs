using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace EncodingChecker;

/// <summary>
/// EC-only full-file strict decoding used by conversion safety checks and <c>-Validate</c>.
/// </summary>
/// <remarks>
/// This is intentionally separate from <see cref="TextValidation"/>, whose buffer-based
/// text-quality check is shared with LineEndingNormalizer and CorpusTesters.
/// </remarks>
internal static class StrictFileValidation
{
    internal static bool TryValidateFile(
        string path, Encoding encoding, out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(encoding);

        using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);

        return TryValidateStream(stream, encoding, out diagnostic);
    }

    internal static bool TryValidateStream(
        Stream stream, Encoding encoding, out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoding);

        if (!stream.CanSeek)
            throw new ArgumentException(@"The stream must be seekable.", nameof(stream));

        diagnostic = null;
        long originalPosition = stream.Position;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(64 * 1024);
        char[] chars = ArrayPool<char>.Shared.Rent(
            TextEncoding.Strict(encoding).GetMaxCharCount(bytes.Length));

        try
        {
            stream.Position = 0;
            Decoder decoder = TextEncoding.Strict(encoding).GetDecoder();

            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                _ = decoder.GetChars(bytes, 0, read, chars, 0, flush: false);

            _ = decoder.GetChars([], 0, 0, chars, 0, flush: true);
            return true;
        }
        catch (Exception ex) when (ex is DecoderFallbackException or NotSupportedException)
        {
            diagnostic = ex.Message;
            return false;
        }
        finally
        {
            stream.Position = originalPosition;
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<char>.Shared.Return(chars);
        }
    }
}
