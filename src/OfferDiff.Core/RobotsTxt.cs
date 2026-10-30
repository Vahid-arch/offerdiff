using System.Text.RegularExpressions;

namespace OfferDiff.Core;

// robots.txt parser (RFC 9309): text in, allow/disallow decisions out. HttpPageSource does the fetching.
// shortcut: paths are compared as written, without normalizing percent-encoding (%7E vs ~); normalize if sites
// write the same path both ways
public class RobotsTxt
{
    // RFC 9309 asks crawlers to parse at least 500 KiB; anything after that is ignored.
    public const int MaxChars = 500 * 1024;

    record Rule(bool Allow, int Length, Regex Pattern);
    record Group(List<string> Agents, List<Rule> Rules);

    readonly List<Group> groups = [];

    RobotsTxt() { }

    public static RobotsTxt Parse(string text)
    {
        var robots = new RobotsTxt();
        Group? group = null;
        var inAgentLines = false;

        foreach (var rawLine in (text.Length > MaxChars ? text[..MaxChars] : text).Split('\n'))
        {
            var line = rawLine.Split('#')[0];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            if (key == "user-agent")
            {
                // consecutive User-agent lines share one group
                if (group == null || !inAgentLines) robots.groups.Add(group = new([], []));
                // "OfferDiff/1.0" still names our product token "offerdiff"
                group.Agents.Add(value.Split('/')[0].Trim().ToLowerInvariant());
                inAgentLines = true;
            }
            else if (key is "allow" or "disallow")
            {
                inAgentLines = false;
                // an empty rule matches nothing ("Disallow:" = allow all); rules before any User-agent have no group
                if (group != null && value != "")
                    group.Rules.Add(new(key == "allow", value.Length, ToRegex(value)));
            }
            // Sitemap, Crawl-delay and unknown lines are ignored
        }
        return robots;
    }

    public bool IsAllowed(string userAgent, string pathAndQuery)
    {
        var agent = userAgent.ToLowerInvariant();
        // all groups for the same agent are combined; our own name beats "*"
        var matching = groups.Where(g => g.Agents.Contains(agent)).ToList();
        if (matching.Count == 0) matching = groups.Where(g => g.Agents.Contains("*")).ToList();

        // longest matching rule wins, Allow wins a tie, and no matching rule means allowed
        var best = matching.SelectMany(g => g.Rules)
            .Where(r => r.Pattern.IsMatch(pathAndQuery))
            .OrderByDescending(r => r.Length)
            .ThenByDescending(r => r.Allow)
            .FirstOrDefault();
        return best?.Allow ?? true;
    }

    // "*" matches any characters, a final "$" anchors the end, everything else is literal and case-sensitive.
    // NonBacktracking keeps matching linear, so a hostile pattern like "/*a*a*a*a" cannot hang the run.
    static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var literalParts = (anchored ? pattern[..^1] : pattern).Split('*').Select(Regex.Escape);
        return new Regex("^" + string.Join(".*", literalParts) + (anchored ? "$" : ""),
            RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
    }
}
