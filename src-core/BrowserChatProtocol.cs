using System.Buffers.Binary;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Chrome native messaging frames, also used between the native host and Hub.</summary>
public static class BrowserChatProtocol
{
    public const string HostName = "com.multiagenthub.browser_chat";
    public const string PipeName = "MultiAgentHub.BrowserChat.v1";
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

/// <summary>Model is the user's confirmed selection in the browser, not a claim about the service's backend.</summary>
public sealed record BrowserChatBinding(string Id, string Provider, int TabId, string Url, string Title, string Model, bool Ready);
