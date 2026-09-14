using Android.Content;
using Mailtide.UI;

namespace Mailtide.Android;

/// <summary>
/// Android Host: ACTION_VIEW for http(s)/mailto URIs.
/// </summary>
internal sealed class AndroidOpenExternalUri : IOpenExternalUri
{
    private readonly Context _context;

    public AndroidOpenExternalUri(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.ApplicationContext ?? context;
    }

    public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = global::Android.Net.Uri.Parse(uri.AbsoluteUri)
            ?? throw new InvalidOperationException("Could not open the link.");
        var intent = new Intent(Intent.ActionView, parsed);
        intent.AddFlags(ActivityFlags.NewTask);
        _context.StartActivity(intent);
        return Task.CompletedTask;
    }
}
