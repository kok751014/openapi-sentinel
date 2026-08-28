using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>HTML-only view model construction. It never mutates engine findings.</summary>
internal static class HtmlPresentation
{
    internal static HtmlReportModel Build(ComparisonResult result)
    {
        var distinct = result.Findings
            .GroupBy(CanonicalLogicalChangeIdentity.Create, StringComparer.Ordinal)
            .Select(group => new HtmlFinding(group.First(), HtmlChangeTarget.From(group.First()), GroupOf(group.First()), group.Count()))
            .OrderBy(finding => GroupOrder(finding.Group))
            .ThenByDescending(finding => SeverityOrder(finding.Finding.Severity))
            .ThenBy(finding => finding.Finding.Path, StringComparer.Ordinal)
            .ThenBy(finding => finding.Finding.HttpMethod.ToHttpString(), StringComparer.Ordinal)
            .ThenBy(finding => finding.Target.Canonical, StringComparer.Ordinal)
            .ThenBy(finding => finding.Finding.RuleId, StringComparer.Ordinal)
            .ToArray();

        return new HtmlReportModel(
            distinct,
            distinct.Where(finding => finding.Group == HtmlChangeGroup.Immediate).ToArray(),
            distinct.Where(finding => finding.Group == HtmlChangeGroup.Review).ToArray(),
            distinct.Where(finding => finding.Group == HtmlChangeGroup.General).ToArray());
    }

    internal static HtmlChangeGroup GroupOf(Finding finding) => finding.Compatibility == Compatibility.Breaking
        ? HtmlChangeGroup.Immediate
        : finding.Compatibility is Compatibility.PotentiallyBreaking or Compatibility.Unknown || finding.Severity == Severity.Verify
            ? HtmlChangeGroup.Review
            : HtmlChangeGroup.General;

    private static int GroupOrder(HtmlChangeGroup group) => group switch { HtmlChangeGroup.Immediate => 0, HtmlChangeGroup.Review => 1, _ => 2 };
    private static int SeverityOrder(Severity severity) => severity switch { Severity.Critical => 6, Severity.High => 5, Severity.Verify => 4, Severity.Medium => 3, Severity.Low => 2, _ => 1 };
}

internal enum HtmlChangeGroup { Immediate, Review, General }

internal static class HtmlChangeGroupExtensions
{
    internal static string ToDataValue(this HtmlChangeGroup group) => group switch { HtmlChangeGroup.Immediate => "immediate", HtmlChangeGroup.Review => "review", _ => "general" };
}

internal sealed record HtmlReportModel(IReadOnlyList<HtmlFinding> Findings, IReadOnlyList<HtmlFinding> Immediate, IReadOnlyList<HtmlFinding> Review, IReadOnlyList<HtmlFinding> General);
internal sealed record HtmlFinding(Finding Finding, HtmlChangeTarget Target, HtmlChangeGroup Group, int DuplicateCount);

/// <summary>Stable HTML-only identity based on semantic fields, never localised prose.</summary>
internal static class CanonicalLogicalChangeIdentity
{
    internal static string Create(Finding finding) => string.Join('\u001f', finding.RuleId, finding.Path, finding.HttpMethod.ToHttpString(), finding.Direction, finding.Category, finding.ChangeType, HtmlChangeTarget.From(finding).Canonical, finding.OldValue ?? "", finding.NewValue ?? "");
}

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
    internal string GroupLabel(HtmlChangeGroup group) => T(group switch { HtmlChangeGroup.Immediate => "需要立即處理", HtmlChangeGroup.Review => "建議確認", _ => "一般變更" }, group switch { HtmlChangeGroup.Immediate => "Needs immediate attention", HtmlChangeGroup.Review => "Needs review", _ => "General changes" });
    internal string Title(Finding finding) => IsChinese ? finding.ChangeType switch { ChangeType.Removed => "已移除相容性項目", ChangeType.Added => "已新增規格項目", ChangeType.Renamed => "規格項目已重新命名", _ => "規格項目已變更" } : finding.Title;
    internal string Explanation(Finding finding) => IsChinese ? $"{HtmlChangeTarget.From(finding).Display}{Suffix(finding.ChangeType)}。" : finding.Explanation;
    internal string Impact(Finding finding) => IsChinese ? finding.Compatibility switch { Compatibility.Breaking => "既有用戶端可能無法再依原合約呼叫或處理回應。", Compatibility.PotentiallyBreaking or Compatibility.Unknown => "請確認既有用戶端與部署設定是否依賴此項目。", _ => "此為相容的新增或資訊性變更。" } : finding.Impact;
    internal string Action(Finding finding) => IsChinese ? finding.Compatibility switch { Compatibility.Breaking => "請通知用戶端並提供相容方案或升級指引。", Compatibility.PotentiallyBreaking or Compatibility.Unknown => "請與用戶端確認實際使用情況後再發布。", _ => "請視需要更新文件與用戶端整合。" } : finding.SuggestedAction;
    internal string Short(HtmlFinding finding) => IsChinese ? finding.Target.Display + Suffix(finding.Finding.ChangeType) : finding.Target.EnglishDisplay + EnglishSuffix(finding.Finding.ChangeType);
    private static string Suffix(ChangeType changeType) => changeType switch { ChangeType.Removed => " 已移除", ChangeType.Added => " 已新增", ChangeType.Renamed => " 已重新命名", _ => " 已變更" };
    private static string EnglishSuffix(ChangeType changeType) => changeType switch { ChangeType.Removed => " removed", ChangeType.Added => " added", ChangeType.Renamed => " renamed", _ => " changed" };
}

/// <summary>Exposes deterministic zh-TW HTML localization coverage for rule-registry tests.</summary>
public static class HtmlLocalizationCatalog
{
    public static bool HasTraditionalChinese(string ruleId) => HtmlLocalizer.HasZhTwLocalization(ruleId);
    public static IReadOnlyCollection<string> TraditionalChineseRuleIds => HtmlLocalizer.SupportedZhTwRuleIds;
}
