using Mailtide.Desktop;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopNotifyInboxArrivalTests
{
    [TestMethod]
    public async Task ShowAsync_invokes_OS_show_boundary()
    {
        InboxArrivalNotification? shown = null;
        var notifier = new DesktopNotifyInboxArrival(show: n => shown = n);
        var notification = Sample();

        await notifier.ShowAsync(notification);

        Assert.AreEqual(notification, shown);
    }

    [TestMethod]
    public void Activate_invokes_activation_boundary()
    {
        InboxArrivalNotification? activated = null;
        var notifier = new DesktopNotifyInboxArrival(onActivated: n => activated = n);
        var notification = Sample();

        notifier.Activate(notification);

        Assert.AreEqual(notification, activated);
    }

    [TestMethod]
    public void CreateLinuxNotifyStartInfo_uses_notify_send_with_title_and_body()
    {
        var startInfo = DesktopNotifyInboxArrival.CreateLinuxNotifyStartInfo(
            "Hello",
            "bob@example.com");

        Assert.AreEqual("notify-send", startInfo.FileName);
        Assert.IsFalse(startInfo.UseShellExecute);
        CollectionAssert.AreEqual(
            new[] { "--app-name=Mailtide", "Hello", "bob@example.com" },
            startInfo.ArgumentList.ToArray());
    }

    [TestMethod]
    public void CreateWindowsToastStartInfo_embeds_escaped_title_and_body()
    {
        var startInfo = DesktopNotifyInboxArrival.CreateWindowsToastStartInfo(
            "A & B <C>",
            "bob@example.com");

        Assert.AreEqual("powershell", startInfo.FileName);
        Assert.IsFalse(startInfo.UseShellExecute);
        var command = startInfo.ArgumentList[^1];
        Assert.Contains("A &amp; B &lt;C&gt;", command, StringComparison.Ordinal);
        Assert.Contains("bob@example.com", command, StringComparison.Ordinal);
        Assert.Contains("ToastNotificationManager", command, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Program_wires_NotifyInboxArrival_host_port()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var program = Path.Combine(dir.FullName, "src", "Mailtide.Desktop", "Program.cs");
            if (File.Exists(program))
            {
                var text = File.ReadAllText(program);
                Assert.Contains("HostBootstrap.NotifyInboxArrival", text, StringComparison.Ordinal);
                Assert.Contains("DesktopNotifyInboxArrival", text, StringComparison.Ordinal);
                return;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Desktop Program.cs");
    }

    private static InboxArrivalNotification Sample() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Just arrived",
            "carol@example.com",
            "Personal");
}