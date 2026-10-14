using System.Text;
using System.Text.RegularExpressions;

namespace MultiAgentHubApp;

internal sealed record ChatSection(string Title, string Meta, string Text, bool User = false, string Key = "", string Logs = "", string Prompt = "");
internal sealed record ChatLink(int Start, int Length, string Url);

/// <summary>Produces native RichEdit content in one assignment, without a browser or HTML surface.</summary>
internal sealed class NativeMarkdown(int userIndentTwips = 2400)
{
    private readonly StringBuilder rtf = new("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Segoe UI;}{\\f1 Consolas;}}{\\colortbl;\\red43\\green45\\blue44;\\red245\\green247\\blue245;\\red130\\green135\\blue132;\\red55\\green125\\blue239;\\red231\\green234\\blue230;}\\viewkind4\\uc1 ");
    private int position;
    public List<ChatLink> Links { get; } = [];
    private static readonly Regex tokens = new(@"\*\*([^*]+)\*\*|__([^_]+)__|`([^`]+)`|\[([^\]]+)\]\((https?://[^\s)]+)\)|(?<!\*)\*([^*]+)\*(?!\*)", RegexOptions.Compiled, TimeSpan.FromMilliseconds(150));
    private void Text(string value)
    {
        foreach (char c in value) {
            if (c is '\\' or '{' or '}') rtf.Append('\\').Append(c);
            else if (c == '\n') rtf.Append("\\line ");
            else if (c == '\t') rtf.Append("\\tab ");
            else if (c > 127) rtf.Append("\\u").Append((short)c).Append('?');
            else if (c != '\r') rtf.Append(c);
            if (c != '\r') position++;
        }
    }
    private void Paragraph() { rtf.Append("\\par\n"); position++; }
    private void BeginParagraph(ChatSection section, int extraLeft = 0, int extraRight = 0, bool code = false)
    {
        rtf.Append("\\pard").Append(section.User && !code ? "\\qr" : "\\ql")
            .Append("\\li").Append((section.User ? Math.Max(0, userIndentTwips) : 0) + extraLeft)
            .Append("\\ri").Append(extraRight);
    }
    private void Inline(string value)
    {
        int index = 0;
        try {
            foreach (Match match in tokens.Matches(value)) {
                Text(value[index..match.Index]); rtf.Append('{');
                if (match.Groups[1].Success || match.Groups[2].Success) { rtf.Append("\\b "); Text(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value); }
                else if (match.Groups[3].Success) { rtf.Append("\\f1\\fs18\\highlight2 "); Text(match.Groups[3].Value); }
                else if (match.Groups[4].Success) {
                    rtf.Append("\\cf4\\ul "); int start = position; Text(match.Groups[4].Value); Links.Add(new(start, position - start, match.Groups[5].Value));
                } else { rtf.Append("\\i "); Text(match.Groups[6].Value); }
                rtf.Append('}'); index = match.Index + match.Length;
            }
            Text(value[index..]);
        } catch (RegexMatchTimeoutException) { Text(value[index..]); }
    }
    public string Render(IEnumerable<ChatSection> sections, bool includeHeader = true)
    {
        foreach (var section in sections) {
            if (includeHeader) {
                BeginParagraph(section); rtf.Append("\\f0\\fs19\\cf1\\highlight0\\i0\\ul0\\b\\sb180\\sa40 "); Text(section.Title); Paragraph();
                BeginParagraph(section); rtf.Append("\\b0\\fs16\\cf3\\highlight0\\sb0\\sa180 "); Text(section.Meta); Paragraph();
            }
            string text = section.Text.Length > 100000 ? section.Text[..100000] + "\n[…mở Chi tiết để xem toàn bộ]" : section.Text;
            bool code = false; var lines = text.ReplaceLineEndings("\n").Split('\n');
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                if (line.TrimStart().StartsWith("```")) {
                    code = !code;
                    if (code) { string language = line.Trim().TrimStart('`'); if (language.Length > 0) { BeginParagraph(section); rtf.Append("\\f0\\fs16\\cf3\\highlight0\\b0\\sa60 "); Text(language); Paragraph(); } }
                    continue;
                }
                if (code) { BeginParagraph(section, 120, 120, code: true); rtf.Append("\\f1\\fs18\\cf1\\b0\\i0\\ul0\\highlight2\\sa20\\sl240\\slmult1 "); Text(line); Paragraph(); continue; }
                if (line.Contains('|') && line.Split('|', StringSplitOptions.RemoveEmptyEntries).All(cell => Regex.IsMatch(cell.Trim(), @"^:?-{3,}:?$"))) continue;
                if (Regex.IsMatch(line, @"^\s*([-*_])(?:\s*\1){2,}\s*$")) { BeginParagraph(section); rtf.Append("\\brdrb\\brdrs\\brdrw5\\brdrcf5\\sa120 "); Paragraph(); continue; }
                int heading = line.TakeWhile(c => c == '#').Count();
                bool headingLine = heading is >= 1 and <= 6 && line.Length > heading && line[heading] == ' ';
                bool quote = !headingLine && line.TrimStart().StartsWith('>');
                BeginParagraph(section, quote ? 180 : 0);
                rtf.Append("\\f0\\fs").Append(headingLine ? heading == 1 ? 25 : 22 : 19).Append("\\cf1\\highlight0\\sl270\\slmult1\\sa80\\sb").Append(headingLine ? "160\\b " : "0\\b0 ");
                if (headingLine) line = line[(heading + 1)..];
                else if (Regex.IsMatch(line, @"^\s*[-*+]\s+")) line = Regex.Replace(line, @"^\s*[-*+]\s+", "• ");
                else if (quote) { line = line.TrimStart().TrimStart('>').TrimStart(); rtf.Append("\\cf3 "); }
                if (line.Contains('|') && (line.TrimStart().StartsWith('|') || i + 1 < lines.Length && lines[i + 1].Contains("---"))) line = string.Join("   ·   ", line.Trim().Trim('|').Split('|').Select(c => c.Trim()));
                Inline(line); Paragraph();
            }
            if (includeHeader) { rtf.Append("\\pard\\ql\\li0\\ri0\\fs12\\b0\\highlight0\\sa180 "); Paragraph(); }
        }
        return rtf.Append('}').ToString();
    }
}
