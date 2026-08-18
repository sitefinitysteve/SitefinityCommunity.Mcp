using ModelContextProtocol.Protocol;
using SitefinityCommunity.Mcp.Extensions;

namespace SitefinityCommunity.Mcp.Tests.Unit;

/// <summary>
/// The limiter is the guard that stands between an oversized tool result and a dropped stdio
/// connection, so its behaviour at and around the boundary is worth pinning down.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ToolOutputLimiterTests
{
    private static CallToolResult TextResult(params string[] blocks)
    {
        return new CallToolResult
        {
            Content = [.. blocks.Select(b => (ContentBlock)new TextContentBlock { Text = b })]
        };
    }

    private static string AllText(CallToolResult result)
    {
        return string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
    }

    /// <summary>
    /// The kept payload, excluding the trailing explanatory notice — which is additive prose and would
    /// otherwise contribute its own letters to any character count.
    /// </summary>
    private static string PayloadOnly(CallToolResult result)
    {
        return string.Concat(result.Content.OfType<TextContentBlock>().SkipLast(1).Select(b => b.Text));
    }

    [Fact]
    public void UnderLimit_PassesThroughUntouched()
    {
        var result = TextResult("a short result");

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 1000);

        Assert.Single(limited.Content);
        Assert.Equal("a short result", AllText(limited));
    }

    [Fact]
    public void ExactlyAtLimit_IsNotTruncated()
    {
        var result = TextResult(new string('x', 100));

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 100);

        Assert.Single(limited.Content);
        Assert.DoesNotContain("truncated", AllText(limited), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OverLimit_TruncatesAndExplains()
    {
        var result = TextResult(new string('x', 5000));

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 1000);

        var text = AllText(limited);
        Assert.Contains("Output truncated by sitefinity-comm-mcp", text);
        Assert.Contains("SITEFINITY_MCP_MAX_TOOL_OUTPUT_CHARS", text);

        // Payload trimmed exactly to the budget; the notice is additive so the caller can always read why.
        var payload = PayloadOnly(limited);
        Assert.Equal(1000, payload.Length);
        Assert.Equal(1000, payload.Count(c => c == 'x'));
    }

    [Fact]
    public void OverLimit_TrimsAcrossMultipleBlocks()
    {
        var result = TextResult(new string('a', 600), new string('b', 600), new string('c', 600));

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 1000);

        var payload = PayloadOnly(limited);
        Assert.Equal(1000, payload.Length);
        Assert.Equal(600, payload.Count(c => c == 'a'));
        Assert.Equal(400, payload.Count(c => c == 'b'));
        Assert.Equal(0, payload.Count(c => c == 'c'));
    }

    [Fact]
    public void NonTextBlocks_SurviveTruncation()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = new string('x', 5000) },
                new ImageContentBlock { Data = new byte[] { 1, 2, 3 }, MimeType = "image/png" }
            ]
        };

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 100);

        // Slicing binary payloads would corrupt them, so they pass through whole.
        Assert.Contains(limited.Content, b => b is ImageContentBlock);
    }

    [Fact]
    public void EmptyResult_IsHandled()
    {
        var result = new CallToolResult { Content = [] };

        var limited = ToolOutputLimiter.Apply(result, maxCharacters: 100);

        Assert.Empty(limited.Content);
    }
}
