using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EncodingChecker;

/// <summary>How far the file's bytes determine which encoding wrote them.</summary>
internal enum AmbiguityClass
{
    /// <summary>Only one supported codec reads these bytes at all.</summary>
    Unambiguous,

    /// <summary>
    /// Several codecs read them, and every one yields the same text. The label is
    /// undetermined; the content is not, so a conversion cannot lose anything.
    /// </summary>
    TextEquivalent,

    /// <summary>
    /// Several codecs read them and disagree about what they say. The bytes do not
    /// determine the answer, and choosing wrongly changes the user's text.
    /// </summary>
    TextChanging,
}

/// <summary>Why a file was placed in its <see cref="AmbiguityClass"/>.</summary>
internal enum AmbiguityReason
{
    /// <summary>Only one supported codec reads these bytes.</summary>
    SingleCandidate,

    /// <summary>The detected codec constrains these bytes; rivals do not compete.</summary>
    StructurallyDetermined,

    /// <summary>Several codecs read them and produce the same text.</summary>
    MultipleCodecsSameText,

    /// <summary>Several codecs read them and produce different text.</summary>
    MultipleCodecsDifferentText,

    /// <summary>The source encoding was chosen rather than detected.</summary>
    ExplicitlySpecified,
}

internal sealed record AmbiguityAnalysis
{
    internal required AmbiguityClass Class { get; init; }

    internal required AmbiguityReason Reason { get; init; }

    /// <summary>Supported codecs that strictly decode the sample, by name.</summary>
    internal required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>Distinct readings those candidates produce.</summary>
    internal required int DistinctReadings { get; init; }

    /// <summary>
    /// Codecs whose reading differs from the detected one. These are the competing
    /// interpretations worth naming to a user, rather than the full candidate list.
    /// </summary>
    internal required IReadOnlyList<string> CompetingCandidates { get; init; }

    internal bool IsSafeToConvertAutomatically => Class != AmbiguityClass.TextChanging;

    /// <summary>A reason a person can act on, naming the codecs actually in conflict.</summary>
    internal string Describe(string detectedName) =>
        Class == AmbiguityClass.TextChanging
            ? DescribeRefusal(detectedName, CompetingCandidates)
            : string.Empty;

    /// <summary>
    /// The refusal message, phrased so the next step is obvious. "Low confidence" tells a
    /// user nothing they can act on; naming the encodings actually in conflict does.
    /// </summary>
    internal static string DescribeRefusal(
        string detectedName, IReadOnlyList<string> competingCandidates)
    {
        string competing = string.Join(", ", competingCandidates.Take(4));

        if (competingCandidates.Count > 4)
            competing += $", and {competingCandidates.Count - 4} more";

        return "The encoding could not be determined uniquely from the file's contents. "
               + $"{detectedName} and {competing} all match this file and would produce "
               + "different text. No conversion was performed; specify the source "
               + "encoding explicitly to convert it.";
    }
}

/// <summary>
/// Decides whether a file's bytes actually identify the encoding that wrote them.
/// </summary>
/// <remarks>
/// A corpus audit of 5,078 files found 262 where they do not: single-byte code pages map
/// 256 values independently, so a file valid in windows-1252 is equally valid in
/// iso-8859-1, and no inspection of the bytes decides between them. Detection heuristics
/// still answer, and on short or ASCII-heavy input that answer is close to a guess.
/// <para>
/// The distinction that matters is not how many codecs *can* read the bytes but whether
/// they *disagree* about the result. A pure-ASCII file read as us-ascii or as UTF-8 is
/// ambiguous in label and identical in text; the same file read as iso-8859-1 or
/// windows-1252 with bytes in 0x80-0x9F is not.
/// </para>
/// </remarks>
internal static class EncodingAmbiguity
{
    /// <summary>
    /// Bytes examined. Matches the detector's own sample: asking a different question of
    /// a different span could conclude "unambiguous" about a region the detector never saw.
    /// </summary>
    internal const int SampleBytes = 64 * 1024;

