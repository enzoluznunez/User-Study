using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Reads the 13F holdings from StreamingAssets once, at startup, and publishes
// them as HoldingsData.Current. The app ships its data: nothing is fetched from
// a server, so the assistant can answer from it the moment it has loaded.
//
// It also decides which views of the holdings stand in the room. The network
// graph draws them directly; the bar sheet draws them as a matrix, a row per
// security and a column per filer, each bar the value of one position.
// Runs first, so the view switches are set before any tool or panel asks.
[DefaultExecutionOrder(-1000)]
public class HoldingsLoader : MonoBehaviour
{
    [Tooltip("The nodes file inside StreamingAssets: one row per filer and per security.")]
    public string nodesFile = HoldingsData.NodesFile;

    [Tooltip("The edges file inside StreamingAssets: one row per filer and security held.")]
    public string edgesFile = HoldingsData.EdgesFile;

    [Tooltip("Show the holdings as a 3D network: filers and securities joined by their holdings.")]
    public bool showGraph = true;

    [Tooltip("Show the holdings as a bar sheet: a row per security, a column per filer.")]
    public bool showSheet;

    [Tooltip("Most securities on the bar sheet, most widely held first.")]
    public int sheetRows = 40;

    [Tooltip("Most filers on the bar sheet, largest portfolio first.")]
    public int sheetColumns = 40;

    public const string SheetLabel = "Holdings";

    // Before any Start, so every tool and the assistant see the same views.
    private void Awake() => Views.Configure(showGraph, showSheet);

    private void Start() => StartCoroutine(Load());

    private IEnumerator Load()
    {
        string nodes = null, edges = null, failure = null;
        yield return ReadText(nodesFile, text => nodes = text, error => failure = error);
        if (failure == null) yield return ReadText(edgesFile, text => edges = text, error => failure = error);

        if (failure != null)
        {
            Fail(failure);
            yield break;
        }

        // Parsed on a worker thread: the model is plain C#, and a full export is
        // large enough that reading it here would freeze the headset.
        var parse = System.Threading.Tasks.Task.Run(() => HoldingsData.Parse(nodes, edges));
        while (!parse.IsCompleted) yield return null;

        if (parse.IsFaulted)
        {
            Exception e = parse.Exception?.GetBaseException();
            Fail($"The holdings files could not be read: {e?.Message}");
            yield break;
        }
        HoldingsData data = parse.Result;

        HoldingsData.Publish(data);
        Debug.Log($"[HoldingsLoader] {data.Filers.Count} filers, {data.Securities.Count} securities, " +
                  $"{data.Holdings.Count} holdings for the quarter ending {data.PeriodOfReport}.");

        if (Views.Sheet) OpenSheet(data);
    }

    private void OpenSheet(HoldingsData data)
    {
        ManageDatasets datasets = Scene.Datasets;
        if (datasets == null)
        {
            Debug.LogError("[HoldingsLoader] The bar sheet is switched on but the scene has no ManageDatasets.");
            return;
        }
        datasets.AddDataset(SheetCsv(data, sheetRows, sheetColumns), SheetLabel);
    }

    // The holdings as the CSV the sheet reads: the first column names the
    // security, every other column is a filer, and a cell is that filer's
    // position in that security in dollars, or empty where it holds none.
    public static string SheetCsv(HoldingsData data, int maxRows, int maxColumns)
    {
        var filers = data.Filers.OrderByDescending(f => f.ReportedPortfolioValue)
            .Take(Math.Max(1, maxColumns)).ToList();
        var securities = data.Securities
            .OrderByDescending(s => s.Holders.Count).ThenByDescending(s => s.SampleValue)
            .Take(Math.Max(1, maxRows)).ToList();

        List<string> columnTitles = UniqueTitles(filers.Select(f => (f.Name, $"CIK {f.Cik}")).ToList());
        List<string> rowTitles = UniqueTitles(securities.Select(s => (s.DisplayName, $"CUSIP {s.Cusip}")).ToList());

        var csv = new StringBuilder();
        // The corner names both axes, rows first: the sheet reports them as what
        // its rows and columns hold.
        csv.Append("Security \\ Filer");
        foreach (string title in columnTitles) csv.Append(',').Append(Quote(title));
        csv.Append('\n');

        for (int r = 0; r < securities.Count; r++)
        {
            Security s = securities[r];
            csv.Append(Quote(rowTitles[r]));
            var held = new Dictionary<Filer, long>(s.Holders.Count);
            foreach (Holding h in s.Holders) held[h.Filer] = h.Value;
            foreach (Filer f in filers)
            {
                csv.Append(',');
                if (held.TryGetValue(f, out long value))
                    csv.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            csv.Append('\n');
        }
        return csv.ToString();
    }

    // Titles the sheet and its tools can tell apart by name: shortened to fit
    // beside a bar, unless shortening makes two the same, then whole, and if
    // two names are the same whole, with the key that separates them.
    private static List<string> UniqueTitles(List<(string name, string key)> items)
    {
        var titles = items.Select(i => GraphLabels.Shorten(i.name)).ToList();
        Disambiguate(titles, i => items[i].name);
        Disambiguate(titles, i => $"{items[i].name} ({items[i].key})");
        return titles;
    }

    private static void Disambiguate(List<string> titles, Func<int, string> longer)
    {
        var counts = titles.GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < titles.Count; i++)
            if (counts[titles[i]] > 1) titles[i] = longer(i);
    }

    private static string Quote(string text) =>
        text.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? text : "\"" + text.Replace("\"", "\"\"") + "\"";

    private static IEnumerator ReadText(string fileName, Action<string> done, Action<string> failed)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(StreamingAssets.Url(fileName)))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                failed($"'{fileName}' is missing from StreamingAssets ({www.error}).");
                yield break;
            }
            done(www.downloadHandler.text);
        }
    }

    private void Fail(string message)
    {
        Debug.LogError($"[HoldingsLoader] {message}");
        Notices.Show(this, "No Data", message);
    }
}
