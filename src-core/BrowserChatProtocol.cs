using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Chrome native messaging frames, also used between the native host and Hub.</summary>
public static class BrowserChatProtocol
{
    public const string HostName = "com.multiagenthub.browser_chat";
    // Keep separate Windows accounts/sessions from colliding on the globally named pipe namespace.
    public static string PipeName { get; } = "MultiAgentHub.BrowserChat.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.UserDomainName + "\\" + Environment.UserName + ":" + (OperatingSystem.IsWindows() ? Process.GetCurrentProcess().SessionId : 0))))[..24];
    public const string ExtensionId = "koeckebjfmkgjanknbifdcgahkbbalpb";
    public const int MaxFrameBytes = 1024 * 1024;
    public const int MaxPromptCharacters = 200000;
    public const int MaxTextCharacters = 64000;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions WireJson = new(Json.Options) { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<byte[]?> ReadFrame(Stream stream, CancellationToken token = default)
    {
        var header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > MaxFrameBytes) throw new InvalidDataException("Khung kết nối trình duyệt vượt giới hạn 1 MiB hoặc rỗng.");
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, token);
        return payload;
    }

    public static async Task WriteFrame(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken token = default)
    {
        if (payload.Length == 0 || payload.Length > MaxFrameBytes) throw new InvalidDataException("Khung kết nối trình duyệt vượt giới hạn 1 MiB hoặc rỗng.");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
    public static JsonDocument Parse(ReadOnlyMemory<byte> payload)
    {
        // Reject malformed UTF-8 before JSON parsing; no replacement characters in prompts or model labels.
        Utf8.GetCharCount(payload.Span);
        return JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 });
    }
    public static bool IsExtensionOrigin(string value) => string.Equals(value, $"chrome-extension://{ExtensionId}/", StringComparison.Ordinal);
}

/// <summary>Selection is stable across turns. Observed metadata is website data, not backend attestation.</summary>
public sealed record BrowserChatBinding(string Id, string Provider, int TabId, string Url, string Title, string Model, bool Ready)
{
    public List<BrowserApiModel> ApiModels { get; init; } = [];
    public string SiteName { get; init; } = "";
    public string DetectedModel { get; init; } = "";
    public string ModelSource { get; init; } = "unknown";
    public string Transport { get; init; } = "dom";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayModel => DetectedModel.Length > 0 ? DetectedModel + (ModelSource == "response" ? " • phản hồi web" : " • giao diện")
        : Model == "Theo website" ? "Theo website • chưa lộ model" : Model + " • nhãn người dùng";
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplaySite => SiteName.Length > 0 ? SiteName : new Uri(Url).Host;
}

public sealed record BrowserApiModel(string Id, string Name, string Protocol, string Source);
