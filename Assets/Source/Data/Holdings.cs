using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// The 13F holdings: who reported holding what at the end of one quarter. A
// filer is an investment manager, a security is what it holds, and a holding is
// one filer's position in one security. Both ends are nodes of a network and a
// holding is the edge between them.
//
// Read from the two CSVs in StreamingAssets, which are the single source of
// truth: CUSIPs are already uppercased there (the spellings that arrived are in
// 'cusip_raw'), and each filer and security appears once, which the reader
// checks. Two lines for one filer and security are read as one position.
public sealed class Filer
{
    public string Id;
    public string Name;
    public string Cik;
    public long ReportedPortfolioValue;
    public int ReportedEntries;
    public Vector3 Layout;
    public readonly List<Holding> Holdings = new List<Holding>();

    // The positions in this data, totalled once as they are read.
    public long SampleValue { get; internal set; }
}

public sealed class Security
{
    public string Id;
    public string Name;
    // The name a person can tell apart from every other: it carries the share
    // class, and the issue or the CUSIP's last four where names still collide.
    public string DisplayName;
    public string ShareClass;
    public string Cusip;
    public Vector3 Layout;
    public readonly List<Holding> Holders = new List<Holding>();

    // What its holders hold of it here, totalled once as the edges are read.
    public long SampleValue { get; internal set; }
    public long SampleShares { get; internal set; }
}

public sealed class Holding
{
    public Filer Filer;
    public Security Security;
    public long Value;
    public long Shares;
    public string FilingDate;
    public string PeriodOfReport;
}

public sealed class HoldingsData
{
    public const string NodesFile = "13f_sample_nodes.csv";
    public const string EdgesFile = "13f_sample_edges.csv";

    // The data the app has loaded, or null until it has. Read-only once set, so
    // the assistant's tools may read it from any thread.
    public static HoldingsData Current { get; private set; }

    public static event Action OnLoaded;

    public static void Publish(HoldingsData data)
    {
        Current = data;
        OnLoaded?.Invoke();
    }

    public readonly List<Filer> Filers = new List<Filer>();
    public readonly List<Security> Securities = new List<Security>();
    public readonly List<Holding> Holdings = new List<Holding>();

    public string PeriodOfReport { get; private set; } = "";
    public string FirstFilingDate { get; private set; } = "";
    public string LastFilingDate { get; private set; } = "";

    private readonly Dictionary<string, Filer> _filersById = new Dictionary<string, Filer>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Filer> _filersByCik = new Dictionary<string, Filer>();
    private readonly Dictionary<string, Security> _securitiesById = new Dictionary<string, Security>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Security> _securitiesByCusip = new Dictionary<string, Security>(StringComparer.OrdinalIgnoreCase);

    public long TotalValue
    {
        get
        {
            long sum = 0;
            for (int i = 0; i < Holdings.Count; i++) sum += Holdings[i].Value;
            return sum;
        }
    }

    public static HoldingsData Parse(string nodesCsv, string edgesCsv)
    {
        var data = new HoldingsData();
        data.ReadNodes(Csv.Read(nodesCsv));
        data.ReadEdges(Csv.Read(edgesCsv));
        return data;
    }

    private void ReadNodes(Csv.Table table)
    {
        for (int r = 0; r < table.Rows.Count; r++)
        {
            string type = table.Get(r, "node_type");
            string id = table.Get(r, "node_id");
            if (string.IsNullOrEmpty(id)) throw new FormatException($"{NodesFile} row {r + 2} has no node_id.");

            var layout = new Vector3(table.Float(r, "x"), table.Float(r, "y"), table.Float(r, "z"));

            if (type == "filer")
            {
                var f = new Filer
                {
                    Id = id,
                    Name = table.Get(r, "name"),
                    Cik = NormalCik(table.Get(r, "filer_cik")),
                    ReportedPortfolioValue = table.Long(r, "reported_portfolio_value_usd"),
                    ReportedEntries = (int)table.Long(r, "reported_entries"),
                    Layout = layout
                };
                if (f.Cik.Length == 0)
                    throw new FormatException($"{NodesFile} row {r + 2} is a filer with no filer_cik.");
                if (_filersByCik.ContainsKey(f.Cik))
                    throw new FormatException($"{NodesFile} lists filer CIK {f.Cik} twice; each filer must appear once.");
                Filers.Add(f);
                _filersById[id] = f;
                _filersByCik[f.Cik] = f;
            }
            else if (type == "security")
            {
                string name = table.Get(r, "name");
                string display = table.Get(r, "display_name");
                var s = new Security
                {
                    Id = id,
                    Name = name,
                    DisplayName = string.IsNullOrEmpty(display) ? name : display,
                    ShareClass = table.Get(r, "share_class"),
                    Cusip = table.Get(r, "cusip").ToUpperInvariant(),
                    Layout = layout
                };
                if (s.Cusip.Length == 0)
                    throw new FormatException($"{NodesFile} row {r + 2} is a security with no cusip.");
                if (_securitiesByCusip.ContainsKey(s.Cusip))
                    throw new FormatException($"{NodesFile} lists CUSIP {s.Cusip} twice; each security must appear once.");
                Securities.Add(s);
                _securitiesById[id] = s;
                _securitiesByCusip[s.Cusip] = s;
            }
            else
            {
                throw new FormatException($"{NodesFile} row {r + 2} has node_type '{type}', not 'filer' or 'security'.");
            }
        }
    }

