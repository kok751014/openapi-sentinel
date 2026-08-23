using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>
/// JUnit-style XML so CI systems can surface the comparison as a test outcome.
/// One failing testcase per policy-violating finding; a passing testcase otherwise.
/// </summary>
public sealed class JunitReporter : IResultReporter
{
    public string FormatName => "junit";

    public string Render(ComparisonResult result, ReporterOptions options)
    {
        var violating = result.Findings.Where(f =>
            f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking &&
            f.Severity >= Severity.Medium).ToList();

        var suiteName = $"OpenAPI Sentinel: {result.OldContractName} → {result.NewContractName}";
        var timestamp = result.GeneratedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss");

        XDocument doc;
        if (violating.Count > 0)
        {
            doc = new XDocument(
                new XElement("testsuites",
                    new XAttribute("name", "OpenAPI Sentinel"),
                    new XAttribute("tests", violating.Count),
                    new XAttribute("failures", violating.Count),
                    new XElement("testsuite",
                        new XAttribute("name", suiteName),
                        new XAttribute("tests", violating.Count),
                        new XAttribute("failures", violating.Count),
                        new XAttribute("timestamp", timestamp),
                        violating.Select(f => new XElement("testcase",
                            new XAttribute("name", $"{f.RuleId} {f.Title}"),
                            new XAttribute("classname", MarkdownReporter.Describe(f)),
                            new XElement("failure",
                                new XAttribute("type", f.Compatibility.ToString()),
                                new XAttribute("message", $"[{f.Severity}] {f.Title} at {MarkdownReporter.Describe(f)}"),
                                new XText($"{f.Explanation}\n\nImpact: {f.Impact}\n\nSuggested action: {f.SuggestedAction}")))))));
        }
        else
        {
            doc = new XDocument(
                new XElement("testsuites",
                    new XAttribute("name", "OpenAPI Sentinel"),
                    new XAttribute("tests", 1),
                    new XAttribute("failures", 0),
                    new XElement("testsuite",
                        new XAttribute("name", suiteName),
                        new XAttribute("tests", 1),
                        new XAttribute("failures", 0),
                        new XAttribute("timestamp", timestamp),
                        new XElement("testcase",
                            new XAttribute("name", "API compatibility policy"),
                            new XAttribute("classname", "OpenAPI Sentinel")))));
        }

        return DocToString(doc);
    }

    /// <summary>Serializes with XML-safe character sanitization for untrusted spec content.</summary>
    internal static string DocToString(XDocument doc)
    {
        var buffer = new StringBuilder();
        // The XmlWriter flushes into <paramref name="buffer"/> only when disposed,
        // so it must be scoped strictly around Save.
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = true,
            Encoding = Encoding.UTF8,
            NewLineHandling = NewLineHandling.Entitize,
        }))
        {
            doc.Save(writer);
        }

        return buffer.ToString();
    }
}
