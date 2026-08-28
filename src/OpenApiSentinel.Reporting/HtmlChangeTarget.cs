using System.Text.RegularExpressions;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>Normalizes a finding location for HTML display and semantic-only grouping.</summary>
internal sealed record HtmlChangeTarget(string Display, string EnglishDisplay, string Canonical)
{
    private static readonly Regex PropertyPattern = new(@"^(?:(?<prefix>.*?)\s+)?(?:schema\s+)?properties\s+(?<name>[^\s]+)(?<rest>.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex ParameterPattern = new(@"^(?:(?<prefix>.*?)\s+)?parameters\s+(?<name>[^\s]+)(?<rest>.*)$", RegexOptions.CultureInvariant);

    internal static HtmlChangeTarget From(Finding finding)
    {
        var relative = RelativeLocation(finding);
        var canonical = string.Join(" ", relative.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return new HtmlChangeTarget(DisplayLocation(relative, finding.Category, chinese: true), DisplayLocation(relative, finding.Category, chinese: false), canonical);
    }

    private static string RelativeLocation(Finding finding)
    {
        var location = finding.Location.Trim();
        var method = finding.HttpMethod.ToHttpString();
        if (!string.IsNullOrEmpty(method) && !string.IsNullOrEmpty(finding.Path))
        {
            var prefix = method + " " + finding.Path;
            if (location.StartsWith(prefix, StringComparison.Ordinal) && (location.Length == prefix.Length || char.IsWhiteSpace(location[prefix.Length]))) location = location[prefix.Length..].TrimStart();
        }

        return location.Replace('.', ' ');
    }

    private static string DisplayLocation(string relative, FindingCategory category, bool chinese)
    {
        if (string.IsNullOrWhiteSpace(relative)) return category == FindingCategory.Endpoint ? "Endpoint" : category.ToString();
        var property = PropertyPattern.Match(relative);
        if (property.Success) return Join(property.Groups["prefix"].Value, $"{(chinese ? "欄位" : "property")} `{property.Groups["name"].Value}`", property.Groups["rest"].Value);
        var parameter = ParameterPattern.Match(relative);
        if (parameter.Success) return Join(parameter.Groups["prefix"].Value, $"{(chinese ? "參數" : "parameter")} `{parameter.Groups["name"].Value}`", parameter.Groups["rest"].Value);
        return relative.Replace("requestBody", "body", StringComparison.Ordinal);
    }

    private static string Join(params string[] values) => string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));
}
