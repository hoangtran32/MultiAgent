using System.IO.Pipes;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

/// <summary>Runs before WinForms initialization; stdout is exclusively native messaging frames.</summary>
internal static class BrowserNativeHost
{
    public static bool IsInvocation(string[] args) => args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 0 || !BrowserChatProtocol.IsExtensionOrigin(args[0])) return 1;
        try {
            using var closing = new CancellationTokenSource();
            using var pipe = new NamedPipeClientStream(".", BrowserChatProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(8000, closing.Token);
            using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput();
            var inbound = Relay(input, pipe, closing.Token);
            var outbound = Relay(pipe, output, closing.Token);
            var finished = await Task.WhenAny(inbound, outbound);
            bool failed = finished.IsFaulted;
            closing.Cancel(); pipe.Dispose(); input.Dispose();
            // Some Windows stdin handles do not honour asynchronous cancellation immediately.
            try { await Task.WhenAll(inbound, outbound).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException) { }
            return failed ? 1 : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException or ObjectDisposedException) { return 1; }
    }

    private static async Task Relay(Stream source, Stream destination, CancellationToken token)
    {
        while (await BrowserChatProtocol.ReadFrame(source, token) is { } frame)
            await BrowserChatProtocol.WriteFrame(destination, frame, token);
    }
}
