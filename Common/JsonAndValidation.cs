using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace KalyanaVaran.Api.Common;

/// <summary>Writes DateTime as 2026-10-23T05:30:00.000Z (UTC, millisecond precision).</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        writer.WriteStringValue(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Lets text fields also accept JSON numbers: "otp": 842910 is read as "842910",
/// "dobDay": 14 as "14", "weightKg": 62 as "62". The value is then validated as usual.
/// </summary>
public sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(
                reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            _ => throw new JsonException()
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

public static class ValidationResponse
{
    private const string JsonTypeError = "The JSON value could not be converted";

    /// <summary>Turns MVC model-state errors into { code: VALIDATION_ERROR, message, details: { field: msg } }.</summary>
    public static IActionResult Create(ActionContext ctx)
    {
        // Names of the action's parameters (e.g. "req", "body"): MVC adds "The req field is required."
        // whenever the body can't be read, which only confuses the caller.
        var paramNames = ctx.ActionDescriptor.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var details = new Dictionary<string, string>();
        var bodyMissing = false;
        foreach (var (key, entry) in ctx.ModelState)
        {
            if (entry.Errors.Count == 0) continue;
            if (paramNames.Contains(key)) { bodyMissing = true; continue; }

            var field = ToCamelPath(key);
            var msg = entry.Errors[0].ErrorMessage;
            if (string.IsNullOrWhiteSpace(msg)) msg = entry.Errors[0].Exception?.Message ?? "Invalid value.";

            if (field == "body")
                msg = "The request body is not valid JSON. Check the commas, quotes and brackets.";
            else if (msg.StartsWith(JsonTypeError, StringComparison.Ordinal))
                msg = $"'{field}' has the wrong type. Check the value; text must be in double quotes, e.g. \"{field}\": \"value\".";

            details.TryAdd(field, msg);
        }

        if (details.Count == 0 && bodyMissing)
            details["body"] = "The request body is missing. Send a JSON body with Content-Type: application/json.";

        var first = details.Values.FirstOrDefault() ?? "The request is invalid.";
        var body = new ApiResponse<object>(false, null,
            new ApiError(ErrorCodes.ValidationError, first, details), ApiMeta.For(ctx.HttpContext));
        return new BadRequestObjectResult(body);
    }

    private static string ToCamelPath(string key)
    {
        if (key.StartsWith("$.")) key = key[2..];
        if (key is "" or "$") return "body";
        return string.Join('.', key.Split('.').Select(p =>
            p.Length > 0 && char.IsUpper(p[0]) ? char.ToLowerInvariant(p[0]) + p[1..] : p));
    }
}
