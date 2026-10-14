using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultiAgentHub.Core;

public sealed class AgentQuotaException() : Exception("Agent đã hết quota hoặc chạm hạn mức sử dụng.");

public static class Quota
{
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase) {
        "insufficient_quota", "quota_exceeded", "usage_limit_reached", "usage_limit_exceeded",
        "organization_usage_limit_exceeded", "organization_spend_limit_exceeded", "project_spend_limit_exceeded",
        "billing_hard_limit_reached", "insufficient_credits", "credit_balance_exhausted", "payment_required"
    };
    // Only inspect explicit errors, never successful answers, prompts or tool output.
    public static bool Exhausted(string value)
    {
        if (Codes.Any(code => Regex.IsMatch(value, @"\b" + Regex.Escape(code) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) return true;
        var normalized = new StringBuilder();
        foreach (char c in value.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) normalized.Append(c == 'đ' ? 'd' : c);
        string text = normalized.ToString().Replace('’', '\'');
        return Regex.IsMatch(text, @"\b(?:quota (?:exceeded|exhausted|has been exhausted)|insufficient (?:quota|credits)|exceeded your (?:current )?quota|(?:hit|reached|exceeded) (?:your |the |its |an? |current |monthly |weekly |daily |session )*(?:usage limit|spend(?:ing)? limit|spend cap|usage cap)|(?:usage|spend(?:ing)?) limit (?:has been )?(?:reached|exceeded)|credit balance (?:is )?(?:too low|depleted|exhausted)|(?:you've|you have) (?:hit|reached) your limit|out of (?:credits|quota)|het (?:quota|han muc|luot su dung)|da (?:vuot|dat) (?:han muc|gioi han su dung))\b", RegexOptions.CultureInvariant);
    }
    public static bool Error(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String) return Exhausted(error.GetString() ?? "");
        if (error.ValueKind != JsonValueKind.Object) return false;
        if (Codes.Contains(error.Str("code")) || Codes.Contains(error.Str("type")) || Codes.Contains(error.Str("status"))) return true;
        string message = error.Str("message");
        // Gemini's RESOURCE_EXHAUSTED also covers short per-minute limits.
        if (error.Str("status") == "RESOURCE_EXHAUSTED" && error.TryGetProperty("details", out var details))
        {
            string limits = details.GetRawText();
            if (limits.Contains("PerMinute", StringComparison.OrdinalIgnoreCase) && !limits.Contains("PerDay", StringComparison.OrdinalIgnoreCase) && !limits.Contains("limit: 0", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return Exhausted(message);
    }
    public static bool ApiError(string body)
    {
        try { using var doc = JsonDocument.Parse(body); return doc.RootElement.TryGetProperty("error", out var error) && Error(error); }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return false; }
    }
}
