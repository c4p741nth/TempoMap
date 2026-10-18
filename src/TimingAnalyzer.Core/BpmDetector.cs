namespace TimingAnalyzer.Core;

public readonly record struct BpmResult(int Status, double? Bpm);

public static class BpmDetector
{
    public static BpmResult Detect(double[] d)
    {
        int nl = (int)Math.Floor(d.Length * 0.5);
        var a = Fft.Autocorrelate(d, nl);
        int maxL = Math.Min(nl, 2000);
        var peaks = new List<int>();
        for (int l = 2; l < maxL - 2; l++)
            if (a[l] > 0 && a[l - 1] < a[l] && a[l + 1] < a[l] && a[l - 2] < a[l - 1] && a[l + 2] < a[l + 1])
                peaks.Add(l);

        var candidates = new List<double>();
        double best = 0;
        int pick = -1;
        for (int ci = 0; ci < peaks.Count; ci++)
        {
            int lag = peaks[ci];
            if (lag > 999) break;
            int t = lag, k = 1, hits = 0, misses = 0, u = ci;
            double skk = 0, skx = 0, hsum = 0, slope = lag;
            while (t < maxL - 10 && misses == 0)
            {
                bool found = false;
                while (u < peaks.Count)
                {
                    if (peaks[u] >= t - 10)
                    {
                        found = peaks[u] <= t + 10;
                        break;
                    }
                    u++;
                }
                double next;
                if (found)
                {
                    int q = peaks[u];
                    hits++;
                    hsum += a[q];
                    double x = Peaks.Interpolate(a, q);
                    skk += (double)k * k;
                    skx += k * x;
                    slope = skx / skk;
                    next = x + slope;
                }
                else
                {
                    misses++;
                    next = t + slope;
                }
                t = Peaks.Round(next);
                k++;
            }
            if (hits > 0 && hsum / hits > a[lag] * 0.7)
            {
                double mean = hsum / hits;
                if (best * 1.2 < mean) pick = candidates.Count;
                best = Math.Max(best, mean);
                candidates.Add(skx / skk);
            }
        }
        if (!(best > 0)) return new(-1, null);
        double period = candidates[pick];
        if (period < TimingConstants.FastPeriod)
            for (int j = pick + 1; j < candidates.Count; j++)
                if ((candidates[j] + 10.0) % period <= 20.0)
                {
                    period = candidates[j];
                    break;
                }

        int atTime = Peaks.Round(period), beat = 1, m = 0, miss = 0;
        double sumKk = 0, sumKx = 0, sumXx = 0, fittedSlope = period;
        while (atTime < nl - 10 && miss == 0)
        {
            int at = Peaks.ArgMax(a, atTime - 10, atTime + 11);
            double next;
            if (a[at] <= 0 || Math.Abs(at - atTime) > 8)
            {
                miss++;
                next = atTime + fittedSlope;
            }
            else
            {
                double x = Peaks.Interpolate(a, at);
                sumKx += beat * x;
                sumKk += (double)beat * beat;
                sumXx += x * x;
                m++;
                fittedSlope = sumKx / sumKk;
                next = x + fittedSlope;
            }
            beat++;
            atTime = Peaks.Round(next);
        }
        if (m < 4) return new(-1, null);
        double bpm = sumKk * 60000.0 / sumKx;
        double sigma = Math.Sqrt(Math.Max(sumXx - sumKx * sumKx / sumKk, 0) / (m - 1));
        double rel = Math.Sqrt(Math.Max(sumXx * sumKk / (sumKx * sumKx) - 1, 0) / (m - 1));
        if (sigma > 2.4 || rel > 3e-5) return new(16, bpm);
        return new(miss < 1 && sigma <= 0.6 ? 0 : 1, bpm);
    }
}
