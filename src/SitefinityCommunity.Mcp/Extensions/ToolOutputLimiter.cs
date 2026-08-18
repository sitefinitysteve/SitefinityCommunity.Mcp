using ModelContextProtocol.Protocol;

namespace SitefinityCommunity.Mcp.Extensions;

/// <summary>
/// Last line of defence for the stdio transport. Every tool result is serialized into a single JSON-RPC
/// frame written to stdout; an oversized frame does not fail gracefully, it drops the connection and the
/// caller sees only "MCP error -32000: Connection closed" with no clue which tool was responsible.
/// <para>
/// This has bitten for real: <c>sitefinity_get_config_section("ContentViewConfig")</c> returned a
/// defaults-merged 79 MB / 375,000-entry payload, which then roughly doubled through indented
/// re-serialization before being string-escaped into one frame.
/// </para>
/// <para>
/// Tools are expected to bound their own output at the source — that is where a good error message and a
/// real total count can live. This limiter exists so that a tool which fails to do so degrades into a
/// readable, truncated result instead of taking the whole session down with it.
/// </para>
/// </summary>
public static class ToolOutputLimiter
{
    /// <summary>Roughly 60k tokens — large enough for any legitimate result, small enough to survive the pipe.</summary>
    public const int DefaultMaxCharacters = 250_000;

    /// <summary>Floor for the override, so a stray value cannot render every tool useless.</summary>
    private const int MinimumMaxCharacters = 1_000;

    private static readonly int ConfiguredMaxCharacters = ResolveLimit();

    /// <summary>
    /// Trims a tool result to the configured character budget, replacing whatever spills over with a
    /// notice explaining what happened and what to do about it.
    /// </summary>
    public static CallToolResult Apply(CallToolResult result)
    {
        return Apply(result, ConfiguredMaxCharacters);
    }

    /// <summary>Budget-explicit overload, for tests.</summary>
    public static CallToolResult Apply(CallToolResult result, int maxCharacters)
    {
        if (result?.Content is null || result.Content.Count == 0)
        {
            return result!;
        }

        var total = 0;

        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text)
            {
                total += text.Text?.Length ?? 0;
            }
        }

        if (total <= maxCharacters)
        {
            return result;
        }

        var trimmed = new List<ContentBlock>();
        var remaining = maxCharacters;

        foreach (var block in result.Content)
        {
            if (block is not TextContentBlock text)
            {
                // Non-text blocks (images, embedded resources) are passed through untouched — slicing
                // their payload would corrupt them, and they are not what blows the budget in practice.
                trimmed.Add(block);
                continue;
            }

            var value = text.Text ?? string.Empty;

            if (remaining <= 0)
            {
                continue;
            }

            if (value.Length <= remaining)
            {
                trimmed.Add(block);
                remaining -= value.Length;
                continue;
            }

            trimmed.Add(new TextContentBlock { Text = value[..remaining] });
            remaining = 0;
        }

        trimmed.Add(new TextContentBlock
        {
            Text = $"\n\n[Output truncated by sitefinity-mcp: {total:N0} characters exceeded the " +
                   $"{maxCharacters:N0}-character limit, so {total - maxCharacters:N0} were dropped. " +
                   "The text above is cut mid-stream and may not parse as JSON. " +
                   "Narrow the request — add a filter, lower a page size, or target a single item — " +
                   "rather than retrying it unchanged. " +
                   "Raise the limit with the SITEFINITY_MCP_MAX_TOOL_OUTPUT_CHARS environment variable " +
                   "if a large result really is needed.]"
        });

        result.Content = trimmed;
        return result;
    }

    /// <summary>
    /// Reads the limit from <c>SITEFINITY_MCP_MAX_TOOL_OUTPUT_CHARS</c>, falling back to the default when
    /// unset, unparseable, or below the floor.
    /// </summary>
    private static int ResolveLimit()
    {
        var raw = Environment.GetEnvironmentVariable("SITEFINITY_MCP_MAX_TOOL_OUTPUT_CHARS");

        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultMaxCharacters;
        }

        if (!int.TryParse(raw, out var parsed))
        {
            return DefaultMaxCharacters;
        }

        return parsed < MinimumMaxCharacters ? MinimumMaxCharacters : parsed;
    }
}