    private void ReadEdges(Csv.Table table)
    {
        var dates = new SortedSet<string>(StringComparer.Ordinal);
        var periods = new Dictionary<string, int>();
        var positions = new Dictionary<(Filer, Security), Holding>();

        for (int r = 0; r < table.Rows.Count; r++)
        {
            string cik = NormalCik(table.Get(r, "filer_cik"));
            string cusip = table.Get(r, "cusip").ToUpperInvariant();

            if (!_filersByCik.TryGetValue(cik, out Filer filer))
                throw new FormatException($"{EdgesFile} row {r + 2} names filer CIK {cik}, which {NodesFile} does not list.");
            if (!_securitiesByCusip.TryGetValue(cusip, out Security security))
                throw new FormatException($"{EdgesFile} row {r + 2} names CUSIP {cusip}, which {NodesFile} does not list.");

            long value = table.Long(r, "position_value_usd");
            long shares = table.Long(r, "shares");

            if (!positions.TryGetValue((filer, security), out Holding h))
            {
                h = new Holding
                {
                    Filer = filer,
                    Security = security,
                    FilingDate = IsoDate(table.Get(r, "filing_date")),
                    PeriodOfReport = IsoDate(table.Get(r, "period_of_report"))
                };
                positions[(filer, security)] = h;
                Holdings.Add(h);
                filer.Holdings.Add(h);
                security.Holders.Add(h);
            }

            // A filer reporting one security on two lines holds it once: the
            // lines are one position, summed.
            h.Value += value;
            h.Shares += shares;
            filer.SampleValue += value;
            security.SampleValue += value;
            security.SampleShares += shares;

            if (!string.IsNullOrEmpty(h.FilingDate)) dates.Add(h.FilingDate);
            if (!string.IsNullOrEmpty(h.PeriodOfReport))
                periods[h.PeriodOfReport] = periods.TryGetValue(h.PeriodOfReport, out int n) ? n + 1 : 1;
        }

        if (dates.Count > 0) { FirstFilingDate = dates.Min; LastFilingDate = dates.Max; }
        int most = 0;
        foreach (var kv in periods)
            if (kv.Value > most) { most = kv.Value; PeriodOfReport = kv.Key; }
    }

    public bool TryGetFiler(string idOrCik, out Filer filer)
    {
        filer = null;
        if (string.IsNullOrWhiteSpace(idOrCik)) return false;
        string token = idOrCik.Trim();
        return _filersById.TryGetValue(token, out filer) || _filersByCik.TryGetValue(NormalCik(token), out filer);
    }

    public bool TryGetSecurity(string idOrCusip, out Security security)
    {
        security = null;
        if (string.IsNullOrWhiteSpace(idOrCusip)) return false;
        string token = idOrCusip.Trim();
        return _securitiesById.TryGetValue(token, out security) || _securitiesByCusip.TryGetValue(token, out security);
    }

    // A CIK is a number: '0001369702' and '1369702' are the same filer.
    public static string NormalCik(string cik)
    {
        if (string.IsNullOrWhiteSpace(cik)) return "";
        string trimmed = cik.Trim().TrimStart('0');
        return trimmed.Length > 0 ? trimmed : "0";
    }

    // '21-JUL-2026' as the filings write it, '2026-07-21' as everything else
    // compares it.
    private static readonly string[] DateFormats = { "dd-MMM-yyyy", "yyyy-MM-dd" };

    // A quarter's filings share a handful of dates, so each is parsed once.
    // Locked because the data may be read off the main thread.
    private static readonly Dictionary<string, string> IsoDates = new Dictionary<string, string>();

    public static string IsoDate(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string trimmed = text.Trim();
        lock (IsoDates)
        {
            if (IsoDates.TryGetValue(trimmed, out string known)) return known;
            string iso = DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime date)
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : trimmed;
            IsoDates[trimmed] = iso;
            return iso;
        }
    }
}

// RFC 4180 CSV: quoted fields may hold commas, quotes and line breaks. Small
// enough to keep beside its one reader.
public static class Csv
{
    public sealed class Table
    {
        public readonly Dictionary<string, int> Columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly List<List<string>> Rows = new List<List<string>>();

        public string Get(int row, string column)
        {
            if (!Columns.TryGetValue(column, out int c)) return "";
            List<string> cells = Rows[row];
            return c < cells.Count ? cells[c].Trim() : "";
        }

        public long Long(int row, string column)
        {
            string text = Get(row, column);
            if (text.Length == 0) return 0;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole)) return whole;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double real)) return (long)Math.Round(real);
            throw new FormatException($"'{text}' in column {column} is not a number.");
        }

        public float Float(int row, string column)
        {
            string text = Get(row, column);
            return text.Length > 0 && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }
    }

    public static Table Read(string text)
    {
        var table = new Table();
        if (string.IsNullOrEmpty(text)) return table;

        List<List<string>> records = Records(text);
        if (records.Count == 0) return table;

        List<string> header = records[0];
        for (int c = 0; c < header.Count; c++)
        {
            string name = header[c].Trim().TrimStart('﻿');
            if (name.Length > 0 && !table.Columns.ContainsKey(name)) table.Columns[name] = c;
        }

        for (int r = 1; r < records.Count; r++)
        {
            List<string> row = records[r];
            if (row.Count == 1 && row[0].Trim().Length == 0) continue;
            table.Rows.Add(row);
        }
        return table;
    }

    // One line's fields, by the same rules a whole file is read with.
    public static List<string> Fields(string line)
    {
        List<List<string>> records = Records(line);
        return records.Count > 0 ? records[0] : new List<string> { "" };
    }

    private static List<List<string>> Records(string text)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch != '"') { cell.Append(ch); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; continue; }
                quoted = false;
                continue;
            }

            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(cell.ToString()); cell.Clear();
                    records.Add(row); row = new List<string>();
                    break;
                default: cell.Append(ch); break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            records.Add(row);
        }
        return records;
    }
}
