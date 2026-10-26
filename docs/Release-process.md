# Release process

The public repository began with the reviewed OSS export and clean public history. Preserve that existing public history for updates; never push the private development repository or its history. `scripts/Build-Release.ps1` rejects a nested private export unless explicitly run in non-shipping diagnostic mode.

Before a library release:

1. Verify the public repository, default branch `main`, Apache-2.0 source, private vulnerability reporting and the `release` environment restricted to stable release tags.
2. Keep main protected against deletion and force pushes. Require successful `OSS package and Windows samples` and `Linux core build` checks. The existing Windows check name is retained for branch-rule compatibility; its job now builds and inspects library assets only. An administrator emergency bypass is an exception, not the normal path.
3. Verify the NuGet trusted publishing policy: user **iqueue**, GitHub owner **alt160**, repository **AbraxasDB**, workflow **release.yml**, environment **release**, push scope, exact package glob **AbraxasDB**.
4. Validate a fresh standalone public checkout, package/symbol contents, and package-only consumption on .NET 8/9/10. Windows runtime tests are the initial verified platform evidence. Linux CI compilation is not Linux/macOS durability/runtime acceptance.
5. Review release notes and the version in `src/AbraxasDB/AbraxasDB.csproj`. Commit the accepted public tree and build from that commit. Do not reuse diagnostic packages built below the private checkout.
6. Push the approved stable `v<version>` tag. The workflow checks the tag, builds with a stable SDK, checks AnyCPU and both owned PDBs, attests artifacts, and publishes through NuGet trusted publishing. It refuses blind replacement of existing release assets.

The library GitHub release contains the NuGet package, symbols, precompiled net8.0 library ZIP and SHA-256 checksums. NuGet contains the runtime library/helper and package documentation. Neither distribution contains desktop apps, private test harnesses or commercial implementation.

[Abraxas Showcase](https://github.com/alt160/AbraxasDB.Showcase) and [Meridian Trading](https://github.com/alt160/AbraxasDB.MeridianTrading) own their source, CI and portable ZIP releases separately. Their .NET 10 Windows Desktop/WebView2 prerequisites do not apply to the library build. Existing library releases that included earlier app snapshots remain available.

No commercial website is required for an OSS release; add links only after the site exists.
