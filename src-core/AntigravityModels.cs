using System.Text;
using System.Text.RegularExpressions;
namespace MultiAgentHub.Core;

public static class AntigravityModels
{
    public static List<string> Parse(string output) => Regex.Replace(output, @"\x1b\[[0-9;]*[a-zA-Z]", "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
        .Where(id => Regex.IsMatch(id, "^[a-zA-Z0-9][a-zA-Z0-9._/-]{1,119}$") && id.Contains('-') && !id.StartsWith('-'))
        .Distinct().ToList();
    public static async Task<List<string>> Load(AgentConfig agent, CancellationToken token)
    {
        string binary = ProcessTransport.Find(agent) ?? throw new FileNotFoundException("Chưa tìm thấy agy.exe. Cài Antigravity CLI trước.");
        var output = new StringBuilder(); var errors = new StringBuilder();
        try {
            await ProcessTransport.Run(new(binary, ["models"], ""), Path.GetTempPath(), s => { if (output.Length < 100000) output.Append(s); }, s => { if (errors.Length < 16000) errors.Append(s); }, token);
        } catch (Exception e) when (e is not OperationCanceledException) {
            var diagnostic = CliDiagnostics.Explain(agent, errors.ToString());
            if (diagnostic != null) throw diagnostic;
            throw;
        }
        var models = Parse(output.ToString()); if (models.Count == 0) {
            var diagnostic = CliDiagnostics.Explain(agent, errors.ToString());
            if (diagnostic != null) throw diagnostic;
            throw new InvalidDataException("CLI chưa trả danh sách model. Chạy agy models trong terminal để kiểm tra kết nối; chưa xác định là lỗi đăng nhập.");
        }
        return models;
    }
}
