[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [string]$Directory = 'artifacts/packages', [string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.Reflection.Metadata
$directoryPath = [IO.Path]::GetFullPath($Directory, (Get-Location).Path)
foreach ($extension in @('nupkg','snupkg')) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $directoryPath "AbraxasDB.$Version.$extension"))
    try {
        $entries = @($archive.Entries.FullName)
        $required = if ($extension -eq 'nupkg') { @('lib/net8.0/AbraxasDB.dll','lib/net8.0/AbraxasDB.xml','lib/net8.0/AbxIdentityGenerator.dll','README.md','LICENSE','CHANGELOG.md') }
                    else { @('lib/net8.0/AbraxasDB.pdb','lib/net8.0/AbxIdentityGenerator.pdb') }
        foreach ($entry in $required) { if ($entry -notin $entries) { throw "Missing $extension entry: $entry" } }
        foreach ($entry in $entries) {
            if ($entry -notin $required -and $entry -notin @('_rels/.rels','AbraxasDB.nuspec','[Content_Types].xml') -and $entry -notmatch '^package/services/metadata/core-properties/[^/]+\.psmdcp$') { throw "Unreviewed package entry: $entry" }
        }
        $reader = [IO.StreamReader]::new($archive.GetEntry('AbraxasDB.nuspec').Open())
        try { [xml]$metadata = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($metadata.package.metadata.id -cne 'AbraxasDB' -or $metadata.package.metadata.version -cne $Version) { throw 'Package identity/version mismatch.' }
        if ($ExpectedCommit -and $metadata.package.metadata.repository.commit -cne $ExpectedCommit) { throw 'Package provenance does not match the public commit.' }
        if ($extension -eq 'nupkg') {
            foreach ($name in @('AbraxasDB','AbxIdentityGenerator')) {
                $stream = [IO.MemoryStream]::new()
                $inputStream = $archive.GetEntry("lib/net8.0/$name.dll").Open()
                try { $inputStream.CopyTo($stream) } finally { $inputStream.Dispose() }
                $stream.Position = 0
                $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
                try {
                    if ([string]$pe.PEHeaders.CoffHeader.Machine -ne 'I386' -or ([int]$pe.PEHeaders.CorHeader.Flags -band 3) -ne 1) { throw "$name must be AnyCPU / ILOnly, not an architecture-specific library." }
                } finally { $pe.Dispose(); $stream.Dispose() }
            }
        }
    } finally { $archive.Dispose() }
}
Write-Output "RELEASE_ASSETS_PASS version=$Version package-and-symbols=verified AnyCPU=verified"
