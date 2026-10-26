# Contributing

Issues and focused pull requests are welcome. Describe the behavior you expect, the behavior you observe, the package/runtime/OS versions, and a small reproduction using synthetic data. Do not upload private datasets or credentials.

Build the core with `dotnet build AbraxasDB.sln -c Release`. The library targets .NET 8 and can be consumed by later compatible .NET runtimes. The optional [Showcase](https://github.com/alt160/AbraxasDB.Showcase) and [Meridian Trading](https://github.com/alt160/AbraxasDB.MeridianTrading) apps have separate source repositories and build against the public NuGet package; this checkout does not build or publish them.

Please keep changes scoped. Low developer friction, predictable behavior, low allocation pressure, and shallow hot paths are design goals. Explain compatibility, concurrency, persistence-format and performance implications where relevant. Do not add commercial implementation or private test/profiling tools to this OSS source distribution.

See [the guides](docs/wiki/Home.md) and [security policy](SECURITY.md). Contributions are licensed under Apache-2.0.
