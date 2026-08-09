using Mailtide.Core;

namespace Mailtide.UI;

/// <summary>
/// Host-provided composition hook so shared UI never references Desktop/Android types.
/// </summary>
public static class HostBootstrap
{
    public static Func<CancellationToken, Task<MailtideApp>>? OpenCoreAsync { get; set; }
}
