[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [switch]$Diagnostic)
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
Get-ChildItem -LiteralPath $packages -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content (Join-Path $packages 'SHA256SUMS.txt')
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
