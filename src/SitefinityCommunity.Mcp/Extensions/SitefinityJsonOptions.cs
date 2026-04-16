using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SitefinityCommunity.Mcp.Extensions;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> used when deserializing responses from the
/// Sitefinity plugin. ServiceStack's default JSON output emits dates in Microsoft's legacy
/// <c>\/Date(1234567890)\/</c> format, which <see cref="System.Text.Json"/> does not parse
/// natively. The converters here accept both that legacy format and ISO 8601 so the MCP
/// server is resilient to whichever date handler the plugin host has configured.
/// </summary>
internal static class SitefinityJsonOptions
{
    public static readonly JsonSerializerOptions Default = Build();

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new SitefinityDateTimeConverter());
        options.Converters.Add(new SitefinityNullableDateTimeConverter());
        return options;
    }
}

/// <summary>
/// Reads <see cref="DateTime"/> values emitted by ServiceStack in either the legacy
/// <c>/Date(…)/</c> format or ISO 8601. Writes ISO 8601 (round-trip "O") on output.
/// </summary>
internal sealed class SitefinityDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return SitefinityDateParser.Parse(ref reader) ?? default;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>
/// Nullable counterpart of <see cref="SitefinityDateTimeConverter"/> — preserves JSON nulls
/// on the wire and handles both the legacy ServiceStack date format and ISO 8601.
/// </summary>
internal sealed class SitefinityNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return SitefinityDateParser.Parse(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Shared parsing logic for the two date converters. Accepts ISO 8601 strings, the legacy
/// <c>/Date(1234567890123)/</c> form (with optional timezone suffix), and raw epoch-millisecond numbers.
/// </summary>
internal static class SitefinityDateParser
{
    // Matches ServiceStack/Microsoft legacy JSON date format: /Date(1234567890123)/ or /Date(1234567890+0000)/
    private static readonly Regex MsDateRegex = new(
        @"^/Date\((?<ms>-?\d+)(?<tz>[+-]\d{4})?\)/$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static DateTime? Parse(ref Utf8JsonReader reader)
    {
        // Native path — ISO 8601 string.
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString();

            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var match = MsDateRegex.Match(raw);
            if (match.Success)
            {
                var ms = long.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                return dt;
            }

            if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
            {
                return parsed;
            }

            return null;
        }

        // Some serializers emit epoch milliseconds as a number.
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var epochMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime;
        }

        return null;
    }
}
