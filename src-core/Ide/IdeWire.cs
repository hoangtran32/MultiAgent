using System.Text.Json;

namespace MultiAgentHub.Core;

public static class IdeWire
{
    private static readonly JsonSerializerOptions Options = new(Json.Options) { WriteIndented = false };
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);
}
