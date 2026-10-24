using System.Globalization;
using System.Text.RegularExpressions;
using TenderSync.Application.Abstractions.Llm;

namespace TenderSync.Application.Features.Llm;

/// <summary>
/// Loads Prompts/{purpose}/v{n}.md from the application base directory (copied to output by the csproj). Each file starts
/// with a header block between "---" lines: purpose, tier (fast|strong), schema, eval. The body is the system prompt.
/// Files are read once; a prompt change is a new version (PLAN.md §8.3).
/// </summary>
public sealed partial class PromptRegistry : IPromptRegistry
{
    private readonly Dictionary<string, List<PromptTemplate>> _byPurpose;

    public PromptRegistry()
        : this(Path.Combine(AppContext.BaseDirectory, "Prompts"))
    {
    }

    public PromptRegistry(string root)
    {
        _byPurpose = new Dictionary<string, List<PromptTemplate>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "v*.md", SearchOption.AllDirectories))
        {
            var match = VersionFile().Match(Path.GetFileName(file));
            if (!match.Success)
            {
                continue;
            }

            var purpose = Path.GetFileName(Path.GetDirectoryName(file))!;
            var version = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var template = Parse(purpose, version, File.ReadAllText(file));
            if (!_byPurpose.TryGetValue(purpose, out var list))
            {
                _byPurpose[purpose] = list = [];
            }

            list.Add(template);
        }

        foreach (var list in _byPurpose.Values)
        {
            list.Sort((a, b) => a.Version.CompareTo(b.Version));
        }
    }

    public PromptTemplate Get(string purpose, int? version = null)
    {
        if (!_byPurpose.TryGetValue(purpose, out var versions) || versions.Count == 0)
        {
            throw new InvalidOperationException($"No prompt files for purpose '{purpose}'. Expected Prompts/{purpose}/v1.md.");
        }

        if (version is null)
        {
            return versions[^1];
        }

        return versions.FirstOrDefault(v => v.Version == version)
            ?? throw new InvalidOperationException($"Prompt '{purpose}' has no version {version}. Available: {string.Join(", ", versions.Select(v => v.VersionTag))}.");
    }

    public IReadOnlyList<PromptTemplate> List() => _byPurpose.Values.SelectMany(v => v).OrderBy(p => p.Purpose, StringComparer.Ordinal).ThenBy(p => p.Version).ToList();

    /// <summary>Parses the header ("---" delimited key: value lines) and the body.</summary>
    public static PromptTemplate Parse(string purpose, int version, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var text = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart();
        var tier = ModelTier.Fast;
        string? schema = null, eval = null;
        var body = text;

        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end > 0)
            {
                foreach (var line in text[4..end].Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var colon = line.IndexOf(':', StringComparison.Ordinal);
                    if (colon <= 0)
                    {
                        continue;
                    }

                    var key = line[..colon].Trim().ToLowerInvariant();
                    var value = line[(colon + 1)..].Trim();
                    switch (key)
                    {
                        case "tier":
                            tier = value.Equals("strong", StringComparison.OrdinalIgnoreCase) ? ModelTier.Strong : ModelTier.Fast;
                            break;
                        case "schema":
                            schema = value.Length == 0 ? null : value;
                            break;
                        case "eval":
                            eval = value.Length == 0 ? null : value;
                            break;
                        case "purpose" when !value.Equals(purpose, StringComparison.OrdinalIgnoreCase):
                            throw new InvalidOperationException($"Prompt file in folder '{purpose}' declares purpose '{value}'.");
                    }
                }

                body = text[(end + 4)..].TrimStart('-').Trim();
            }
        }

        return new PromptTemplate(purpose, version, tier, schema, eval, body.Trim());
    }

    [GeneratedRegex(@"^v(\d+)\.md$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionFile();
}
