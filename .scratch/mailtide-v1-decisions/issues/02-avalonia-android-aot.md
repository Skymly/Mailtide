# Avalonia 12 + .NET 10 on Android (AOT / trimming)

Part of: [Mailtide v1 — decision map](../map.md)

Type: research  
Status: resolved

## Question

What is the current, primary-source status of Avalonia 12 on **Android** with .NET 10, including Native AOT / trimming / linker expectations? Can the same working-hypothesis stack serve Android, or do official docs imply a different publish mode? Cited fact sheet only — no stack decision in this ticket.

## Answer

Same Avalonia 12 + .NET 10 UI stack can target Android via a separate Android head, but **publish/compilation mode differs from desktop Native AOT**: Avalonia Android docs use APK/AAB + `PublishTrimmed`; Microsoft marks Android `PublishAot` as experimental / not production (no built-in Java interop); production path is Mono AOT (`RunAOTCompilation`) + partial trimming.

Full cited fact sheet: [research/02-avalonia-android-aot.md](../research/02-avalonia-android-aot.md)
