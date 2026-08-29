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

    private void Write(string name, string text, string charset) =>
        File.WriteAllBytes(
            Path.Combine(_root, name), Encoding.GetEncoding(charset).GetBytes(text));

    /// <summary>A plan over whatever is currently in the directory.</summary>
    private ConversionPlan Plan(bool backup = true, string target = "utf-8")
    {
        var entries = new EntrySink();

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

        UiTest.OnStaThread(() =>
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

        UiTest.OnStaThread(() =>
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

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains("require their source encoding to be identified or confirmed", text);
            Assert.Contains("Nothing ready to convert", text);
            Assert.DoesNotContain("Convert 1 file", text);
        });
    }

    [Fact]
    public void ItNamesTheDetectedLegacyEncodingAndOffersAnExplicitChoice()
    {
        // "Could not be determined" on its own gives a user nothing to act on. The
        // alternatives and the way out are what make the refusal actionable.
        Write("ambiguous.txt", "Le café était déjà prêt", "windows-1252");

        ConversionPlan plan = Plan();
        PlannedFile refused = Assert.Single(plan.Files);

        UiTest.OnStaThread(() =>
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
            Assert.Contains(refused.SourceEncoding, cells);

            // And the way out is offered, populated from the charsets EC supports.
            ComboBox chooser = Assert.Single(Descendants(form).OfType<ComboBox>());

            Assert.True(chooser.Items.Count > 1);
            Assert.Contains("windows-1252", chooser.Items.Cast<string>());
            Assert.True(chooser.Width >= 235, "the full source-encoding prompt must be visible");
        });
    }

    [Fact]
    public void ItExposesTheReviewControlsToKeyboardAndAssistiveTechnology()
    {
        Write("legacy.txt", "Le caf\u00e9 \u20ac", "windows-1252");

        ConversionPlan plan = Plan();

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());
            ComboBox source = Assert.Single(Descendants(form).OfType<ComboBox>());
            Button confirmSource = Assert.Single(
                Descendants(form).OfType<Button>(),
                button => button.Text.StartsWith("Confirm for"));

            Assert.Equal("Files requiring source encoding", list.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(list.AccessibleDescription));
            Assert.Equal("Source encoding for selected legacy files", source.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(source.AccessibleDescription));
            Assert.Equal("Confirm selected source encoding", confirmSource.AccessibleName);
            Assert.False(string.IsNullOrWhiteSpace(confirmSource.AccessibleDescription));

            // Enter and Escape retain their conventional, safe meanings.
            Assert.IsType<Button>(form.AcceptButton);
            Assert.IsType<Button>(form.CancelButton);
            Assert.Equal(DialogResult.Cancel, ((Button)form.CancelButton!).DialogResult);
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

        UiTest.OnStaThread(() =>
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
            Assert.Contains("Confirm for 2 file(s)", AllText(form));

            list.Items[0].Checked = false;

            Assert.Contains("Confirm for 1 file(s)", AllText(form));

            // And with none ticked there is nothing to apply, so it cannot be pressed.
            list.Items[1].Checked = false;

            Button apply = Assert.Single(
                Descendants(form).OfType<Button>(),
                b => b.Text.StartsWith("Confirm for"));

            Assert.False(apply.Enabled);
            Assert.True(apply.Width >= 230, "the source-confirmation button must stay easy to read and click");
        });
    }

    [Fact]
    public void ItOffersEveryLegacyFileNamedInTheReview()
    {
        // A large batch used to show the true total in the explanation but silently
        // truncated the selectable list at 200 files.
        PlannedFile[] files = Enumerable.Range(1, 310)
            .Select(i => new PlannedFile
            {
                RelativePath = $"legacy-{i:D3}.txt",
                Size = 1,
                Sha256 = new string('0', 64),
                Action = PlannedAction.Refuse,
                SourceEncoding = "windows-1252",
                SourceCodePage = 1252,
                SourceHasBom = false,
                SourceWasSpecified = false,
                SourceInterpretation = SourceInterpretation.LegacyNeedsSourceChoice,
            })
            .ToArray();

        var plan = new ConversionPlan
        {
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            EcVersion = "test",
            BaseDirectory = _root,
            TargetEncoding = "utf-8",
            TargetHasBom = false,
            BackupEnabled = true,
            Files = files,
        };

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);
            form.CreateControl();
            _ = form.Handle;

            ListView list = Assert.Single(Descendants(form).OfType<ListView>());
            list.CreateControl();
            _ = list.Handle;

            Assert.Equal(310, list.Items.Count);
            Assert.Equal(310, list.CheckedItems.Count);
            Assert.Contains(
                "310 file(s) require their source encoding to be identified or confirmed",
                AllText(form));
            Assert.Contains("Confirm for 310 file(s)", AllText(form));
        });
    }

    [Fact]
    public void ItSaysWhetherOriginalsWillBeKept()
    {
        // Whether a conversion is undoable is part of what is being approved.
        Write("jp.txt", "こんにちは世界。テキスト", "shift_jis");

        ConversionPlan withBackups = Plan(backup: true);
        ConversionPlan without = Plan(backup: false);

        UiTest.OnStaThread(() =>
        {
            using var kept = new ConversionConfirmationForm(withBackups);
            using var lost = new ConversionConfirmationForm(without);

            Assert.Contains(".bak", AllText(kept));
            Assert.Contains("OFF", AllText(lost));
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

        UiTest.OnStaThread(() =>
        {
            using var form = new ConversionConfirmationForm(plan);

            string text = AllText(form);

            Assert.Contains($"{plan.Files.Count} selected file(s). Target:", text);
            Assert.DoesNotContain("late-arrival", text);
        });
    }
}
