using Android.Content;
using Duende.IdentityModel.OidcClient.Browser;
using Application = Android.App.Application;

namespace Mailtide.Android.Host;

/// <summary>
/// Opens the system browser via ACTION_VIEW and completes when MainActivity receives the redirect Intent.
/// </summary>
public sealed class IntentSystemBrowser : IBrowser
{
    public const string RedirectUri = "mailtide://oauth/callback";

    private static TaskCompletionSource<string>? _pending;

    public async Task<BrowserResult> InvokeAsync(
        BrowserOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Interlocked.Exchange(ref _pending, tcs);
        previous?.TrySetCanceled();

        try
        {
            OpenSystemBrowser(options.StartUrl);

            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            if (options.Timeout > TimeSpan.Zero)
            {
                using var timeout = new CancellationTokenSource(options.Timeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
                using var timeoutRegistration = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));
                var responseUrl = await tcs.Task.ConfigureAwait(false);
                return Success(responseUrl);
            }

            var response = await tcs.Task.ConfigureAwait(false);
            return Success(response);
        }
        catch (OperationCanceledException)
        {
            return new BrowserResult
            {
                ResultType = cancellationToken.IsCancellationRequested
                    ? BrowserResultType.UserCancel
                    : BrowserResultType.Timeout,
                Error = cancellationToken.IsCancellationRequested
                    ? "OAuth authorization was cancelled."
                    : "Timed out waiting for the OAuth redirect.",
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
        finally
        {
            Interlocked.CompareExchange(ref _pending, null, tcs);
        }
    }

    public static bool TryComplete(System.Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var pending = _pending;
        return pending is not null && pending.TrySetResult(uri.ToString());
    }

    private static BrowserResult Success(string responseUrl) =>
        new()
        {
            ResultType = BrowserResultType.Success,
            Response = responseUrl,
        };

    private static void OpenSystemBrowser(string url)
    {
        var context = Application.Context
            ?? throw new InvalidOperationException("Android Application.Context is unavailable.");
        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
