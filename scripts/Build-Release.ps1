[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [switch]$BuildSamples, [switch]$Diagnostic)
$ErrorActionPreference = 'Stop'
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
Set-Location -LiteralPath $root
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Only stable three-part versions are supported.' }
[xml]$project = Get-Content src/AbraxasDB/AbraxasDB.csproj -Raw
if ($Version -cne [string]$project.Project.PropertyGroup.Version) { throw 'Requested and project versions differ.' }
$commit = ''
if (-not $Diagnostic) {
    $gitRoot = (& git rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne $root) { throw 'Build from a standalone public checkout, never a nested private export.' }
    $commit = (& git rev-parse HEAD).Trim()
    if ($env:GITHUB_SHA -and $commit -cne $env:GITHUB_SHA) { throw 'Public checkout commit mismatch.' }
}
$output = Join-Path $root 'artifacts/build'
$packages = Join-Path $root 'artifacts/packages'
$cache = Join-Path $root 'artifacts/nuget-cache'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
& dotnet build AbraxasDB.sln -c Release '-p:Platform=Any CPU' --artifacts-path $output "-p:RestorePackagesPath=$cache" "-p:RepositoryCommit=$commit" -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
& dotnet pack src/AbraxasDB/AbraxasDB.csproj -c Release --no-build --no-restore -p:BuildProjectReferences=false -p:Platform=AnyCPU --artifacts-path $output -o $packages "-p:RestorePackagesPath=$cache" "-p:RepositoryCommit=$commit" -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Package build failed.' }
& (Join-Path $PSScriptRoot 'Test-ReleaseAssets.ps1') -Version $Version -Directory $packages -ExpectedCommit $commit
$library = Join-Path $root 'artifacts/library'
New-Item -ItemType Directory -Path $library -Force | Out-Null
Copy-Item -Path (Join-Path $output 'bin/AbraxasDB/release/*') -Destination $library
Copy-Item -LiteralPath 'README.md','LICENSE','CHANGELOG.md' -Destination $library
Compress-Archive -Path "$library/*" -DestinationPath (Join-Path $packages "AbraxasDB.$Version.net8.0.zip")
if ($BuildSamples) {
    if (-not $IsWindows) { throw 'Windows sample binaries must be built on Windows.' }
    $configuration = [xml]'<configuration><packageSources><clear/><add key="candidate"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="AbraxasDB"/></packageSource><packageSource key="nuget.org"><package pattern="*"/></packageSource></packageSourceMapping></configuration>'
    $configuration.configuration.packageSources.add[0].SetAttribute('value', $packages)
    $candidateConfig = Join-Path $root 'artifacts/NuGet.candidate.config'
    $configuration.Save($candidateConfig)
    foreach ($app in @('AbraxasShowcase','MeridianTrading')) {
        $destination = Join-Path $root "artifacts/apps/$app"
        & dotnet publish "examples/$app/$app.csproj" -c Release -r win-x64 --self-contained false --artifacts-path $output -o $destination "-p:AbraxasPackageVersion=$Version" "-p:RestoreConfigFile=$candidateConfig" "-p:RestorePackagesPath=$cache" -warnaserror
        if ($LASTEXITCODE -ne 0) { throw "$app publish failed." }
        Copy-Item -LiteralPath 'LICENSE','CHANGELOG.md' -Destination $destination
        Copy-Item -LiteralPath "examples/$app/README.md" -Destination (Join-Path $destination 'README.md')
        Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $destination
        Copy-Item -LiteralPath 'licenses' -Destination $destination -Recurse
        $licenseRoot = Join-Path $destination 'licenses/packages'
        foreach ($packageMetadata in Get-ChildItem -LiteralPath $cache -Filter '*.nuspec' -Recurse -File) {
            [xml]$packageInfo = Get-Content -LiteralPath $packageMetadata.FullName -Raw
            $licenseDirectory = Join-Path $licenseRoot "$($packageInfo.package.metadata.id)/$($packageInfo.package.metadata.version)"
            New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null
            Copy-Item -LiteralPath $packageMetadata.FullName -Destination $licenseDirectory
            foreach ($notice in Get-ChildItem -LiteralPath $packageMetadata.DirectoryName -File -Recurse | Where-Object { $_.Name -match '(?i)license|notice' }) {
                $relativeNotice = [IO.Path]::GetRelativePath($packageMetadata.DirectoryName, $notice.FullName)
                $noticeTarget = Join-Path $licenseDirectory $relativeNotice
                New-Item -ItemType Directory -Path (Split-Path -Parent $noticeTarget) -Force | Out-Null
                Copy-Item -LiteralPath $notice.FullName -Destination $noticeTarget
            }
        }
        Compress-Archive -Path "$destination/*" -DestinationPath (Join-Path $packages "$app.$Version.win-x64.zip")
    }
}
Get-ChildItem -LiteralPath $packages -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content (Join-Path $packages 'SHA256SUMS.txt')
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
