using System.Collections.Generic;

public static class SheetStats
{
    public struct Summary
    {
        public bool valid;
        public int count;
        public double min;
        public double max;
        public int minVisRow;
        public int minVisCol;
        public int maxVisRow;
        public int maxVisCol;
        public double mean;
        public double sum;

        // The part the stats card shows, in the shape the graph's cards use too.
        public ValueStats.Summary AsValues() => new ValueStats.Summary
        {
            valid = valid, count = count, min = min, max = max, mean = mean, sum = sum
        };
    }

    public static Summary Over(DataSource data, int rowMin, int rowMax, int colMin, int colMax)
    {
        Summary summary = new Summary();
        if (data == null) return summary;

        IReadOnlyList<int> rowOrder = data.RowOrder;
        IReadOnlyList<int> colOrder = data.ColumnOrder;
        if (rowOrder == null || colOrder == null) return summary;

        var acc = new ValueStats.Accumulator();
        for (int visRow = rowMin; visRow <= rowMax; visRow++)
        {
            if (visRow < 0 || visRow >= rowOrder.Count) continue;
            int dataRow = rowOrder[visRow];

            for (int visCol = colMin; visCol <= colMax; visCol++)
            {
                if (visCol < 0 || visCol >= colOrder.Count) continue;
                int dataCol = colOrder[visCol];
                if (!data.HasValue(dataRow, dataCol)) continue;

                acc.Add(data.GetValue(dataRow, dataCol), out bool lowest, out bool highest);
                if (lowest) { summary.minVisRow = visRow; summary.minVisCol = visCol; }
                if (highest) { summary.maxVisRow = visRow; summary.maxVisCol = visCol; }
            }
        }

        ValueStats.Summary values = acc.Summary;
        summary.valid = values.valid;
        summary.count = values.count;
        summary.min = values.min;
        summary.max = values.max;
        summary.mean = values.mean;
        summary.sum = values.sum;
        return summary;
    }
}
