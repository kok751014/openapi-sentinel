using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>HTML-only view model construction. It never mutates engine findings.</summary>
internal static class HtmlPresentation
{
    internal static HtmlReportModel Build(ComparisonResult result)
    {
        var distinct = result.Findings
            .GroupBy(Identity, StringComparer.Ordinal)
            .Select(g => new HtmlFinding(g.First(), g.Count()))
            .OrderBy(f => CompatibilityOrder(f.Finding.Compatibility))
            .ThenByDescending(f => SeverityOrder(f.Finding.Severity))
            .ThenBy(f => f.Finding.Path, StringComparer.Ordinal)
            .ThenBy(f => f.Finding.HttpMethod.ToHttpString(), StringComparer.Ordinal)
            .ThenBy(f => f.Finding.Location, StringComparer.Ordinal)
            .ThenBy(f => f.Finding.RuleId, StringComparer.Ordinal)
            .ToArray();
        var endpoints = (result.Endpoints ?? [])
            .Select(e => new HtmlEndpoint(e, distinct.Where(f => f.Finding.Path == e.Path && f.Finding.HttpMethod == e.HttpMethod).ToArray()))
            .OrderBy(EndpointOrder).ThenBy(e => e.Endpoint.Path, StringComparer.Ordinal).ThenBy(e => e.Endpoint.HttpMethod.ToHttpString(), StringComparer.Ordinal).ToArray();
        return new HtmlReportModel(distinct, endpoints, distinct.Where(f => IsAttention(f.Finding)).ToArray(), distinct.Where(f => !IsAttention(f.Finding)).ToArray(), BuildRelated(distinct));
    }

    private static string Identity(Finding f) => string.Join('\u001f', f.RuleId, f.Path, f.HttpMethod.ToHttpString(), f.Location, f.OldValue ?? "", f.NewValue ?? "", f.Severity, f.Compatibility, f.ChangeType, f.Direction, f.Title, f.Explanation, f.Impact, f.SuggestedAction);
    internal static bool IsAttention(Finding f) => f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking or Compatibility.Unknown || f.Severity == Severity.Verify;
    private static int CompatibilityOrder(Compatibility c) => c switch { Compatibility.Breaking => 0, Compatibility.PotentiallyBreaking => 1, Compatibility.Unknown => 2, Compatibility.NonBreaking => 3, _ => 4 };
    private static int SeverityOrder(Severity s) => s switch { Severity.Critical => 6, Severity.High => 5, Severity.Verify => 4, Severity.Medium => 3, Severity.Low => 2, _ => 1 };
    private static int EndpointOrder(HtmlEndpoint e) => (e.Endpoint.State, e.Findings.Any(f => f.Finding.Compatibility == Compatibility.Breaking), e.Findings.Any(f => IsAttention(f.Finding))) switch { (EndpointState.Removed, true, _) => 0, (EndpointState.Changed, true, _) => 1, (EndpointState.Changed, _, true) => 2, (EndpointState.Added, _, _) => 3, _ => 4 };
    private static HtmlRelatedChange[] BuildRelated(IReadOnlyList<HtmlFinding> findings) => findings.Where(f => !string.IsNullOrEmpty(f.Finding.Path)).GroupBy(f => (f.Finding.Path, f.Finding.HttpMethod, ParentLocation(f.Finding.Location))).Where(g => g.Count() > 1).Select(g => new HtmlRelatedChange(g.Key.Path, g.Key.HttpMethod, g.Key.Item3, g.ToArray())).OrderBy(g => g.Path, StringComparer.Ordinal).ThenBy(g => g.Method.ToHttpString(), StringComparer.Ordinal).ToArray();
    private static string ParentLocation(string location) { var parts = location.Split('.', StringSplitOptions.RemoveEmptyEntries); return location.StartsWith("responses.", StringComparison.Ordinal) && parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts.FirstOrDefault() ?? location; }
}

internal sealed record HtmlReportModel(IReadOnlyList<HtmlFinding> Findings, IReadOnlyList<HtmlEndpoint> Endpoints, IReadOnlyList<HtmlFinding> Attention, IReadOnlyList<HtmlFinding> Other, IReadOnlyList<HtmlRelatedChange> Related);
internal sealed record HtmlFinding(Finding Finding, int DuplicateCount);
internal sealed record HtmlEndpoint(EndpointSummary Endpoint, IReadOnlyList<HtmlFinding> Findings);
internal sealed record HtmlRelatedChange(string Path, HttpMethodKind Method, string ParentLocation, IReadOnlyList<HtmlFinding> Findings);

