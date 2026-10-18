namespace TimingAnalyzer.Core;

public readonly record struct TimelineResult(int Flags, string Marks);

public static class Timeline
{
    public static TimelineResult Build(double[] d, double bpm, double offsetInternal)
    {
        int n = d.Length;
        double period = 60000.0 / bpm;
        int nb = (int)Math.Ceiling(n * 0.001 * 0.1);
        var w = new double[nb];
        var m1 = new double[nb];
        var m2 = new double[nb];
        var q = new double[nb];
        var r = new double[nb];
        int half = Peaks.Round(period * 0.7);
        double t = offsetInternal + TimingConstants.OffsetLatency;
        while (t <= n - 1)
        {
            int i = Peaks.Round(t), a = i;
            double p10 = d[i];
            for (int j = Math.Max(i - 10, 0); j < Math.Min(i + 10, n - 1) + 1; j++)
                if (d[j] > p10) { a = j; p10 = d[j]; }
            double p30 = p10;
            for (int j = Math.Max(i - 30, 0); j < Math.Min(i + 30, n - 1) + 1; j++)
                if (d[j] > p30) { a = j; p30 = d[j]; }
            double pw = p30;
            for (int j = Math.Max(i - half, 0); j < Math.Min(i + half, n); j++) pw = Math.Max(pw, d[j]);
            int b = (int)Math.Floor(t * 0.001 * 0.1);
            if (p10 > 0 && Math.Abs(a - t) <= 25 && 0 < a && a < n - 1)
            {
                double dev = (d[a + 1] - d[a - 1]) * 0.5 / ((d[a] - d[a - 1]) + (d[a] - d[a + 1])) + (a - t);
                w[b] += p10;
                m1[b] += p10 * dev;
                m2[b] += p10 * dev * dev;
                q[b] += p30;
            }
            else
            {
                q[b] += pw;
                r[b] += pw - p10;
            }
            t += period;
        }
        double k = period * 0.001 * 0.1;
        int flags = 0;
        var marks = new char[nb];
        for (int b = 0; b < nb; b++)
        {
            double s = q[b] * k, ratio = r[b] / q[b];
            double mu = m1[b] / w[b], sigma = Math.Sqrt(m2[b] / w[b]), amp = w[b] * k;
            double threshold = Math.Sqrt(sigma * 0.2 * sigma + 6);
            int flag = 0;
            if ((s > 3 && ratio > 0.7) || (s > 4 && ratio > 0.5) || (s > 6 && ratio > 0.4) || (s > 10 && ratio > 0.3)) flag = 1;
            else if (s > 3 && mu > threshold) flag = 2;
            else if (s > 3 && -threshold > mu) flag = 4;
            else if (amp > 3 && sigma > 10) flag = 8;
            flags |= flag;
            marks[b] = flag switch { 1 => '#', 2 => '+', 4 => '-', 8 => '.', _ => ' ' };
        }
        return new(flags, new string(marks));
    }
}
