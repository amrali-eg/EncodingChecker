using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EncodingChecker.Tests;

/// <summary>
/// Rules about the test suite itself, checked by the test suite itself.
///
/// <see cref="ScanEngine.ScanDirectory"/> and <see cref="ScanEngine.ConvertFiles"/> invoke
/// their callbacks concurrently from worker threads and say so. The suite passed
/// <c>List&lt;T&gt;.Add</c> anyway, in twenty files, and lost entries — measured at three
/// runs in forty over two hundred files. It surfaced once, as a CI failure whose message
/// blamed the product: a file modified after planning was reported as converted rather
/// than stale, because that file had never reached the plan for the staleness check to
/// look at.
///
/// The rest of the time it did not surface at all, which is the part worth guarding
/// against. A dropped entry does not throw; it quietly removes a file from what a test
/// then asserts about, so the test passes while asserting less than it claims. A
/// concurrent test that silently loses an input does not merely flake — it can report a
/// safety property as verified when it was never exercised.
///
/// So the rule is enforced here rather than left to each future author to remember.
/// </summary>
public sealed class HarnessInvariantTests
{
    /// <summary>
    /// The calls whose callbacks the engine documents as concurrent. Callbacks handed to
    /// anything else — a plain sequential enumeration, say — are not this rule's business,
    /// and flagging them would make the rule noise that people learn to ignore.
    /// </summary>
    private static readonly string[] ConcurrentCalls =
    [
        "ScanEngine.ScanDirectory(",
        "ScanEngine.ConvertFiles(",
    ];

    /// <summary>
    /// Passes <c>NAME.Add</c> as a method group — an argument, not a call. A call is
    /// followed by '('; a callback being handed over is followed by ',' or ')'.
    /// </summary>
    private static readonly Regex AddAsCallback = new(
        @"\b(\w+)\.Add\s*(?=[,)\r\n])", RegexOptions.Compiled);

    private static string TestSourceDirectory([CallerFilePath] string thisFile = "") =>
        Path.GetDirectoryName(thisFile)!;

    [Fact]
    public void NoTestHandsAPlainListToAConcurrentCallback()
    {
        string directory = TestSourceDirectory();

        Assert.True(
            Directory.Exists(directory),
            $"the test sources were not found at '{directory}'; this check reads them, "
            + "so it has to run from a source checkout");

        var offences = new List<string>();
        var scanned = 0;

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            string text = File.ReadAllText(file);

            foreach ((int start, string arguments) in ConcurrentCallArguments(text))
            {
                scanned++;

                foreach (Match match in AddAsCallback.Matches(arguments))
                {
                    string name = match.Groups[1].Value;

                    if (!IsDeclaredAsAPlainList(text, start, name))
                        continue;

                    offences.Add(
                        $"{Path.GetFileName(file)}:{LineOf(text, start)}: "
                        + $"'{name}.Add' is a List's Add handed to a concurrent callback");
                }
            }
        }

        // A rule that silently matched nothing would look identical to a rule that
        // passed, so the search itself is checked before its result is trusted.
        Assert.True(
            scanned > 20,
            $"only {scanned} concurrent call site(s) were found to check; the search is "
            + "not looking where it thinks it is");

        Assert.True(
            offences.Count == 0,
            "A List<T> is being handed to a callback the scan engine documents as "
            + "concurrent, so entries can be dropped without anything failing. Collect "
            + "with EntrySink instead."
            + Environment.NewLine
            + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void TheSanctionedSinkIsActuallyUsed()
    {
        // A ban with nothing to point at is a ban people work around. If the replacement
        // ever stops being used, the rule above has quietly become unenforceable rather
        // than satisfied.
        int users = Directory
            .EnumerateFiles(TestSourceDirectory(), "*.cs")
            .Count(f => File.ReadAllText(f).Contains(
                "new EntrySink()", StringComparison.Ordinal));

        Assert.True(
            users > 10,
            $"only {users} test file(s) collect through EntrySink; the concurrent "
            + "callbacks are being collected some other way");
    }

    /// <summary>
    /// The argument list of every call whose callback runs concurrently, with the index
    /// the call starts at.
    /// </summary>
    private static IEnumerable<(int Start, string Arguments)> ConcurrentCallArguments(
        string text)
    {
        foreach (string call in ConcurrentCalls)
        {
            int from = 0;

            while (true)
            {
                int start = text.IndexOf(call, from, StringComparison.Ordinal);

                if (start < 0)
                    break;

                int open = start + call.Length - 1;
                int depth = 0;
                int i = open;

                for (; i < text.Length; i++)
                {
                    if (text[i] == '(')
                        depth++;
                    else if (text[i] == ')' && --depth == 0)
                        break;
                }

                yield return (start, text[(open + 1)..Math.Min(i, text.Length)]);
                from = start + call.Length;
            }
        }
    }

    /// <summary>
    /// Whether the nearest declaration of <paramref name="name"/> before
    /// <paramref name="before"/> makes it a plain <c>List</c>.
    /// </summary>
    /// <remarks>
    /// Nearest, not anywhere in the file: one method's <c>List</c> named <c>entries</c>
    /// must not condemn another method's <c>EntrySink</c> of the same name, which is the
    /// false positive the first version of this rule produced.
    /// </remarks>
    private static bool IsDeclaredAsAPlainList(string text, int before, string name)
    {
        MatchCollection declarations = Regex.Matches(
            text[..before],
            $@"\b(?:var|List<[^>]*>)\s+{Regex.Escape(name)}\s*=\s*(new\s+)?(?<type>\w+)");

        return declarations.Count > 0
               && declarations[^1].Groups["type"].Value == "List";
    }

    private static int LineOf(string text, int index) =>
        text[..index].Count(c => c == '\n') + 1;
}
