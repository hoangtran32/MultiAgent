using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MultiAgentHub.Core;

internal static class ChatGptPlanTests
{
    private static void Assert(bool condition) { if (!condition) throw new Exception("ChatGPT plan assertion failed."); }
    private static async Task Reject(Func<Task> action) { try { await action(); } catch (Exception error) when (error is InvalidDataException or ChatGptPlanException) { return; } throw new Exception("Expected invalid/incomplete ChatGPT result."); }
    private static string Event(object payload) => "data: " + JsonSerializer.Serialize(payload) + "\r\n\r\n";
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("ChatGPT plan validates JWT signature issuer audience expiry and nonce before accepting identity", async () => {
            using var rsa = RSA.Create(2048); using var other = RSA.Create(2048);
            var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
            string jwks = JsonSerializer.Serialize(new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(key) } });
            string Signed(string issuer = "https://auth.openai.com", string audience = "oaiapp_fixture", string nonce = "nonce", bool expired = false, RSA? signer = null) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
                Issuer = issuer, Audience = audience, IssuedAt = DateTime.UtcNow.AddHours(-1), NotBefore = DateTime.UtcNow.AddHours(-1), Expires = expired ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5),
                Claims = new Dictionary<string, object> { ["sub"] = "subject", ["email"] = "fixture@example.test", ["nonce"] = nonce }, SigningCredentials = new SigningCredentials(signer == null ? key : new RsaSecurityKey(signer) { KeyId = "fixture" }, SecurityAlgorithms.RsaSha256) });
            var identity = await ChatGptPlan.VerifyIdentity(Signed(), "oaiapp_fixture", "nonce", jwks); Assert(identity.Subject == "subject");
            foreach (string invalid in new[] { Signed(issuer: "https://wrong.example"), Signed(audience: "other_client"), Signed(nonce: "other"), Signed(expired: true), Signed(signer: other) })
                await Reject(() => ChatGptPlan.VerifyIdentity(invalid, "oaiapp_fixture", "nonce", jwks));
        });
        await test("ChatGPT plan stream waits for completed event and reports cumulative Unicode text", async () => {
            var seen = new List<string>();
            string input = ": heartbeat\r\n\r\n" + Event(new { type = "response.output_text.delta", delta = "Chào " }) + Event(new { type = "response.output_text.delta", delta = "bạn 👋" }) + Event(new { type = "response.completed" });
            string answer = await ChatGptPlanStream.Read(new StringReader(input), seen.Add, default);
            Assert(answer == "Chào bạn 👋" && seen.SequenceEqual(new[] { "Chào ", "Chào bạn 👋" }));
        });
        await test("ChatGPT plan retains streamed text but rejects EOF incomplete and usage errors", async () => {
            string prefix = Event(new { type = "response.output_text.delta", delta = "Saved partial" });
            foreach (string suffix in new[] { "", "data: [DONE]\n\n", Event(new { type = "response.incomplete" }), Event(new { type = "response.failed", response = new { error = new { code = "subscription_sharing_usage_limit_exceeded", message = "do not echo provider secrets" } } }) }) {
                string received = ""; await Reject(async () => { await ChatGptPlanStream.Read(new StringReader(prefix + suffix), s => received = s, default); }); Assert(received == "Saved partial");
            }
            await Reject(async () => { await ChatGptPlanStream.Read(new StringReader(Event(new { type = "response.completed" })), _ => { }, default); });
        });
        await test("ChatGPT plan SSE accepts split multi-line events and rejects unbounded lines", async () => {
            string input = "data: {\"type\":\"response.output_text.delta\",\n" + "data: \"delta\":\"OK\"}\n\n" + Event(new { type = "response.completed" });
            Assert(await ChatGptPlanStream.Read(new StringReader(input), _ => { }, default) == "OK");
            await Reject(async () => { await ChatGptPlanStream.Read(new StringReader("data: " + new string('x', 2 * 1024 * 1024)), _ => { }, default); });
        });
        await test("ChatGPT plan credentials stay encrypted and refresh lock excludes concurrent writers", async () => {
            if (!OperatingSystem.IsWindows()) return;
            var store = new ChatGptPlanStore(Path.Combine(root, "plan-secrets"));
            var session = new ChatGptPlanStore.Session { ClientId = "oaiapp_fixture", Subject = "fixture", Label = "Fixture", AccessToken = new string('a', 5000), RefreshToken = "sensitive-refresh", IdToken = "sensitive-id", Scopes = ["chatgpt.tokens.use.direct"] };
            store.Save(session); Assert(store.Load(session.Id).AccessToken.Length == 5000 && store.Accounts.Single().PlanEnabled);
            string path = Path.Combine(root, "plan-secrets", "chatgpt-plan", session.Id + ".protected");
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("sensitive-refresh"));
            using (var held = await store.Lock(session.Id, default)) {
                using var cancel = new CancellationTokenSource(250); bool cancelled = false;
                try { using var blocked = await store.Lock(session.Id, cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Assert(cancelled);
            }
            store.ClearTokens(session); Assert(!store.Accounts.Single().SignedIn && store.Load(session.Id).ClientId == "oaiapp_fixture");
        });
    }
}
