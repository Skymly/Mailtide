using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mailtide.Core.Outbox;

internal static class OutboxAttemptLog
{
    public static string Encode(IReadOnlyList<OutboxAttemptInfo> attempts) =>
        JsonSerializer.Serialize(attempts.Select(ToEntry).ToArray());

    public static IReadOnlyList<OutboxAttemptInfo> Decode(string? encoded) =>
        string.IsNullOrWhiteSpace(encoded)
            ? []
            : (JsonSerializer.Deserialize<Entry[]>(encoded) ?? [])
                .Select(entry => new OutboxAttemptInfo(entry.At, entry.Kind, entry.Error))
                .ToList();

    public static string Append(string? encoded, OutboxAttemptInfo attempt) =>
        Encode([..Decode(encoded), attempt]);

    private static Entry ToEntry(OutboxAttemptInfo attempt) =>
        new() { At = attempt.At, Kind = attempt.Kind, Error = attempt.Error };

    private sealed class Entry
    {
        public DateTimeOffset At { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter<OutboxAttemptKind>))]
        public OutboxAttemptKind Kind { get; set; }

        public string? Error { get; set; }
    }
}
