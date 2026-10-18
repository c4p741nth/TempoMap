using System.Globalization;
using TimingAnalyzer.Audio;
using TimingAnalyzer.Core;

var invariant = CultureInfo.InvariantCulture;
if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine("usage: timing-analyzer <audio> [bpm] [offset]");
    return 2;
}
double bpm = 0;
double? offset = null;
if (args.Length > 1)
{
    if (!double.TryParse(args[1], NumberStyles.Float, invariant, out bpm) || !double.IsFinite(bpm))
    {
        Console.Error.WriteLine("ERROR: invalid BPM");
        return 2;
    }
}
if (args.Length > 2)
{
    if (!double.TryParse(args[2], NumberStyles.Float, invariant, out double value) || !double.IsFinite(value))
    {
        Console.Error.WriteLine("ERROR: invalid offset");
        return 2;
    }
    offset = value;
}

try
{
    var audio = AudioDecoder.Read(args[0]);
    var result = Analyzer.Analyze(audio.Mono, audio.Rate, bpm, offset);
    if (result.Error is not null)
    {
        Console.WriteLine("ERROR: " + result.Error);
        return 1;
    }
    if (result.ApproximateBpm is double approximate)
    {
        Console.WriteLine("Approximate BPM: " + Format(approximate, 1));
        return 0;
    }
    if (result.Flags != 0)
    {
        Console.WriteLine("Timeline   >>" + result.Timeline + "<<");
        Console.WriteLine("Time (min) >>" + string.Concat(Enumerable.Range(0, result.Timeline!.Length)
            .Select(u => u % 6 == 0 ? (u / 6).ToString(invariant) : " ")) + "<<");
    }
    Console.WriteLine("BPM:    " + Format(result.Bpm!.Value, 3));
    Console.WriteLine("Offset: " + Format(result.Offset!.Value, 0));
    return 0;
}
catch (Exception ex) when (ex is IOException or DllNotFoundException or BadImageFormatException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("ERROR: " + ex.Message);
    return 1;
}

static string Format(double value, int decimals) => Math.Round(value, decimals, MidpointRounding.ToEven)
    .ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).PadLeft(8);
