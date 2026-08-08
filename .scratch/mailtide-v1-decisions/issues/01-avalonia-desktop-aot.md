# Avalonia 12 + .NET 10 Native AOT on Windows/Linux

Part of: [Mailtide v1 — decision map](../map.md)

Type: research  
Status: resolved

## Question

What is the current, primary-source status of shipping an Avalonia 12 app with .NET 10 Native AOT on **Windows** and **Linux**? Cover publish/trim/AOT flags, documented limitations, known breakages (reflection, XAML, DI), and what “supported” means for desktop targets. Produce a cited fact sheet — do not choose Mailtide’s stack here.

## Answer

Desktop Native AOT is a documented Avalonia 12 publish path on .NET 10; Microsoft lists Windows and Linux RIDs as supported (not experimental). Ship with `PublishAot` + trim/self-contained per RID; hard constraints include compiled bindings/XAML, no reflection ViewLocator, compile-time DI. Win accessibility moved to source-generated COM for .NET 8+ AOT — still verify on real publishes.

Full cited fact sheet: [research/01-avalonia-desktop-aot.md](../research/01-avalonia-desktop-aot.md)