    /// <summary>
    /// The codecs EC can name, deduplicated by code page so aliases do not appear as
    /// rival interpretations of the same bytes. Derived from what EC supports rather
    /// than from any corpus - a candidate set drawn from test data makes the answer
    /// depend on what the tests happened to contain.
    /// </summary>
    private static readonly Lazy<Encoding[]> Universe = new(() =>
    {
        var seen = new HashSet<int>();
        var result = new List<Encoding>();

        foreach (string name in TextEncoding.SupportedCharsets)
        {
            Encoding encoding;

            try
            {
                encoding = Encoding.GetEncoding(
                    name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                continue;
            }

            if (seen.Add(encoding.CodePage))
                result.Add(encoding);
        }

        return [.. result];
    });

    /// <summary>
    /// Above this, the codec has some hold on the input.
    /// </summary>
    /// <remarks>
    /// Set just above zero because the observed separation is not a matter of degree: a
    /// single-byte code page with no undefined positions rejects *nothing*, measuring
    /// exactly 0.000, while every multi-byte and Unicode encoding measured on real text
    /// lands at 0.111 or above. A larger margin was tried first and put Shift_JIS at
    /// 0.146 - above the line, but close enough that a different probe order moved it
    /// across. The gap is zero versus non-zero; the threshold should say so rather than
    /// invent a cutoff in the middle of empty space.
    /// </remarks>
    private const double ConstraintFloor = 0.02;

    /// <summary>Probe positions; enough to characterise the input without scanning it all.</summary>
    private const int ProbePositions = 256;

    /// <summary>
    /// How tightly a codec constrains these particular bytes: the fraction of small
    /// mutations it rejects.
    /// </summary>
    /// <remarks>
    /// This is what separates a real rival reading from a meaningless one. Every byte
    /// sequence is "valid" iso-8859-1, so iso-8859-1 offering a different reading of a
    /// UTF-8 file is not a competing claim - it is a codec that cannot refuse anything.
    /// Valid UTF-8 is improbable by accident, so a file that survives mutation under it
    /// was not valid by chance.
    /// <para>
    /// Asked of the bytes rather than of the codec, because single-byte does not imply
    /// unconstrained: windows-1252 leaves 0x81, 0x8D, 0x8F, 0x90 and 0x9D undefined, so a
    /// file containing one of them is constrained under it while another file is not.
    /// </para>
    /// <para>
    /// Probes evenly-spaced positions rather than random ones. An earlier version sampled
    /// randomly and gave answers that moved with the seed on short files - a conversion
    /// refusing or proceeding should not depend on a random draw.
    /// </para>
    /// </remarks>
    private static double ConstraintOn(ReadOnlySpan<byte> sample, Encoding encoding)
    {
        if (sample.Length < 2)
            return 0.0;

        int step = Math.Max(1, sample.Length / ProbePositions);
        int rejected = 0;
        int probes = 0;

        byte[] flipped = sample.ToArray();
        byte[] shortened = new byte[sample.Length - 1];

        for (int i = 0; i < sample.Length; i += step)
        {
            // Deleting a byte tests alignment, which is the only structure a fixed-width
            // encoding has: UTF-16 survives nearly any bit flip, because most flips just
            // produce a different valid character, but losing one byte shifts every unit
            // after it.
            sample[..i].CopyTo(shortened);
            sample[(i + 1)..].CopyTo(shortened.AsSpan(i));

            probes++;
            if (TryHash(shortened, encoding, flush: true) is null)
                rejected++;

            // Flipping the high bit tests the value, which is what the multi-byte pages
            // constrain.
            byte original = flipped[i];
            flipped[i] ^= 0x80;

            probes++;
            if (TryHash(flipped, encoding, flush: true) is null)
                rejected++;

            flipped[i] = original;
        }

        return probes == 0 ? 0.0 : (double)rejected / probes;
    }

    internal static AmbiguityAnalysis Analyze(ReadOnlySpan<byte> sample, Encoding detected)
    {
        ArgumentNullException.ThrowIfNull(detected);

        if (sample.Length > SampleBytes)
            sample = sample[..SampleBytes];

        string? detectedHash = null;
        var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (Encoding candidate in Universe.Value)
        {
            string? hash = TryHash(sample, candidate);

            if (hash is null)
                continue;

            if (!byHash.TryGetValue(hash, out List<string>? names))
                byHash[hash] = names = [];

            names.Add(candidate.WebName);

            if (candidate.CodePage == detected.CodePage)
                detectedHash = hash;
        }

        // The detected codec failing to decode its own sample is a detection problem,
        // not an ambiguity one; leave it to strict decoding to reject.
        if (detectedHash is null)
        {
            return new AmbiguityAnalysis
            {
                Class = AmbiguityClass.Unambiguous,
                Reason = AmbiguityReason.SingleCandidate,
                Candidates = [],
                DistinctReadings = byHash.Count,
                CompetingCandidates = [],
            };
        }

        List<string> candidates = [.. byHash.Values.SelectMany(v => v)];

        List<string> competing =
        [
            .. byHash
                .Where(pair => !string.Equals(pair.Key, detectedHash, StringComparison.Ordinal))
                .SelectMany(pair => pair.Value)
                .Order(StringComparer.Ordinal)
        ];

        // Rival readings only compete when the detected codec has no hold on these
        // bytes. If it does - valid UTF-8, valid Shift_JIS - the file's structure
        // already picked it out, and codecs that accept every byte sequence are not
        // offering an alternative so much as failing to object.
        bool detectedIsDetermined =
            competing.Count > 0 && ConstraintOn(sample, detected) >= ConstraintFloor;

        if (detectedIsDetermined)
            competing = [];

        AmbiguityClass classification =
            competing.Count > 0 ? AmbiguityClass.TextChanging
            : candidates.Count > 1 ? AmbiguityClass.TextEquivalent
            : AmbiguityClass.Unambiguous;

        AmbiguityReason reason =
            competing.Count > 0 ? AmbiguityReason.MultipleCodecsDifferentText
            : detectedIsDetermined ? AmbiguityReason.StructurallyDetermined
            : candidates.Count > 1 ? AmbiguityReason.MultipleCodecsSameText
            : AmbiguityReason.SingleCandidate;

        return new AmbiguityAnalysis
        {
            Class = classification,
            Reason = reason,
            Candidates = candidates,
            DistinctReadings = byHash.Count,
            CompetingCandidates = competing,
        };
    }

    /// <summary>
    /// SHA-256 over the decoded text, or <see langword="null"/> if this codec cannot
    /// strictly decode the sample.
    /// </summary>
    /// <param name="flush">
    /// Whether an incomplete trailing sequence counts as invalid.
    /// <para>
    /// False when asking which codecs read the real sample: it may genuinely cut a
    /// character in half at 64 KiB, and a boundary artifact is not evidence.
    /// </para>
    /// <para>
    /// True when measuring constraint against mutated copies, where an incomplete tail
    /// is the whole point. Deleting a byte from a UTF-16 file leaves an odd length, and
    /// tolerating that made fixed-width encodings look unconstrained - they survive
    /// almost any bit flip, so alignment is the only structure they have to test.
    /// </para>
    /// </param>
    private static string? TryHash(
        ReadOnlySpan<byte> sample, Encoding encoding, bool flush = false)
    {
        char[] buffer;

        try
        {
            buffer = new char[encoding.GetMaxCharCount(sample.Length)];
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        int written;

        try
        {
            Decoder decoder = TextEncoding.Strict(encoding).GetDecoder();
            written = decoder.GetChars(sample, buffer, flush);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            return null;
        }

        if (written == 0)
            return null;

        Span<byte> bytes = stackalloc byte[sizeof(char)];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int i = 0; i < written; i++)
        {
            BitConverter.TryWriteBytes(bytes, buffer[i]);
            sha.AppendData(bytes);
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
