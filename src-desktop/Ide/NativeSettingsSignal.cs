using System.IO.Pipes;
using System.Text;

namespace MultiAgentHubApp;

internal sealed class NativeSettingsSignal : IDisposable
{
    private readonly CancellationTokenSource closing = new();
    private readonly Task listener;
    public NativeSettingsSignal(string instance, Action open)
    {
        listener = Task.Run(async () => {
            try {
                while (!closing.IsCancellationRequested) {
                    using var pipe = new NamedPipeServerStream("MultiAgentHub.Settings." + instance, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(closing.Token);
                    byte[] request = new byte[8]; int count = await pipe.ReadAsync(request, closing.Token);
                    if (Encoding.ASCII.GetString(request, 0, count) == "settings") open();
                }
            } catch (OperationCanceledException) { } catch (IOException) { }
        });
    }
    public static async Task<bool> Request(string instance)
    {
        try { using var pipe = new NamedPipeClientStream(".", "MultiAgentHub.Settings." + instance, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await pipe.ConnectAsync(1500); await pipe.WriteAsync("settings"u8.ToArray()); return true; }
        catch (Exception error) when (error is IOException or TimeoutException) { return false; }
    }
    public void Dispose() { closing.Cancel(); _ = listener.ContinueWith(_ => closing.Dispose(), TaskScheduler.Default); }
}
