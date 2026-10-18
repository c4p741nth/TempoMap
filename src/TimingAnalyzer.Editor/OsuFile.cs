using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TimingAnalyzer.Editor;

/// <summary>Lossless UTF-8 document. Only red timing rows are regenerated on edit.</summary>
public sealed class OsuFile
{
    private sealed record Line(string Text, string Ending);
    private readonly byte[] original;
    private readonly Line[] lines;
    private readonly Dictionary<int, TimingPoint> rows = new();
    private readonly bool bom;
    private readonly string newline;
    private int header = -1, end;
    public IReadOnlyList<TimingPoint> TimingPoints { get; }
    /// <summary>
    /// Red lines sorted by time. Of red lines sharing a time the first in the file is kept, as osu! does (lazer's
    /// LegacyBeatmapDecoder skips a later timing point at the same time); 10 of 6 065 local maps have such pairs.
    /// </summary>
    public IReadOnlyList<TimingPoint> RedLines { get; }
    /// <summary>Red lines left out of <see cref="RedLines"/> because an earlier one has the same time.</summary>
    public int DuplicateRedLines { get; }
    public string? AudioFilename { get; }
    /// <summary>
    /// End of the last hit object (a hold's or spinner's end time, else its start; a slider counts from its start, as
    /// its length needs the slider maths); null without hit objects. osu!'s main BPM is measured up to here.
    /// </summary>
    public double? LastObjectTime { get; }
    /// <summary>[Metadata] fields (Title, TitleUnicode, Artist, ArtistUnicode, Creator, Version, ...) as written.</summary>
    public IReadOnlyDictionary<string, string> Metadata => metadata;
    private readonly Dictionary<string, string> metadata = new(StringComparer.Ordinal);
    /// <summary>"Artist - Title [Version] (Creator)", leaving out missing parts; null without a title.</summary>
    public string? SongTitle
    {
        get
        {
            string Field(string key) => metadata.TryGetValue(key, out string? value) ? value : "";
            string title = Field("Title"), artist = Field("Artist"), version = Field("Version"), creator = Field("Creator");
            if (title.Length == 0) return null;
            return (artist.Length > 0 ? artist + " - " : "") + title + (version.Length > 0 ? " [" + version + "]" : "") +
                (creator.Length > 0 ? " (" + creator + ")" : "");
        }
    }

    private OsuFile(byte[] bytes)
    {
        original = bytes.ToArray();
        bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        string text = new UTF8Encoding(false, true).GetString(bytes.AsSpan(bom ? 3 : 0));
        lines = Regex.Matches(text, @"([^\r\n]*)(\r\n|\n|\r|$)")
            .Where(m => m.Length > 0).Select(m => new Line(m.Groups[1].Value, m.Groups[2].Value)).ToArray();
        newline = lines.FirstOrDefault(l => l.Ending.Length > 0)?.Ending ?? "\r\n";
        string section = "";
        end = lines.Length;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Text.Trim();
            if (line.StartsWith('['))
            {
                if (section == "[TimingPoints]") end = i;
                section = line;
                if (section == "[TimingPoints]") header = i;
            }
            else if (section == "[General]" && line.StartsWith("AudioFilename:", StringComparison.Ordinal))
                AudioFilename = line["AudioFilename:".Length..].Trim();
            else if (section == "[Metadata]" && line.IndexOf(':') is > 0 and var colon)
                metadata.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
            else if (section == "[TimingPoints]" && line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
                rows.Add(i, TimingPoint.Parse(line));
            else if (section == "[HitObjects]" && ObjectEnd(line) is { } objectEnd)
                LastObjectTime = Math.Max(LastObjectTime ?? objectEnd, objectEnd);
        }
        TimingPoints = Array.AsReadOnly(rows.Values.ToArray());
        var times = new HashSet<double>();
        var reds = TimingPoints.Where(p => p.Uninherited && times.Add(p.Time)).OrderBy(p => p.Time).ToArray();
        DuplicateRedLines = TimingPoints.Count(p => p.Uninherited) - reds.Length;
        RedLines = Array.AsReadOnly(reds);
    }

    /// <summary>x,y,time,type,…: a hold (type 128) ends at field 5 before ':', a spinner (type 8) at field 5.</summary>
    private static double? ObjectEnd(string line)
    {
        var f = line.Split(',');
        if (f.Length < 4 || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double time) ||
            !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)) return null;
        if ((type & (128 | 8)) != 0 && f.Length > 5 &&
            double.TryParse(f[5].Split(':')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double end))
            return Math.Max(time, end);
        return time;
    }

    public static OsuFile Parse(byte[] bytes) => new(bytes);
    public static OsuFile Load(string path) => Parse(File.ReadAllBytes(path));
    public static OsuFile Create(string audioFilename)
    {
        if (audioFilename.IndexOfAny(['\r', '\n']) >= 0) throw new ArgumentException("Invalid audio filename.");
        return Parse(Encoding.UTF8.GetBytes("osu file format v14\r\n\r\n[General]\r\nAudioFilename: " + audioFilename +
            "\r\nMode: 0\r\n\r\n[Metadata]\r\nTitle:Untitled\r\nArtist:Unknown\r\nCreator:Timing Analyzer\r\nVersion:Timing\r\n\r\n" +
            "[Difficulty]\r\nHPDrainRate:5\r\nCircleSize:4\r\nOverallDifficulty:5\r\nApproachRate:5\r\nSliderMultiplier:1.4\r\nSliderTickRate:1\r\n\r\n" +
            "[Events]\r\n\r\n[TimingPoints]\r\n\r\n[HitObjects]\r\n"));
    }

    public byte[] WithRedLines(IEnumerable<TimingPoint> redLines)
    {
        var reds = redLines.Select(p => p.Validate()).ToArray();
        if (reds.Any(p => !p.Uninherited)) throw new ArgumentException("Expected red lines only.");
        // Unchanged timing keeps the file's bytes, duplicates included; an edit writes each red line once.
        if (reds.SequenceEqual(TimingPoints.Where(p => p.Uninherited)) || reds.SequenceEqual(RedLines)) return original.ToArray();
        var output = rows.Where(r => !r.Value.Uninherited)
            .Select(r => (Point: r.Value, Text: lines[r.Key].Text))
            .Concat(reds.Select(p => (Point: p, Text: p.ToOsuLine())))
            // Only red times are rounded; green rows retain their original (possibly fractional) times.
            .OrderBy(r => r.Point.SerializedTime)
            .ThenBy(r => !r.Point.Uninherited).Select(r => r.Text).ToArray();
        var b = new StringBuilder();
        int next = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == end && header >= 0)
                while (next < output.Length) b.Append(output[next++]).Append(newline);
            if (rows.ContainsKey(i))
            {
                if (next < output.Length) b.Append(output[next++]).Append(lines[i].Ending.Length > 0 ? lines[i].Ending : newline);
            }
            else b.Append(lines[i].Text).Append(lines[i].Ending);
        }
        if (header < 0)
        {
            if (b.Length > 0 && b[^1] != '\n' && b[^1] != '\r') b.Append(newline);
            b.Append("[TimingPoints]").Append(newline);
        }
        if (next < output.Length && b.Length > 0 && b[^1] != '\n' && b[^1] != '\r') b.Append(newline);
        while (next < output.Length) b.Append(output[next++]).Append(newline);
        return (bom ? Encoding.UTF8.Preamble.ToArray() : []).Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray();
    }

    public void Save(string path, IEnumerable<TimingPoint> redLines)
    {
        byte[] bytes = WithRedLines(redLines);
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.WriteAllBytes(path, bytes);
    }
}
