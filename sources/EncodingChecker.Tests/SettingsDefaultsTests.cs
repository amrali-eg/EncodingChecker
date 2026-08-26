using System.Text;
using System.Xml.Serialization;

namespace EncodingChecker.Tests;

/// <summary>
/// Pins the startup defaults and their round trip through the settings file.
///
/// These are the values a first run gets, and they are easy to break silently:
/// the form applies them only if it actually reaches the code that does so, and
/// a missing settings file used to skip that entirely - which left
/// <see cref="Settings.IncludeSubdirectories"/> documented as <c>true</c> while
/// the checkbox came up unchecked on a first run.
/// </summary>
public sealed class SettingsDefaultsTests
{
    [Fact]
    public void CreateBackup_DefaultsToOn()
    {
        // Conversion from a Unicode or ASCII source preserved every one of 1,832
        // files in the corpus audit, but roughly one in five converted from a
        // legacy code page came out with different text. That is usually
        // reversible, and only for someone who still knows which codec was used.
        // Defaulting the backup on keeps the original recoverable without it.
        Assert.True(new Settings().CreateBackup);
    }

    [Fact]
    public void IncludeSubdirectories_DefaultsToOn()
    {
        Assert.True(new Settings().IncludeSubdirectories);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateBackup_SurvivesTheSettingsRoundTrip(bool value)
    {
        // The choice is persisted, so a user who turns backups off - or leaves
        // them on - does not have to make the decision again on every launch.
        var original = new Settings { CreateBackup = value };

        var serializer = new XmlSerializer(typeof(Settings));
        using var buffer = new MemoryStream();
        serializer.Serialize(buffer, original);

        buffer.Position = 0;
        var restored = (Settings)serializer.Deserialize(buffer)!;

        Assert.Equal(value, restored.CreateBackup);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeThisOptionExisted_StillLoads()
    {
        // Older installs have a settings file with no CreateBackup element.
        // XmlSerializer leaves absent elements at their field initializer, so
        // such a file has to come back with the option on rather than off.
        const string legacy = """
            <?xml version="1.0"?>
            <Settings xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <WindowPosition><Left>-1</Left><Top>-1</Top><Width>-1</Width><Height>-1</Height></WindowPosition>
              <RecentDirectories />
              <IncludeSubdirectories>true</IncludeSubdirectories>
              <FileMasks />
              <ValidCharsets />
            </Settings>
            """;

        var serializer = new XmlSerializer(typeof(Settings));
        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(legacy));
        var restored = (Settings)serializer.Deserialize(buffer)!;

        Assert.True(restored.CreateBackup);
        Assert.True(restored.IncludeSubdirectories);
    }
}
