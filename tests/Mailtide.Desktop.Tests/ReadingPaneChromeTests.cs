namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class ReadingPaneChromeTests
{
    [TestMethod]
    public void Conversation_card_has_no_persistent_object_buttons()
    {
        var axaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Mailtide.UI", "MailShellView.axaml"));
        var templateStart = axaml.IndexOf("x:Key=\"ConversationCardTemplate\"", StringComparison.Ordinal);
        var templateEnd = axaml.IndexOf("</DataTemplate>", templateStart, StringComparison.Ordinal);
        Assert.IsTrue(templateStart >= 0 && templateEnd > templateStart);
        var template = axaml[templateStart..templateEnd];
        Assert.DoesNotContain("<Button", template, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowActions", template, StringComparison.Ordinal);

        var readingMenu = axaml.IndexOf("Name=\"ReadingMoreButton\"", StringComparison.Ordinal);
        Assert.IsTrue(readingMenu > templateEnd);
        Assert.Contains("Header=\"Reply\"", axaml[readingMenu..], StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Mailtide.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate repo root.");
        return null!;
    }
}
