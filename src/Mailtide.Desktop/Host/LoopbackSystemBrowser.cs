using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Duende.IdentityModel.OidcClient.Browser;

namespace Mailtide.Desktop.Host;

/// <summary>
/// Opens the system browser and captures the OAuth redirect on a loopback HttpListener.
/// The listener is started at construction so the redirect port stays reserved.
/// </summary>
public sealed class LoopbackSystemBrowser : IBrowser, IAsyncDisposable
{
    private readonly HttpListener _listener;
    private bool _disposed;

    public LoopbackSystemBrowser()
    {
        var port = GetFreeTcpPort();
        RedirectUri = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(RedirectUri);
        _listener.Start();
    }

    public string RedirectUri { get; }

    public async Task<BrowserResult> InvokeAsync(
        BrowserOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            OpenSystemBrowser(options.StartUrl);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.Timeout > TimeSpan.Zero)
            {
                linked.CancelAfter(options.Timeout);
            }

            var contextTask = _listener.GetContextAsync();
            var completed = await Task
                .WhenAny(contextTask, Task.Delay(Timeout.Infinite, linked.Token))
                .ConfigureAwait(false);

            if (completed != contextTask)
            {
                return new BrowserResult
                {
                    ResultType = BrowserResultType.Timeout,
                    Error = "Timed out waiting for the OAuth redirect.",
                };
            }

            var context = await contextTask.ConfigureAwait(false);
            var responseUrl = context.Request.Url?.ToString() ?? RedirectUri;

            var html = "<html><body>Mailtide sign-in complete. You can close this window.</body></html>";
            var buffer = System.Text.Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(buffer, linked.Token).ConfigureAwait(false);
            context.Response.OutputStream.Close();

            return new BrowserResult
            {
                ResultType = BrowserResultType.Success,
                Response = responseUrl,
            };
        }
        catch (OperationCanceledException)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UserCancel,
                Error = "OAuth authorization was cancelled.",
            };
        }
        catch (Exception ex)
        {
            return new BrowserResult
            {
                ResultType = BrowserResultType.UnknownError,
                Error = ex.Message,
            };
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        return ValueTask.CompletedTask;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void OpenSystemBrowser(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
            return;
        }

        throw new PlatformNotSupportedException("System browser OAuth is only supported on Windows and Linux.");
    }
}
