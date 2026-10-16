# Abraxas Showcase

A Windows / WinForms / WebView2 walkthrough of public AbraxasDB APIs. The app contains runnable OSS lessons, complete copyable C# programs, graph inspection, indexes, selected-value reads, and memory-backed index examples.

Build with `dotnet build examples/AbraxasShowcase/AbraxasShowcase.csproj -c Release`. This project consumes the public AbraxasDB NuGet package, not a private engine checkout. Precompiled releases require the .NET 8 Windows Desktop runtime (x64) and WebView2 Evergreen runtime. Web content is bundled locally, including the pinned Monaco bundle and its license.

Run `AbraxasShowcase.exe --data "T:\AbraxasShowcaseDemo"` to choose a dedicated laboratory. Group setup can replace data inside its app-owned laboratory after confirmation; do not use an application database as a laboratory. The default laboratory is under the current user's LocalApplicationData directory.

Commercial feature examples remain readable. OSS execution shows an edition notice and does not run commercial engine code. Private verification, named-pipe UI probes, profiling tools and commercial implementations are excluded from this distribution.

`--export-examples --output "T:\AbraxasExamples"` writes standalone example programs to a chosen new output folder. Commercial-only examples are marked as requiring the separate commercial edition.
