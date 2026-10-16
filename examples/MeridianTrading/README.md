# Meridian Trading

A Windows / WinForms / WebView2 reference app using ordinary domain objects and public AbraxasDB APIs. Customers, orders and lines live in separate stores; registered links compose their natural object graphs during hydration. The app demonstrates bounded pages, sorting, conditions, selected-value analysis, editors with stale-form guards, runtime metrics, and a user-visible dataset audit.

Build with `dotnet build examples/MeridianTrading/MeridianTrading.csproj -c Release`. The app consumes the public NuGet package. Precompiled builds require the .NET 8 Windows Desktop runtime (x64) and WebView2 Evergreen runtime. All browser content is local.

Run `MeridianTrading.exe --data "T:\MeridianDemo"` with a dedicated new directory, or use the default under LocalApplicationData. Generate a Smoke or Custom fixture from Runtime metrics. Generation never replaces an existing started dataset. Cancellation preserves completed chunks and does not provide a cross-store rollback or resumable import.

The OSS edition's fresh-corpus generator supports up to 104,857 orders (about 524,282 normalized lines). Standard (200,000 orders) and Large (2,000,000 orders) remain visible as larger-capacity profiles but are unavailable with the OSS library. The UI and backend enforce the loaded library's capacity before generation. Existing datasets may have less remaining identity capacity.

This is a reference application, not a completed commercial trading system. Private command-line verification, browser capture harnesses, experimental diagnostics and benchmarks are not included. The visible runtime metrics and dataset audit are intentional application features.
