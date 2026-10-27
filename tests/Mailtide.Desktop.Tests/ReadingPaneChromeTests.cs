namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class ReadingPaneChromeTests
{
    [TestMethod]
    public void Conversation_card_has_no_persistent_object_buttons()
    {
        var axaml = ReadShellAxaml();
        var templateStart = axaml.IndexOf("x:Key=\"ConversationCardTemplate\"", StringComparison.Ordinal);
        var templateEnd = axaml.IndexOf("</DataTemplate>", templateStart, StringComparison.Ordinal);
        Assert.IsTrue(templateStart >= 0 && templateEnd > templateStart);
        var template = axaml[templateStart..templateEnd];
        Assert.DoesNotContain("<Button", template, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowActions", template, StringComparison.Ordinal);

        var readingMenu = axaml.IndexOf("Name=\"ReadingMoreButton\"", StringComparison.Ordinal);
        Assert.IsTrue(readingMenu > templateEnd);
        Assert.Contains("<MenuFlyout", axaml[readingMenu..], StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Reply\"", axaml, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Conversation_card_carries_shared_context_menu_and_no_attachment_ui()
    {
        var axaml = ReadShellAxaml();
        var templateStart = axaml.IndexOf("x:Key=\"ConversationCardTemplate\"", StringComparison.Ordinal);
        var templateEnd = axaml.IndexOf("</DataTemplate>", templateStart, StringComparison.Ordinal);
        var template = axaml[templateStart..templateEnd];

        Assert.Contains("OnConversationCardMenuOpening", template, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAttachmentClip", template, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAttachmentChips", template, StringComparison.Ordinal);
        Assert.DoesNotContain("OnConversationAttachment", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Attachments}", template, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Thread_row_marks_are_passive_and_strip_opens_on_single_tap()
    {
        var axaml = ReadShellAxaml();
        Assert.DoesNotContain("OnThreadFlagPressed", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnThreadAttachmentClipPressed", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("OnThreadAttachmentDoubleTapped", axaml, StringComparison.Ordinal);

        var stripStart = axaml.IndexOf("Name=\"ThreadAttachmentsList\"", StringComparison.Ordinal);
        var stripEnd = axaml.IndexOf("</ListBox>", stripStart, StringComparison.Ordinal);
        Assert.IsTrue(stripStart >= 0 && stripEnd > stripStart);
        var strip = axaml[stripStart..stripEnd];
        Assert.Contains("Tapped=\"OnThreadAttachmentTapped\"", strip, StringComparison.Ordinal);
        Assert.Contains("Header=\"Open\"", strip, StringComparison.Ordinal);
        Assert.Contains("Header=\"Save As\"", strip, StringComparison.Ordinal);
        Assert.Contains("Header=\"Save All\"", strip, StringComparison.Ordinal);
    }

    private static string ReadShellAxaml() => File.ReadAllText(
        Path.Combine(FindRepoRoot(), "src", "Mailtide.UI", "MailShellView.axaml"));

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
