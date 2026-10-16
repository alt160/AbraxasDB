# Release process

The public repository must start from the reviewed OSS export with new Git history. Never push the private development repository or its history. `scripts/Build-Release.ps1` rejects a nested private export unless explicitly run in non-shipping diagnostic mode.

Before the first release, configure `alt160/AbraxasDB`:

1. Create/verify the public repository, default branch `main`, Apache-2.0 source, private vulnerability reporting, and a `release` environment restricted to stable release tags.
2. Protect main against deletion and force pushes. Require pull requests and the successful `OSS package and Windows samples` and `Linux core build` checks; an administrator emergency bypass is an explicit exception, not the normal path.
3. On NuGet.org, sign in as **iqueue** and create a trusted publishing policy for GitHub owner **alt160**, repository **AbraxasDB**, workflow **release.yml**, environment **release**, push scope, exact package glob **AbraxasDB**. A BufferStream/Fractal policy does not match a different GitHub repository even if its package glob is expanded.
4. Validate a fresh standalone public checkout, package-only consumption on .NET 8/9/10, and both sample apps. Windows runtime tests are the initial verified platform evidence. Linux CI compilation is not Linux/macOS durability/runtime acceptance.
5. Review release notes and the version in `src/AbraxasDB/AbraxasDB.csproj`. Commit the accepted public tree; build from that commit. Do not reuse diagnostic packages built below the private checkout.
6. Push the approved stable `v<version>` tag. The workflow checks the tag, builds with a stable SDK, checks AnyCPU and both owned PDBs, builds package-only Windows samples, attests artifacts, and publishes through NuGet trusted publishing. It refuses blind replacement of existing release assets.

The GitHub release contains the package, symbols, precompiled net8.0 library, framework-dependent Windows sample apps, and SHA-256 checksums. GitHub's source archives contain only the reviewed public repository. NuGet contains only the runtime library/helper and package documentation—not the apps, test harnesses or commercial implementation.

The sample apps require .NET 8 Windows Desktop (x64) and WebView2 Evergreen. NuGet is the recommended way to resolve the library's dependencies. No commercial website is required for the OSS release; add links only after the site exists.
