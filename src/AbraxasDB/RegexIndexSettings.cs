using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace AbraxasDB
{
    /// <summary>
    /// Options that control how a match-value index is created and validated.<br/>
    /// &lt;br/&gt;
    /// Use an instance of this type with the match-value `CreateIndex(...)` or `CreateMemoryIndex(...)` overloads to provide explicit
    /// group/occurrence selection, validation timeouts, and ordered text representation.<br/>
    /// </summary>
    public sealed class MatchValueIndexOptions
    {
        /// <summary>
        /// Default validation timeout used when none is supplied: 250ms.
        /// </summary>
        public static readonly TimeSpan DefaultValidationTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Local enum describing index variant styles. Mirrors the runtime IndexSelector.IndexType values
        /// but is defined here to avoid depending on the nested RecordStore generic type.
        /// </summary>
        public enum IndexTypeEnum
        {
            /// <summary>
            /// Maintains exact projected text keys, with optional no-case support selected separately.<br/>
            /// </summary>
            Default,
            /// <summary>
            /// Maintains culture sort-key support alongside exact projected text keys.<br/>
            /// </summary>
            CollationKey,
            /// <summary>
            /// Maintains folded projected text for case-insensitive lookup alongside exact text keys.<br/>
            /// </summary>
            FoldedText,
            /// <summary>
            /// Maintains both culture sort-key and folded support alongside exact projected text keys.<br/>
            /// </summary>
            CollatedAndFoldedText,
        }

        /// <summary>
        /// The index style to create (Default / CollationKey / FoldedText / CollatedAndFoldedText).
        /// &lt;br/&gt;Defaults to <see cref="IndexTypeEnum.Default"/>.
        /// </summary>
        public IndexTypeEnum IndexType { get; init; } = IndexTypeEnum.Default;

        /// <summary>
        /// When true and IndexType is Default, maintains exact, folded, and culture sort-key text representations; an explicit IndexType takes precedence.<br/>
        /// &lt;br/&gt;Note: full Unicode folding is provided by the FoldedText variant and is preferred for correctness.
        /// </summary>
        public bool IgnoreCase { get; init; } = false;

        /// <summary>
        /// When true, requests descending physical key order for the match-value index.<br/>
        /// </summary>
        public bool Descending { get; init; } = false;

        /// <summary>
        /// If supplied, the named capture to extract from the regex match for indexing.
        /// &lt;br/&gt;Mutually exclusive with <see cref="SelectedGroupNumber"/>.
        /// </summary>
        public string? SelectedGroupName { get; init; }

        /// <summary>
        /// If supplied, the numeric capture group index to extract for indexing (0 = whole-match).
        /// &lt;br/&gt;Mutually exclusive with <see cref="SelectedGroupName"/>.
        /// </summary>
        public int? SelectedGroupNumber { get; init; }

        /// <summary>
        /// When the pattern can match multiple non-overlapping substrings, this selects which occurrence to use (0 = first).
        /// &lt;br/&gt;When null the library rejects ambiguous multi-match patterns by default; pass a value to select explicitly.
        /// </summary>
        public int? Occurrence { get; init; }

        /// <summary>
        /// How long to wait for sample validation runs before treating the pattern as too expensive.
        /// &lt;br/&gt;Defaults to <see cref="DefaultValidationTimeout"/> when null.
        /// </summary>
        public TimeSpan? ValidationTimeout { get; init; }

        /// <summary>
        /// When true, validation that times out will not cause creation to fail. Use with caution.
        /// &lt;br/&gt;Defaults to <c>false</c> (fail on timeout) to avoid silent, expensive regexes.
        /// </summary>
        public bool AllowExpensiveRegex { get; init; } = false;

        /// <summary>
        /// When true, require that all existing rows produce a non-null match for the selected group/occurrence.
        /// &lt;br/&gt;This triggers an explicit validation pass and can be expensive on large tables. Default false.
        /// </summary>
        public bool RequireMatch { get; init; } = false;

        /// <summary>
        /// Optional sample strings used during validation. When null a sensible default sample set is used.
        /// &lt;br/&gt;The default set covers empty-string, short ASCII, short Unicode and a long repeated string to
        /// surface empty-match and catastrophic backtracking risks.
        /// </summary>
        public IReadOnlyList<string>? ValidationSamples { get; init; }

        /// <summary>
        /// Create a settings instance that selects a named capture group.
        /// &lt;br/&gt;Convenience factory to make intent explicit.
        /// </summary>
        public static MatchValueIndexOptions ForGroupName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentNullException(nameof(groupName));

            return new MatchValueIndexOptions() { SelectedGroupName = groupName };
        }

        /// <summary>
        /// Create a settings instance that selects a numeric capture group.
        /// &lt;br/&gt;Convenience factory to make intent explicit.
        /// </summary>
        public static MatchValueIndexOptions ForGroupNumber(int groupNumber)
        {
            if (groupNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(groupNumber));

            return new MatchValueIndexOptions() { SelectedGroupNumber = groupNumber };
        }

        /// <summary>
        /// Validate the supplied <paramref name="rx"/> against these settings using lightweight checks and the
        /// configured sample strings. Throws <see cref="ArgumentException"/> on validation failure or
        /// <see cref="RegexMatchTimeoutException"/> when a sample match times out (unless <see cref="AllowExpensiveRegex"/>).
        /// &lt;br/&gt;This method is conservative by design and intended to fail-fast for ambiguous or expensive patterns.
        /// </summary>
        /// <param name="rx">Regex instance to validate; must not be null.</param>
        public void Validate(Regex rx)
        {
            if (rx is null)
                throw new ArgumentNullException(nameof(rx));

            // Mutually exclusive group selection
            if (!string.IsNullOrEmpty(SelectedGroupName) && SelectedGroupNumber.HasValue)
                throw new ArgumentException("SelectedGroupName and SelectedGroupNumber are mutually exclusive.");

            // Occurrence must be non-negative if supplied
            if (Occurrence.HasValue && Occurrence.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(Occurrence));

            // Determine group info from the regex
            var groupNames = rx.GetGroupNames() ?? Array.Empty<string>();
            var groupNumbers = rx.GetGroupNumbers() ?? Array.Empty<int>();
            var captureGroupCount = Math.Max(0, groupNumbers.Length - 1); // exclude group 0

            // If user provided a selected group name, ensure it exists
            if (!string.IsNullOrEmpty(SelectedGroupName))
            {
                if (!groupNames.Contains(SelectedGroupName))
                    throw new ArgumentException($"SelectedGroupName '{SelectedGroupName}' is not present in the regex.");
            }

            // If user provided a selected group number, ensure it exists
            if (SelectedGroupNumber.HasValue)
            {
                if (!groupNumbers.Contains(SelectedGroupNumber.Value))
                    throw new ArgumentException($"SelectedGroupNumber '{SelectedGroupNumber.Value}' is not present in the regex.");
            }

            // If there are any capture groups require explicit group selection
            if (captureGroupCount >= 1 && string.IsNullOrEmpty(SelectedGroupName) && !SelectedGroupNumber.HasValue)
                throw new ArgumentException("Ambiguous pattern: capture groups require explicit selection through MatchValueIndexOptions.ForGroupName(...) or ForGroupNumber(...).\nIf you intended to index the whole match, pass SelectedGroupNumber = 0.");

            // Prepare validation samples
            var samples = ValidationSamples ?? new List<string>() { string.Empty, "a", "é", new string('a', 4096) };
            var timeout = ValidationTimeout ?? DefaultValidationTimeout;

            // Create a regex instance for validation that uses the requested timeout
            Regex validationRegex;
            try
            {
                validationRegex = new Regex(rx.ToString(), rx.Options, timeout);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Fallback: if the provided timeout is invalid for some reason, use the default
                validationRegex = new Regex(rx.ToString(), rx.Options, DefaultValidationTimeout);
            }

            // Empty-match check
            try
            {
                if (validationRegex.IsMatch(string.Empty))
                    throw new ArgumentException("Regex can match the empty string which is unsafe for indexing. Refactor the pattern or provide explicit group/occurrence selection.");
            }
            catch (RegexMatchTimeoutException)
            {
                if (!AllowExpensiveRegex)
                    throw;
            }

            // Multi-match ambiguity: if any sample yields multiple matches and caller didn't supply Occurrence, reject
            foreach (var s in samples)
            {
                try
                {
                    var matches = validationRegex.Matches(s);
                    if (matches.Count > 1 && !Occurrence.HasValue)
                        throw new ArgumentException($"Regex produces multiple matches on sample input '{ShortSample(s)}'. Provide an Occurrence value to select which match to index.");

                    // If caller selected a group, ensure that selected group exists and yields a value when match exists
                    if (matches.Count >= 1 && (SelectedGroupNumber.HasValue || !string.IsNullOrEmpty(SelectedGroupName)))
                    {
                        var m = matches[0];
                        string? val = null;
                        if (SelectedGroupNumber.HasValue)
                        {
                            var gi = SelectedGroupNumber.Value;
                            if (gi < m.Groups.Count)
                                val = m.Groups[gi].Success ? m.Groups[gi].Value : null;
                            else
                                throw new ArgumentException($"Selected group number {gi} not available on sample match.");
                        }
                        else if (!string.IsNullOrEmpty(SelectedGroupName))
                        {
                            var gname = SelectedGroupName!;
                            var grp = m.Groups[gname];
                            if (grp == null)
                                throw new ArgumentException($"Selected group name '{gname}' not available on sample match.");
                            val = grp.Success ? grp.Value : null;
                        }

                        // Treat missing capture as acceptable at validation time; callers may opt into RequireMatch which will do a fuller scan.
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    if (!AllowExpensiveRegex)
                        throw new ArgumentException("Regex validation timed out on sample input; pattern appears too expensive. Set AllowExpensiveRegex to true to opt in.");
                }
            }
        }

        /// <summary>
        /// Serialize this settings instance to JSON. Uses compact representation and omits null values.
        /// &lt;br/&gt;Useful for persisting settings or embedding them in metadata.
        /// </summary>
        /// <returns>JSON string representation of these settings.</returns>
        public string ToJson()
        {
            var opts = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            return JsonSerializer.Serialize(this, opts);
        }

        /// <summary>
        /// Parse settings from JSON previously produced by <see cref="ToJson"/>.
        /// &lt;br/&gt;Throws <see cref="ArgumentNullException"/> for null/empty input and <see cref="JsonException"/> on parse errors.
        /// </summary>
        /// <param name="json">JSON string to parse.</param>
        /// <returns>Rehydrated <see cref="MatchValueIndexOptions"/> instance.</returns>
        public static MatchValueIndexOptions FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentNullException(nameof(json));

            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var s = JsonSerializer.Deserialize<MatchValueIndexOptions>(json, opts);
            if (s is null) throw new JsonException("Failed to deserialize MatchValueIndexOptions from JSON.");
            return s;
        }

        /// <summary>
        /// Try to parse settings from JSON. Does not throw on failure; returns false instead.
        /// &lt;br/&gt;On success <paramref name="settings"/> will contain the deserialized instance.
        /// </summary>
        public static bool TryFromJson(string? json, out MatchValueIndexOptions? settings, out string? error)
        {
            settings = null; error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Input json is null or empty.";
                return false;
            }

            try
            {
                settings = FromJson(json!);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                settings = null;
                return false;
            }
        }

        /// <summary>
        /// Compute a short deterministic hex identity for these settings. Useful when embedding settings identity
        /// into names or tokens. Produces the first <paramref name="hexChars"/> characters of the SHA-256 hex digest
        /// of the canonical JSON form produced by <see cref="ToJson"/>.
        /// &lt;br/&gt;Default returns 8 hex characters.
        /// </summary>
        /// <param name="hexChars">Number of hex characters to return (must be even and &gt;= 2).</param>
        /// <returns>Lower-case hex string.</returns>
        public string ComputeDeterministicId(int hexChars = 8)
        {
            if (hexChars <= 0) throw new ArgumentOutOfRangeException(nameof(hexChars));
            var json = ToJson();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            if (hexChars >= hex.Length) return hex;
            return hex.Substring(0, hexChars);
        }

        private static string ShortSample(string s)
        {
            if (s is null)
                return "(null)";
            if (s.Length <= 32)
                return s.Replace("\n", "\\n").Replace("\r", "\\r");
            return s.Substring(0, 32).Replace("\n", "\\n").Replace("\r", "\\r") + "...";
        }
    }
}
