using System.Reflection;
using System.Security.Cryptography;

namespace AbraxasShowcase;

/// <summary>
/// Resolves the Showcase's file-backed development UI or restores the embedded release UI into a verified local cache.<br/>
/// A normal local Release build remains file-backed; only builds made with ShowcaseEmbedWebAssets=true carry embedded web assets.<br/>
/// </summary>
internal static class ShowcaseWebAssets
{
    private const string ResourcePrefix = "showcase.web/";

    /// <summary>
    /// Returns the folder that WebView2 should map to showcase.local.<br/>
    /// In an ordinary build this is the copied Web folder beside the executable.<br/>
    /// In a packaged build each embedded resource is checked against its cache copy and any missing or corrupt copy is atomically restored before navigation.<br/>
    /// The cache is versioned by the executing module identity, so a newer executable never reuses an older build's web files.<br/>
    /// </summary>
    /// <returns>The resolved folder, backing mode, asset count, and number of files repaired during this call.<br/></returns>
    internal static ShowcaseWebAssetState Resolve()
    {
        Assembly assembly = typeof(ShowcaseWebAssets).Assembly;
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
        {
            throw new InvalidDataException("This packaged Showcase build contains no embedded web assets.");
        }

        string root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbraxasShowcase", "web-cache", assembly.ManifestModule.ModuleVersionId.ToString("N")));
        string rootPrefix = root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        int repaired = 0;
        foreach (string resourceName in resources)
        {
            string relative = resourceName[ResourcePrefix.Length..].Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException($"Invalid packaged Showcase asset path: {resourceName}");
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Packaged Showcase asset leaves its cache: {resourceName}");
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidDataException($"Packaged Showcase asset is unreadable: {resourceName}");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] expected = buffer.ToArray();
            byte[] expectedHash = SHA256.HashData(expected);
            if (File.Exists(target) && SHA256.HashData(File.ReadAllBytes(target)).AsSpan().SequenceEqual(expectedHash))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + ".repair-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, expected);
                if (!SHA256.HashData(File.ReadAllBytes(temporary)).AsSpan().SequenceEqual(expectedHash))
                    throw new IOException($"Could not verify restored Showcase asset: {relative}");
                File.Move(temporary, target, overwrite: true);
                repaired++;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidDataException("The packaged Showcase UI has no index.html resource.");
        return new ShowcaseWebAssetState(root, "embedded-cache", resources.Length, repaired);
    }
}

/// <summary>Reports which web backing a Showcase executable selected and whether its packaged cache needed repair.<br/></summary>
internal sealed record ShowcaseWebAssetState(string Root, string Mode, int AssetCount, int RepairedCount);
