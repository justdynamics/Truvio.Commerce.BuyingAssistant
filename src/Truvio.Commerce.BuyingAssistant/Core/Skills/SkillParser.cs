namespace Truvio.Commerce.BuyingAssistant.Core.Skills;

/// <summary>A named block of instructions the assistant applies when the request matches.</summary>
public sealed record Skill(string Name, string Body);

/// <summary>
/// Parses the Skills setting. Format: sections that start with a line
/// <c>## Skill: Name</c>; everything until the next such line is the skill body.
/// Text before the first heading is ignored (it belongs in Instructions).
/// </summary>
public static class SkillParser
{
    private const string HeadingPrefix = "## Skill:";

    public static IReadOnlyList<Skill> Parse(string? text)
    {
        var skills = new List<Skill>();
        if (string.IsNullOrWhiteSpace(text)) return skills;

        string? name = null;
        var body = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith(HeadingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Flush(skills, name, body);
                name = line.TrimStart()[HeadingPrefix.Length..].Trim();
                body = new List<string>();
                continue;
            }
            if (name != null) body.Add(line);
        }
        Flush(skills, name, body);
        return skills;
    }

    /// <summary>
    /// Keeps only the skills named in <paramref name="namesCsv"/> (comma or newline separated,
    /// case-insensitive, trailing * matches a prefix). Blank returns everything.
    /// </summary>
    public static IReadOnlyList<Skill> Filter(IReadOnlyList<Skill> skills, string? namesCsv)
    {
        if (string.IsNullOrWhiteSpace(namesCsv)) return skills;
        var patterns = namesCsv.Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (patterns.Count == 0) return skills;
        return skills.Where(s => patterns.Any(p => Matches(s.Name, p))).ToList();
    }

    private static bool Matches(string name, string pattern)
    {
        if (pattern.EndsWith('*')) return name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase);
        return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static void Flush(List<Skill> skills, string? name, List<string> body)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var text = string.Join("\n", body).Trim();
        skills.Add(new Skill(name.Trim(), text));
    }
}
