# Third-party components

AbraxasDB and the alt160 BufferStream, FractalKVS, Inheto and LibraDex components are Apache-2.0. The adjacent LICENSE contains the Apache-2.0 text. Copyright (c) 2026 alt160.

The precompiled sample apps also redistribute dependencies restored from NuGet. Their exact package metadata, included license texts and notices are retained under `licenses/packages/<package>/<version>/`. Some restore-only dependency notices are included for completeness; this does not imply their assemblies are bundled. A new dependency/version requires review of its redistribution terms before release.

| Component | Version | License / attribution |
| --- | --- | --- |
| fasterflect | 3.0.0 | Apache-2.0; Buu Nguyen, Morten Mertner, Bernard Dijk. https://github.com/buunguyen/fasterflect |
| FastMember | 1.5.0 | Apache-2.0; Copyright Marc Gravell 2012-2016. https://github.com/mgravell/fast-member |
| Standart.Hash.xxHash | 4.0.5 | MIT; Copyright (c) 2018 Melnik Alexander. License retained in `licenses/Standart.Hash.xxHash-4.0.5-LICENSE.txt`. https://github.com/uranium62/xxHash |
| Microsoft.Web.WebView2 | 1.0.4078.44 | The package's LICENSE.txt and NOTICE.txt are retained under `licenses/packages/`. Copyright Microsoft Corporation. |
| Monaco Editor | 0.56.0 | MIT; Microsoft Corporation. The local Showcase bundle includes its LICENSE.txt under Web/vendor/monaco, and an adjacent copy is supplied under `licenses/Monaco-LICENSE.txt` in the release. |

The browser/runtime prerequisites (.NET Windows Desktop and WebView2 Evergreen) are installed separately, not bundled with the framework-dependent apps. The library-only ZIP contains the two owned assemblies, symbols and documentation; use NuGet to resolve its dependencies.
