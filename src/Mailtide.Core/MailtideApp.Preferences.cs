using Mailtide.Core.Store;
using Microsoft.EntityFrameworkCore;

namespace Mailtide.Core;

public sealed partial class MailtideApp
{
    public async Task<string?> GetPreferenceAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Preferences
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Key == key, cancellationToken)
                .ConfigureAwait(false);
            return record?.Value;
        }
        finally
        {
            _dbGate.Release();
        }
    }

    public async Task SetPreferenceAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        await _dbGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await _db.Preferences
                .SingleOrDefaultAsync(item => item.Key == key, cancellationToken)
                .ConfigureAwait(false);
            if (record is null)
            {
                _db.Preferences.Add(new PreferenceRecord { Key = key, Value = value });
            }
            else
            {
                record.Value = value;
            }

            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dbGate.Release();
        }
    }
}
