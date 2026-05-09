namespace OpenEgg.Discord;

public static class TextChunker
{
    public static IReadOnlyList<string> Chunk(string text, int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        if (string.IsNullOrEmpty(text))
        {
            return [string.Empty];
        }

        var chunks = new List<string>();
        var current = new StringWriter();

        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length > limit)
            {
                Flush();
                for (var offset = 0; offset < line.Length; offset += limit)
                {
                    chunks.Add(line.Substring(offset, Math.Min(limit, line.Length - offset)));
                }

                continue;
            }

            var separator = current.GetStringBuilder().Length == 0 ? 0 : 1;
            if (current.GetStringBuilder().Length + separator + line.Length > limit)
            {
                Flush();
            }

            if (current.GetStringBuilder().Length > 0)
            {
                current.WriteLine();
            }

            current.Write(line);
        }

        Flush();
        return chunks;

        void Flush()
        {
            if (current.GetStringBuilder().Length == 0)
            {
                return;
            }

            chunks.Add(current.ToString());
            current.GetStringBuilder().Clear();
        }
    }
}
