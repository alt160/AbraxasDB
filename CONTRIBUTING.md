# Contributing

Issues and focused pull requests are welcome. Describe the behavior you expect, the behavior you observe, the package/runtime/OS versions, and a small reproduction using synthetic data. Do not upload private datasets or credentials.

Build the core with `dotnet build AbraxasDB.sln -c Release`. The library targets .NET 8 and can be consumed by later compatible .NET runtimes. The two optional Windows sample apps live under `examples/` and build against the AbraxasDB package; they are not part of the NuGet library.

Please keep changes scoped. Low developer friction, predictable behavior, low allocation pressure, and shallow hot paths are design goals. Explain compatibility, concurrency, persistence-format and performance implications where relevant. Do not add commercial implementation or private test/profiling tools to this OSS source distribution.

See [the guides](docs/wiki/Home.md) and [security policy](SECURITY.md). Contributions are licensed under Apache-2.0.
