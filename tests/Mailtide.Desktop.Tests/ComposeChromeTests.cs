namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class ComposeChromeTests
{
    [TestMethod]
    public void Compose_footer_contains_only_send_and_discard()
    {
        var axaml = ReadShellAxaml();
        var sendIndex = axaml.IndexOf("Name=\"SendDraftButton\"", StringComparison.Ordinal);
        Assert.IsTrue(sendIndex >= 0);

        var footerStart = axaml.LastIndexOf(
            "<StackPanel DockPanel.Dock=\"Bottom\"", sendIndex, StringComparison.Ordinal);
        var footerEnd = axaml.IndexOf("</StackPanel>", sendIndex, StringComparison.Ordinal);
        Assert.IsTrue(footerStart >= 0 && footerEnd > footerStart);

        var footer = axaml[footerStart..footerEnd];
        Assert.Contains("Name=\"SendDraftButton\"", footer, StringComparison.Ordinal);
        Assert.Contains("Name=\"DiscardDraftButton\"", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachDraftButton", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveDraftAttachmentButton", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove Attachment", footer, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Attach_lives_on_the_compose_header()
    {
        var axaml = ReadShellAxaml();
        var composeStart = axaml.IndexOf("Name=\"ComposeSurface\"", StringComparison.Ordinal);
        var attachIndex = axaml.IndexOf("Name=\"AttachDraftButton\"", StringComparison.Ordinal);
        var footerIndex = axaml.IndexOf(
            "DockPanel.Dock=\"Bottom\"", composeStart, StringComparison.Ordinal);
        Assert.IsTrue(composeStart >= 0 && attachIndex > composeStart);
        Assert.IsTrue(attachIndex < footerIndex, "Attach must sit inside the compose header, above the footer.");
        Assert.Contains("OnAttachDraftClick", axaml[attachIndex..footerIndex], StringComparison.Ordinal);
    }

    [TestMethod]
    public void Each_draft_attachment_row_offers_remove()
    {
        var axaml = ReadShellAxaml();
        var listIndex = axaml.IndexOf("Name=\"DraftAttachmentsList\"", StringComparison.Ordinal);
        var templateEnd = axaml.IndexOf("</DataTemplate>", listIndex, StringComparison.Ordinal);
        Assert.IsTrue(listIndex >= 0 && templateEnd > listIndex);

        var template = axaml[listIndex..templateEnd];
        Assert.Contains("<Button", template, StringComparison.Ordinal);
        Assert.Contains("OnRemoveDraftAttachmentClick", template, StringComparison.Ordinal);
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
