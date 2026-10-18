using System.Text;
using System.Text.RegularExpressions;

namespace EvolutionaryLogistics.Services.Ai.Rag;

/// <summary>
/// Yapısal + örtüşmeli (overlap) parçalama. Sabit boyutlu kör kesme DEĞİLDİR:
/// önce başlıklara (#, ##, ###), sonra paragraflara bölünür; yalnızca tek paragraf
/// <paramref name="maxChars"/>'ı aşarsa örtüşmeli kayan pencere uygulanır.
/// Bu sayede her chunk anlamlı bir bütün kalır ve komşu chunk'lar arası bağlam korunur.
/// </summary>
public static class DocumentChunker
{
    private static readonly Regex HeadingPattern = new(@"(?m)^#{1,3}\s", RegexOptions.Compiled);
    private static readonly Regex ParagraphPattern = new(@"\n\s*\n", RegexOptions.Compiled);

    public static IEnumerable<string> Chunk(string text, int maxChars = 2000, int overlapChars = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var section in SplitByHeadings(text))
        {
            foreach (var chunk in ChunkSection(section, maxChars, overlapChars))
            {
                yield return chunk;
            }
        }
    }

    private static IEnumerable<string> SplitByHeadings(string text)
    {
        var headings = HeadingPattern.Matches(text);
        if (headings.Count == 0)
        {
            yield return text;
            yield break;
        }

        var first = headings[0].Index;
        if (first > 0)
        {
            var preamble = text[..first].Trim();
            if (preamble.Length > 0)
            {
                yield return preamble;
            }
        }

        for (var i = 0; i < headings.Count; i++)
        {
            var start = headings[i].Index;
            var end = i + 1 < headings.Count ? headings[i + 1].Index : text.Length;
            var section = text[start..end].Trim();
            if (section.Length > 0)
            {
                yield return section;
            }
        }
    }

    private static IEnumerable<string> ChunkSection(string section, int maxChars, int overlapChars)
    {
        if (section.Length <= maxChars)
        {
            yield return section;
            yield break;
        }

        var buffer = new StringBuilder();
        foreach (var raw in ParagraphPattern.Split(section))
        {
            var paragraph = raw.Trim();
            if (paragraph.Length == 0)
            {
                continue;
            }

            if (paragraph.Length > maxChars)
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString().Trim();
                    buffer.Clear();
                }

                foreach (var window in SlidingWindow(paragraph, maxChars, overlapChars))
                {
                    yield return window;
                }

                continue;
            }

            if (buffer.Length > 0 && buffer.Length + paragraph.Length + 2 > maxChars)
            {
                yield return buffer.ToString().Trim();
                buffer.Clear();
            }

            if (buffer.Length > 0)
            {
                buffer.Append("\n\n");
            }

            buffer.Append(paragraph);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
    }

    private static IEnumerable<string> SlidingWindow(string text, int maxChars, int overlapChars)
    {
        var overlap = overlapChars >= maxChars ? maxChars / 4 : overlapChars;
        var step = Math.Max(1, maxChars - overlap);

        for (var start = 0; start < text.Length; start += step)
        {
            var length = Math.Min(maxChars, text.Length - start);
            yield return text.Substring(start, length).Trim();

            if (start + length >= text.Length)
            {
                yield break;
            }
        }
    }
}
