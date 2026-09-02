using System.Text.Json;

namespace Truvio.Commerce.BuyingAssistant.Core.Assistant;

/// <summary>Small helpers for building tool input schemas without a schema library.</summary>
internal static class JsonSchema
{
    public static JsonElement Str(string description) => JsonSerializer.SerializeToElement(new { type = "string", description });

    public static JsonElement Num(string description) => JsonSerializer.SerializeToElement(new { type = "number", description });

    public static JsonElement Int(string description) => JsonSerializer.SerializeToElement(new { type = "integer", description });

    public static JsonElement Bool(string description) => JsonSerializer.SerializeToElement(new { type = "boolean", description });

    public static JsonElement StrArray(string description) => JsonSerializer.SerializeToElement(new { type = "array", items = new { type = "string" }, description });

    public static JsonElement Raw(object anonymous) => JsonSerializer.SerializeToElement(anonymous);

    public static JsonElement Element(JsonElement e) => e;
}