/// <summary>Deterministic HTML-only localizer. Rule coverage is explicit so future additions cannot silently fall back.</summary>
internal sealed class HtmlLocalizer
{
    private static readonly HashSet<string> ZhRuleIds = new("""CG1001 CG1002 CG1003 CG1004 CG1005 CG1010 CG1011 CG1012 CG1013 CG2001 CG2002 CG2003 CG2004 CG2005 CG2006 CG2007 CG2008 CG2009 CG2010 CG2011 CG2012 CG2013 CG2014 CG2015 CG2016 CG2017 CG2018 CG2019 CG3001 CG3002 CG3003 CG3004 CG3005 CG3006 CG4001 CG4002 CG4003 CG4004 CG4005 CG4006 CG4007 CG5001 CG5002 CG5003 CG5004 CG5005 CG5006 CG5007 CG5008 CG5009 CG5010 CG5011 CG5012 CG5013 CG5014 CG5015 CG5017 CG5018 CG5019 CG5020 CG5022 CG5023 CG5024 CG5025 CG5026 CG6001 CG6002 CG6003 CG6004 CG6005 CG6006 CG6007 CG6008 CG6009 CG6010 CG6011 CG6012 CG6013 CG6014 CG6015 CG7001 CG7002 CG7003 CG7004 CG7005 CG9001 CG9002 CG9003""".Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    internal HtmlLocalizer(HtmlLanguage language) => Language = language;
    internal HtmlLanguage Language { get; }
    internal bool IsChinese => Language == HtmlLanguage.ZhTw;
    internal static bool HasZhTwLocalization(string ruleId) => ZhRuleIds.Contains(ruleId);
    internal static IReadOnlyCollection<string> SupportedZhTwRuleIds => ZhRuleIds;
    internal string T(string zh, string en) => IsChinese ? zh : en;
    internal string CompatibilityLabel(Compatibility c) => T(c switch { Compatibility.Breaking => "重大變更", Compatibility.PotentiallyBreaking => "可能影響相容性", Compatibility.NonBreaking => "非破壞性", Compatibility.Informational => "資訊", _ => "需要確認" }, c switch { Compatibility.PotentiallyBreaking => "Potentially breaking", Compatibility.NonBreaking => "Non-breaking", _ => c.ToString() });
    internal string State(EndpointState s) => T(s switch { EndpointState.Added => "新增", EndpointState.Removed => "移除", EndpointState.Changed => "修改", _ => "未變更" }, s.ToString());
    internal string Title(Finding f) => IsChinese ? f.ChangeType switch { ChangeType.Removed => "已移除相容性項目", ChangeType.Added => "已新增規格項目", ChangeType.Renamed => "規格項目已重新命名", _ => "規格項目已變更" } : f.Title;
    internal string Explanation(Finding f) => IsChinese ? f.ChangeType switch { ChangeType.Removed => $"{Target(f)}已移除。", ChangeType.Added => $"{Target(f)}已新增。", ChangeType.Renamed => $"{Target(f)}已重新命名。", _ => $"{Target(f)}已變更。" } : f.Explanation;
    internal string Impact(Finding f) => IsChinese ? f.Compatibility switch { Compatibility.Breaking => "既有用戶端可能無法再依原合約呼叫或處理回應。", Compatibility.PotentiallyBreaking or Compatibility.Unknown => "請確認既有用戶端與部署設定是否依賴此項目。", _ => "此為相容的新增或資訊性變更。" } : f.Impact;
    internal string Action(Finding f) => IsChinese ? f.Compatibility switch { Compatibility.Breaking => "請通知用戶端並提供相容方案或升級指引。", Compatibility.PotentiallyBreaking or Compatibility.Unknown => "請與用戶端確認實際使用情況後再發布。", _ => "請視需要更新文件與用戶端整合。" } : f.SuggestedAction;
    internal string Short(Finding f) => IsChinese ? f.ChangeType switch { ChangeType.Removed => $"{Target(f)}已移除", ChangeType.Added => $"{Target(f)}已新增", _ => $"{Target(f)}已變更" } : f.Title;
    private static string Target(Finding f) => f.Category == FindingCategory.Endpoint ? "Endpoint" : !string.IsNullOrWhiteSpace(f.Location) ? f.Location.Replace('.', ' ') : f.Category.ToString();
}

/// <summary>Exposes deterministic zh-TW HTML localization coverage for rule-registry tests.</summary>
public static class HtmlLocalizationCatalog
{
    /// <summary>Returns whether a built-in rule has a Traditional-Chinese HTML localization entry.</summary>
    public static bool HasTraditionalChinese(string ruleId) => HtmlLocalizer.HasZhTwLocalization(ruleId);

    /// <summary>Rule IDs covered by the Traditional-Chinese HTML localization catalog.</summary>
    public static IReadOnlyCollection<string> TraditionalChineseRuleIds => HtmlLocalizer.SupportedZhTwRuleIds;
}
