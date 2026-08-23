using OpenApiSentinel.Core;

namespace OpenApiSentinel.Core.Tests;

public class GlobMatcherTests
{
    [Theory]
    [InlineData("/internal/**", "/internal", true)]
    [InlineData("/internal/**", "/internal/", true)]
    [InlineData("/internal/**", "/internal/users", true)]
    [InlineData("/internal/**", "/internal/a/b/c", true)]
    [InlineData("/internal/**", "/internalx", false)]
    [InlineData("/internal/**", "/orders/internal", false)]
    public void TrailingDoubleStar_MatchesPrefixAndSubtree(string pattern, string value, bool expected)
        => Assert.Equal(expected, GlobMatcher.Matches(pattern, value));

    [Theory]
    [InlineData("/a/**/b", "/a/b", true)]
    [InlineData("/a/**/b", "/a/x/y/b", true)]
    [InlineData("/a/**/b", "/a/b/b", true)]
    [InlineData("/a/*/items", "/a/orders/items", true)]
    [InlineData("/a/*/items", "/a/orders/sub/items", false)]
    [InlineData("/*", "/only", true)]
    [InlineData("/*", "/two/segments", false)]
    public void SegmentSemantics_AreRespected(string pattern, string value, bool expected)
        => Assert.Equal(expected, GlobMatcher.Matches(pattern, value));

    [Fact]
    public void Star_DoesNotCrossSlashes()
    {
        Assert.True(GlobMatcher.Matches("/o*", "/orders"));
        Assert.False(GlobMatcher.Matches("/o*", "/orders/123"));
    }

    [Fact]
    public void QuestionMark_MatchesSingleCharacterOnly()
    {
        Assert.True(GlobMatcher.Matches("/item-?", "/item-1"));
        Assert.False(GlobMatcher.Matches("/item-?", "/item-12"));
        Assert.False(GlobMatcher.Matches("/item-?", "/item-/"));
    }

    [Fact]
    public void RegexSpecialCharacters_AreEscaped()
    {
        Assert.True(GlobMatcher.Matches("/v1.0/orders(legacy)", "/v1.0/orders(legacy)"));
        Assert.False(GlobMatcher.Matches("/v1.0/orders(legacy)", "/v1X0/orders(legacy)"));
        Assert.False(GlobMatcher.Matches("/v1.0/orders(legacy)", "/v1.0/ordersXlegacy)"));
    }

    [Fact]
    public void DoubleStarAlone_MatchesEverything()
    {
        Assert.True(GlobMatcher.Matches("/**", "/anything/at/all"));
        Assert.False(GlobMatcher.Matches("/**", "relative"));
    }

    [Fact]
    public void Matching_IsCaseSensitive()
    {
        Assert.False(GlobMatcher.Matches("/Orders", "/orders"));
    }

    [Fact]
    public void MatchesAny_ReturnsTrueWhenAnyPatternMatches()
    {
        IReadOnlyList<GlobMatcher> matchers = [new("/internal/**"), new("/legacy/*")];
        Assert.True(GlobMatcher.MatchesAny(matchers, "/legacy/orders"));
        Assert.True(GlobMatcher.MatchesAny(matchers, "/internal/x"));
        Assert.False(GlobMatcher.MatchesAny(matchers, "/public/x"));
    }

    [Theory]
    [InlineData("", "/orders", false)]
    [InlineData("   ", "/orders", false)]
    public void EmptyPatterns_MatchNothing(string pattern, string value, bool expected)
        => Assert.Equal(expected, GlobMatcher.Matches(pattern, value));
}
