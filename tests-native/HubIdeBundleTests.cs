using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class HubIdeBundleTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        (MemoryStream, HubIdeManifest) Bundle(string extra = "", string brand = "Hub Code") {
            var stream = new MemoryStream(); byte[] exe = Encoding.UTF8.GetBytes("own editor fixture");
            var manifest = new HubIdeManifest { Version = "6.1.0", SourceCommit = new('a', 40), EditorCommit = new('b', 40), ExecutableSha256 = Convert.ToHexStringLower(SHA256.HashData(exe)) };
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) {
                void Put(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
                using (var file = zip.CreateEntry(HubIdeDistribution.ExecutableName).Open()) file.Write(exe);
                Put("resources/app/product.json", JsonSerializer.Serialize(new { nameShort=brand, hubVersion=manifest.Version, hubSourceCommit=manifest.SourceCommit }));
                Put("resources/app/extensions/multiagent-hub/package.json", JsonSerializer.Serialize(new { publisher="multiagent-hub", version=manifest.Version }));
                if (extra.Length > 0) Put(extra, "must not escape");
            }
            stream.Position = 0; manifest.ArchiveBytes = stream.Length; manifest.Sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)); stream.Position = 0;
            return (stream, manifest);
        }
        void Rejected(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid bundle rejection"); }
        await test("own IDE bundle installs its product and Agent; rejects swapped executable and foreign product", () => {
            var (zip, manifest) = Bundle(); using (zip) {
                string target = Path.Combine(root, "own-editor"); HubIdeDistribution.Extract(zip, target, manifest, default);
                HubIdeDistribution.ValidateInstalled(target, manifest);
                File.AppendAllText(Path.Combine(target, HubIdeDistribution.ExecutableName), "tampered");
                Rejected(() => HubIdeDistribution.ValidateInstalled(target, manifest));
            }
            var (foreign, metadata) = Bundle(brand: "VSCodium"); using (foreign) Rejected(() => HubIdeDistribution.Extract(foreign, Path.Combine(root, "foreign-editor"), metadata, default));
            return Task.CompletedTask;
        });
        await test("own IDE bundle rejects corrupt archives and traversal before writing outside cache", () => {
            var (zip, manifest) = Bundle(); using (zip) {
                manifest.Sha256 = new('0', 64); string target = Path.Combine(root, "corrupt-editor");
                Rejected(() => HubIdeDistribution.Extract(zip, target, manifest, default));
                if (Directory.Exists(target)) throw new Exception("Corrupt archive created installation directory");
            }
            foreach (string entry in new[] { "../outside.txt", "..\\outside.txt", "C:/outside.txt", "/outside.txt" }) {
                var (bad, metadata) = Bundle(entry); using (bad) Rejected(() => HubIdeDistribution.Extract(bad, Path.Combine(root, "traversal-" + Guid.NewGuid().ToString("N")), metadata, default));
            }
            if (File.Exists(Path.Combine(root, "outside.txt"))) throw new Exception("Archive escaped cache");
            return Task.CompletedTask;
        });
    }
}
