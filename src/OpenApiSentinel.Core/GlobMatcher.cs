using System.Text;
using System.Text.RegularExpressions;

namespace OpenApiSentinel.Core;

/// <summary>
/// Bounded glob matcher for API paths and suppression patterns.
/// Supported syntax: <c>*</c> (any characters except <c>/</c>), <c>**</c> (any characters including
/// <c>/</c>, as a whole segment also matches zero segments), <c>?</c> (single character except
/// <c>/</c>). Matching is ordinal (case-sensitive) and anchored to the full string.
/// </summary>
public sealed class GlobMatcher
{
    private readonly Regex _regex;

    public GlobMatcher(string pattern)
    {
        Pattern = pattern;
        _regex = Compile(pattern);
    }

    public string Pattern { get; }

    public bool IsMatch(string value) => _regex.IsMatch(value);

    public static bool Matches(string pattern, string value) => new GlobMatcher(pattern).IsMatch(value);

    public static bool MatchesAny(IReadOnlyCollection<string> patterns, string value)
    {
        foreach (var pattern in patterns)
        {
            if (Matches(pattern, value)) return true;
        }
        return false;
    }

    /// <summary>Overload accepting pre-compiled matchers to avoid recompiling per call.</summary>
    public static bool MatchesAny(IReadOnlyList<GlobMatcher> matchers, string value)
    {
        foreach (var matcher in matchers)
        {
            if (matcher.IsMatch(value)) return true;
        }
        return false;
    }

    private static Regex Compile(string pattern)
    {
        var p = pattern.Trim();
        var hasTrailingDoubleStar = p.EndsWith("/**", StringComparison.Ordinal);
        if (hasTrailingDoubleStar) p = p[..^3];

        var sb = new StringBuilder();
        sb.Append('^');
        var i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '*')
            {
                var isDoubleStar = i + 1 < p.Length && p[i + 1] == '*';
                if (!isDoubleStar)
                {
                    sb.Append("[^/]*");
                    i++;
                    continue;
                }

                var atSegmentStart = i == 0 || p[i - 1] == '/';
                var followedBySlash = i + 2 < p.Length && p[i + 2] == '/';
                if (atSegmentStart && followedBySlash)
                {
                    // "/a/**/b" also matches "/a/b"
                    sb.Append("(?:.*/)?");
                    i += 3;
                    continue;
                }

                sb.Append(".*");
                i += 2;
                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                i++;
                continue;
            }

            sb.Append(Regex.Escape(c.ToString()));
            i++;
        }

        if (hasTrailingDoubleStar)
        {
            if (p.Length == 0)
            {
                // "/**" alone matches every non-empty path
                sb.Clear().Append("^/.+");
            }
            else
            {
                sb.Append("(?:/.*)?");
            }
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
    }
}
