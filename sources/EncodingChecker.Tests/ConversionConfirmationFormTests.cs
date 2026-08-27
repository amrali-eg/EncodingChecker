using System.Text;
using System.Windows.Forms;

namespace EncodingChecker.Tests;

/// <summary>
/// The confirmation dialog is the only part of the safety model a GUI user ever reads,
/// and until now it was also the only part no test had ever executed. Layout code that
/// has never run is layout code that throws the first time somebody converts a directory
/// with an unusual mix of outcomes — and it would throw at exactly the moment the user is
/// being asked to approve something.
///
/// These build it against real plans rather than asserting on pixels: every outcome mix,
/// on an STA thread, checking that it constructs and that what it says matches the plan
/// it was given.
/// </summary>
public sealed class ConversionConfirmationFormTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("ec_dialog_").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>Runs <paramref name="body"/> on an STA thread, as WinForms requires.</summary>
    private static void OnUiThread(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the dialog did not finish");

        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"the dialog threw: {failure}");
    }

    private void Write(string name, string text, string charset) =>
        File.WriteAllBytes(
            Path.Combine(_root, name), Encoding.GetEncoding(charset).GetBytes(text));

    /// <summary>A plan over whatever is currently in the directory.</summary>
    private ConversionPlan Plan(bool backup = true, string target = "utf-8")
    {
        var entries = new List<ConversionReportEntry>();

        ScanEngine.ScanDirectory(
            new ScanDirectoryOptions
            {
                BaseDirectory = _root,
                IncludeSubdirectories = true,
                IncludePatterns = ["*"],
                Action = ScanAction.Convert,
                TargetCharset = target,
                TargetWriteBom = false,
                WhatIf = true,
            },
            entries.Add,
            CancellationToken.None);

        return ConversionPlan.FromEntries(
            entries, _root, target, targetHasBom: false,
            backupEnabled: backup, explicitSource: null);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (Control nested in Descendants(child))
                yield return nested;
        }
    }

    private static string AllText(Control root) =>
        string.Join("\n", Descendants(root).Select(c => c.Text));

    [Fact]
    public void ItBuildsForAMixOfEveryOutcome()
    {
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");
        Write("plain.txt", "just ascii here", "ascii");
        Write("already.txt", "already utf-8 世界", "utf-8");

        ConversionPlan plan = Plan();

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.NotEmpty(Descendants(form).ToList());
        });
    }

    [Fact]
    public void ItBuildsWhenNothingIsRefused()
    {
        // The common case, and the one where a refusal panel must not appear at all.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");

        ConversionPlan plan = Plan();

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            Assert.DoesNotContain("need an explicit source encoding", AllText(form));
        });
    }

    [Fact]
    public void ItBuildsWhenEverythingIsRefused()
    {
        // Nothing to convert. The button has to say so rather than offering an action
        // that would do nothing.
        Write("a.txt", "Le café était déjà prêt", "windows-1252");
        Write("b.txt", "Привет мир, это русский", "koi8-r");

        ConversionPlan plan = Plan();

        Assert.All(plan.Files, f => Assert.Equal(PlannedAction.Refuse, f.Action));

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains("need an explicit source encoding", text);
            Assert.Contains("Nothing to convert", text);
            Assert.DoesNotContain("Convert 1 file", text);
        });
    }

    [Fact]
    public void ItNamesTheCompetingEncodingsRatherThanJustReportingLowConfidence()
    {
        // "Could not be determined" on its own gives a user nothing to act on. The
        // alternatives and the way out are what make the refusal actionable.
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();
        PlannedFile refused = Assert.Single(plan.Files);

        Assert.NotEmpty(refused.CompetingEncodings);

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            List<string> cells =
            [
                .. Descendants(form)
                    .OfType<ListView>()
                    .SelectMany(v => v.Items.Cast<ListViewItem>())
                    .SelectMany(i => i.SubItems.Cast<ListViewItem.ListViewSubItem>())
                    .Select(sub => sub.Text)
            ];

            Assert.Contains("ambiguous.txt", cells);
            Assert.Contains(cells, c => c.Contains(refused.CompetingEncodings[0]));

            // And the way out is offered, populated from the charsets EC supports.
            ComboBox chooser = Assert.Single(Descendants(form).OfType<ComboBox>());

            Assert.True(chooser.Items.Count > 1);
            Assert.Contains("windows-1252", chooser.Items.Cast<string>());
        });
    }

    [Fact]
    public void ItMakesTheScopeOfAnEncodingChoiceUnmistakable()
    {
        // The button says how many files the choice would apply to, and the count moves
        // with the ticks. A user must never have to infer how far their answer reaches.
        Write("french.txt", "Le café était déjà prêt", "windows-1252");
        Write("russian.txt", "Привет мир, это русский текст", "koi8-r");

        ConversionPlan plan = Plan();

        Assert.Equal(2, plan.Files.Count(f => f.Action == PlannedAction.Refuse));

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            // ListView caches check state until it has a window handle, and only raises
            // ItemChecked once it does. Nothing here pumps messages; the handle is enough.
            form.CreateControl();
            _ = form.Handle;

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());

            list.CreateControl();
            _ = list.Handle;

            // Everything the dialog asked about starts ticked, and the button says so.
            Assert.Equal(2, list.CheckedItems.Count);
            Assert.Contains("for 2 file(s)", AllText(form));

            list.Items[0].Checked = false;

            Assert.Contains("for 1 file(s)", AllText(form));

            // And with none ticked there is nothing to apply, so it cannot be pressed.
            list.Items[1].Checked = false;

            Button apply = Assert.Single(
                Descendants(form).OfType<Button>(),
                b => b.Text.StartsWith("Use this encoding"));

            Assert.False(apply.Enabled);
        });
    }

    [Fact]
    public void ItSaysWhetherOriginalsWillBeKept()
    {
        // Whether a conversion is undoable is part of what is being approved.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        ConversionPlan withBackups = Plan(backup: true);
        ConversionPlan without = Plan(backup: false);

        OnUiThread(() =>
        {
            using var kept = new ConversionConfirmationForm(withBackups);
            using var lost = new ConversionConfirmationForm(without);

            Assert.Contains(".bak", AllText(kept));
            Assert.Contains("DISABLED", AllText(lost));
        });
    }

    [Fact]
    public void ItReportsThePlanItWasGivenRatherThanRecountingTheDirectory()
    {
        // The dialog must describe the decisions that will execute. Recomputing here is
        // how a confirmation ends up describing something other than what happens.
        Write("jp.txt", "こんにちは世界。日本語のテキストです。", "shift_jis");
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();

        int convert = plan.Files.Count(f => f.Action == PlannedAction.Convert);

        // Change the directory after planning. The dialog must not notice.
        File.Delete(Path.Combine(_root, "jp.txt"));
        Write("late-arrival.txt", "added after the plan", "ascii");

        OnUiThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains($"Convert {convert} of {plan.Files.Count} selected", text);
            Assert.DoesNotContain("late-arrival", text);
        });
    }
}
