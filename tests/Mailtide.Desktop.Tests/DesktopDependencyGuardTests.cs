using System.Reflection;
using System.Xml.Linq;
using Mailtide.Core;
using Mailtide.Core.Imap;
using Mailtide.Core.Smtp;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class DesktopDependencyGuardTests
{
    [TestMethod]
    public void BrowseShell_only_accepts_MailtideApp()
    {
        AssertShellOnlyAcceptsMailtideApp(typeof(BrowseShell));
    }

    [TestMethod]
    public void ComposeOutboxShell_only_accepts_MailtideApp()
    {
        AssertShellOnlyAcceptsMailtideApp(typeof(ComposeOutboxShell));
    }

    [TestMethod]
    public void BrowseShell_public_API_does_not_mention_IMAP_or_SMTP_ports()
    {
        AssertShellDoesNotExposeProtocolPorts(typeof(BrowseShell));
    }

    [TestMethod]
    public void ComposeOutboxShell_public_API_does_not_mention_IMAP_or_SMTP_ports()
    {
        AssertShellDoesNotExposeProtocolPorts(typeof(ComposeOutboxShell));
    }

    [TestMethod]
    public void Desktop_project_does_not_reference_MailKit_packages()
    {
        var csprojPath = FindDesktopCsproj();
        var document = XDocument.Load(csprojPath);
        var packageIds = document
            .Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? e.Element("Include")?.Value)
            .Where(id => id is not null)
            .ToList();

        Assert.IsFalse(
            packageIds.Any(id => id!.Contains("MailKit", StringComparison.OrdinalIgnoreCase)),
            "Desktop must not take a direct MailKit dependency.");
        Assert.IsFalse(
            packageIds.Any(id => id!.Contains("MimeKit", StringComparison.OrdinalIgnoreCase)),
            "Desktop must not take a direct MimeKit dependency.");
    }

    private static void AssertShellOnlyAcceptsMailtideApp(Type shellType)
    {
        var constructors = shellType.GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        Assert.HasCount(1, constructors);

        var parameters = constructors[0].GetParameters();
        Assert.HasCount(1, parameters);
        Assert.AreEqual(typeof(MailtideApp), parameters[0].ParameterType);
    }

    private static void AssertShellDoesNotExposeProtocolPorts(Type shellType)
    {
        var forbidden = new HashSet<Type>
        {
            typeof(IImapClient),
            typeof(IImapClientFactory),
            typeof(ISmtpClient),
            typeof(ISmtpClientFactory),
        };

        foreach (var member in shellType.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            foreach (var type in TypesOf(member))
            {
                Assert.IsFalse(
                    forbidden.Contains(type),
                    $"{shellType.Name}.{member.Name} exposes protocol type {type.Name}");
            }
        }
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member) =>
        member switch
        {
            MethodInfo method => method.GetParameters()
                .Select(p => p.ParameterType)
                .Append(method.ReturnType)
                .SelectMany(Unwrap),
            PropertyInfo property => Unwrap(property.PropertyType),
            ConstructorInfo ctor => ctor.GetParameters().Select(p => p.ParameterType).SelectMany(Unwrap),
            _ => [],
        };

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                foreach (var nested in Unwrap(arg))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string FindDesktopCsproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Mailtide.Desktop", "Mailtide.Desktop.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        Assert.Fail("Could not locate Mailtide.Desktop.csproj from the test output directory.");
        return null!;
    }
}
