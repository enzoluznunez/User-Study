using System.Collections.Generic;

// Count, range, mean and total of a set of values: what a stats card reports,
// what the assistant ranks lines by, and what GetStatistics reads out. One
// accumulator for all three, so they cannot disagree on what a minimum is.
public static class ValueStats
{
    public struct Summary
    {
        public bool valid;
        public int count;
        public double min;
        public double max;
        public double mean;
        public double sum;
    }

    public struct Accumulator
    {
        public int Count { get; private set; }
        public double Sum { get; private set; }
        public double Min { get; private set; }
        public double Max { get; private set; }

        // Adds a value, and says whether it is the new lowest or highest, for a
        // caller that also wants to know where the extremes are.
        public void Add(double value, out bool lowest, out bool highest)
        {
            lowest = Count == 0 || value < Min;
            highest = Count == 0 || value > Max;
            if (lowest) Min = value;
            if (highest) Max = value;
            Sum += value;
            Count++;
        }

        public void Add(double value) => Add(value, out _, out _);

        public Summary Summary => Count == 0
            ? default
            : new Summary { valid = true, count = Count, min = Min, max = Max, mean = Sum / Count, sum = Sum };

        // One number by name: sum, average, max or min.
        public bool TryMeasure(string measure, out double score)
        {
            score = 0d;
            if (Count == 0) return false;
            switch (measure)
            {
                case "sum": score = Sum; return true;
                case "average": score = Sum / Count; return true;
                case "max": score = Max; return true;
                case "min": score = Min; return true;
                default: return false;
            }
        }
    }

    public static Summary Of(IEnumerable<double> values)
    {
        var acc = new Accumulator();
        if (values != null)
            foreach (double v in values)
                if (!double.IsNaN(v)) acc.Add(v);
        return acc.Summary;
    }
}
