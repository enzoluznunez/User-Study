using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.GenAI.Types;

// The assistant's reads of the 13F holdings. Every one of them reads the whole
// dataset rather than whatever the room happens to show, so a question about a
// filer or a security can always be answered, hidden or not. None changes
// anything, so none touches the scene or the undo timeline, and since the data
// is read-only once loaded they run off the main thread.
public static class HoldingsLimits {
    public const int Default = 10;
    public const int Max = 200;
}

public abstract class HoldingsTool<TArgs> : Function where TArgs : class, new() {

    protected const int MaxLimit = HoldingsLimits.Max;

    protected abstract void Run(HoldingsData data, TArgs args, Dictionary<string, object> result);

    protected override Task<Dictionary<string, object>> Execute(Dictionary<string, object> args) {
        var result = new Dictionary<string, object>();
        HoldingsData data = HoldingsData.Current;

        if (data == null) {
            result["error"] = "The holdings data has not finished loading; try again in a moment.";
        }
        else {
            var bound = ToolArguments.Bind(typeof(TArgs), args ?? new Dictionary<string, object>(), out string error) as TArgs;
            if (bound == null) result["error"] = error;
            else {
                try { Run(data, bound, result); }
                catch (Exception e) {
                    result.Clear();
                    result["error"] = e.Message;
                }
            }
        }

        if (!result.ContainsKey("error") && !result.ContainsKey("needsChoice")) result["ok"] = true;
        UnityEngine.Debug.Log($"[Gemini][tool] {Name} -> {BriefJson(result, 300, out _)}");
        return Task.FromResult(result);
    }

    // ----- Finding a filer or a security by what the user said -----

    // A spoken name, a node id, a CIK or a CUSIP. One match answers; several ask
    // the user which, listing them; none says so and points at FindEntity.
    protected static bool TryResolveFiler(HoldingsData data, string token, Dictionary<string, object> result,
        out Filer filer) {
        filer = null;
        if (string.IsNullOrWhiteSpace(token)) { result["error"] = "Name the filer."; return false; }
        if (data.TryGetFiler(token, out filer)) return true;

        List<Filer> found = NameMatch.Best(data.Filers, token, f => f.Name);
        if (found.Count == 1) { filer = found[0]; return true; }
        if (found.Count == 0) {
            result["error"] = $"No filer matches '{token.Trim()}'. FindEntity searches by name, CIK or CUSIP.";
            return false;
        }
        Ambiguous(result, "filer", token, found.Select(f => $"{f.Name} (CIK {f.Cik})").ToList());
        return false;
    }

    protected static bool TryResolveSecurity(HoldingsData data, string token, Dictionary<string, object> result,
        out Security security) {
        security = null;
        if (string.IsNullOrWhiteSpace(token)) { result["error"] = "Name the security."; return false; }
        if (data.TryGetSecurity(token, out security)) return true;

        List<Security> found = NameMatch.Best(data.Securities, token, s => s.DisplayName);
        if (found.Count == 1) { security = found[0]; return true; }
        if (found.Count == 0) {
            result["error"] = $"No security matches '{token.Trim()}'. FindEntity searches by name, CIK or CUSIP.";
            return false;
        }
        Ambiguous(result, "security", token, found.Select(s => $"{s.DisplayName} (CUSIP {s.Cusip})").ToList());
        return false;
    }

    private static void Ambiguous(Dictionary<string, object> result, string what, string token, List<string> names) =>
        NeedChoice(result, what, names, $"More than one {what} matches '{token.Trim()}'. Ask the user which, " +
                                        "then call again with the CIK or CUSIP in brackets, which is unique.");

    // ----- How a filer, a security and a holding read back -----

    protected static Dictionary<string, object> Describe(Filer f) => new Dictionary<string, object> {
        { "id", f.Id },
        { "name", f.Name },
        { "cik", f.Cik },
        { "reportedPortfolioValueUsd", f.ReportedPortfolioValue },
        { "reportedPositions", f.ReportedEntries },
        { "holdingsInSample", f.Holdings.Count },
        { "valueInSampleUsd", f.SampleValue }
    };

    protected static Dictionary<string, object> Describe(Security s) {
        var d = new Dictionary<string, object> {
            { "id", s.Id },
            { "name", s.DisplayName },
            { "cusip", s.Cusip },
            { "holders", s.Holders.Count },
            { "valueInSampleUsd", s.SampleValue },
            { "sharesInSample", s.SampleShares }
        };
        if (!string.IsNullOrEmpty(s.ShareClass)) d["shareClass"] = s.ShareClass;
        return d;
    }

    protected static object Percent(long part, long whole) =>
        whole > 0 ? (object)Math.Round(100.0 * part / whole, 2) : null;

    protected static int Limit(int? limit) => Math.Max(1, Math.Min(limit ?? HoldingsLimits.Default, MaxLimit));

    protected static void NoteCut(Dictionary<string, object> result, int shown, int total, string noun) {
        result["total"] = total;
        if (shown < total) result["note"] = $"Showing {shown} of {total} {noun}; raise 'limit' for more.";
    }
}

public sealed class NoArgs { }

// How a spoken name finds a filer or security, shared by the data tools and
// the tools that act on the graph so a name means the same thing to both.
public static class NameMatch {

    // Best tier wins: an exact name, then a name that starts with what was said,
    // then one holding every word of it whole, then one with a word starting
    // each. So 'Alphabet Class A' finds one security, where a bare 'a' read as
    // a prefix would also match 'Alphabet' itself, and 'Alphabet' finds both
    // classes, which is the question to ask.
    public static List<T> Best<T>(IEnumerable<T> items, string token, Func<T, string> name) {
        string wanted = Normal(token);
        if (wanted.Length == 0) return new List<T>();
        string[] words = wanted.Split(' ');

        var tiers = new[] { new List<T>(), new List<T>(), new List<T>(), new List<T>() };
        foreach (T item in items) {
            string have = Normal(name(item));
            string padded = " " + have + " ";
            if (have == wanted) tiers[0].Add(item);
            else if (have.StartsWith(wanted, StringComparison.Ordinal)) tiers[1].Add(item);
            else if (words.All(w => padded.Contains(" " + w + " "))) tiers[2].Add(item);
            else if (words.All(w => padded.Contains(" " + w))) tiers[3].Add(item);
        }
        return tiers.FirstOrDefault(t => t.Count > 0) ?? tiers[0];
    }

    // Lowercase letters and digits, single-spaced: 'RB Capital Management, LLC'
    // and 'rb capital management llc' are the same name.
    public static string Normal(string text) {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char raw in text) {
            char c = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(c)) {
                if (space && sb.Length > 0) sb.Append(' ');
                sb.Append(c);
                space = false;
            }
            else if (c != '\'' && c != '’') space = true;
        }
        return sb.ToString();
    }
}

public sealed class GetHoldingsSummary : HoldingsTool<NoArgs> {

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "GetHoldingsSummary",
        Description = "What the 13F holdings data covers: the quarter, the filing dates, how many filers, securities " +
                      "and holdings it holds, their total value, the largest filer and the most widely held " +
                      "security. Call this when the user asks what the data is, or before answering anything " +
                      "about the dataset as a whole."
    };

    protected override void Run(HoldingsData data, NoArgs args, Dictionary<string, object> result) {
        Filer largest = data.Filers.OrderByDescending(f => f.ReportedPortfolioValue).ThenBy(f => f.Name).FirstOrDefault();
        Security widest = data.Securities.OrderByDescending(s => s.Holders.Count).ThenByDescending(s => s.SampleValue).FirstOrDefault();
        int reported = data.Filers.Sum(f => f.ReportedEntries);

        result["periodOfReport"] = data.PeriodOfReport;
        result["filedFrom"] = data.FirstFilingDate;
        result["filedTo"] = data.LastFilingDate;
        result["filers"] = data.Filers.Count;
        result["securities"] = data.Securities.Count;
        result["holdings"] = data.Holdings.Count;
        result["valueInSampleUsd"] = data.TotalValue;
        result["reportedPositionsAcrossFilers"] = reported;
        if (largest != null) result["largestFiler"] = Describe(largest);
        if (widest != null) result["mostWidelyHeld"] = Describe(widest);
        result["note"] = $"A sample: it holds {data.Holdings.Count} of the {reported} positions these filers reported. " +
                         "Reported portfolio values cover each filer's whole filing; sample values cover only " +
                         "the positions here.";
    }
}

public sealed class FindEntity : HoldingsTool<FindEntity.Args> {

    public class Args {
        [Doc("A name, or part of one, a filer's CIK, a security's CUSIP, or a node id such as F03 or S12.")]
        public string query;

        [Optional, Values("any", "filer", "security"), DefaultsTo("any")]
        [Doc("Search only filers or only securities.")]
        public string type;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "FindEntity",
        Description = "Look up filers and securities by name, CIK or CUSIP. Use this when a name the user said " +
                      "matches nothing, or to tell similar names apart: several securities share a name and " +
                      "differ by share class or issue, which their names here state.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        string type = args.type ?? "any";
        var found = new List<object>();

        if (type != "security") {
            var filers = data.TryGetFiler(args.query, out Filer f) ? new List<Filer> { f } : NameMatch.Best(data.Filers, args.query, x => x.Name);
            foreach (Filer x in filers.Take(MaxLimit)) {
                var d = Describe(x);
                d["type"] = "filer";
                found.Add(d);
            }
        }
        if (type != "filer") {
            var securities = data.TryGetSecurity(args.query, out Security s) ? new List<Security> { s } : NameMatch.Best(data.Securities, args.query, x => x.DisplayName);
            foreach (Security x in securities.Take(MaxLimit)) {
                var d = Describe(x);
                d["type"] = "security";
                found.Add(d);
            }
        }

        result["matches"] = found;
        if (found.Count == 0) result["note"] = $"Nothing matches '{args.query.Trim()}'. Try fewer words, or ListFilers and ListSecurities.";
    }
}

public sealed class ListFilers : HoldingsTool<ListFilers.Args> {

    public class Args {
        [Optional, Values("portfolioValue", "sampleValue", "holdings", "name"), DefaultsTo("portfolioValue")]
        [Doc("'portfolioValue' is each filer's whole reported portfolio, 'sampleValue' only the positions in this " +
             "data, 'holdings' how many of those positions there are.")]
        public string sortBy;

        [Optional, Limits(1, HoldingsLimits.Max), DefaultsTo(HoldingsLimits.Default)]
        [Doc("How many to list, from the top of the order.")]
        public int? limit;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "ListFilers",
        Description = "List the filers, the investment managers that filed a 13F, largest first unless sorted " +
                      "otherwise, with their reported portfolio value and what of it is in this data.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        IEnumerable<Filer> order;
        switch (args.sortBy ?? "portfolioValue") {
            case "sampleValue": order = data.Filers.OrderByDescending(f => f.SampleValue); break;
            case "holdings": order = data.Filers.OrderByDescending(f => f.Holdings.Count).ThenByDescending(f => f.SampleValue); break;
            case "name": order = data.Filers.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase); break;
            default: order = data.Filers.OrderByDescending(f => f.ReportedPortfolioValue); break;
        }

        int limit = Limit(args.limit);
        result["filers"] = order.Take(limit).Select(x => (object)Describe(x)).ToList();
        NoteCut(result, Math.Min(limit, data.Filers.Count), data.Filers.Count, "filers");
    }
}

public sealed class ListSecurities : HoldingsTool<ListSecurities.Args> {

    public class Args {
        [Optional, Values("holders", "value", "name"), DefaultsTo("holders")]
        [Doc("'holders' is how many filers hold it, 'value' the combined value of their positions.")]
        public string sortBy;

        [Optional, Limits(1, HoldingsLimits.Max), DefaultsTo(HoldingsLimits.Default)]
        [Doc("How many to list, from the top of the order.")]
        public int? limit;

        [Optional, Limits(1, 10000)]
        [Doc("Only securities at least this many filers hold.")]
        public int? minHolders;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "ListSecurities",
        Description = "List the securities the filers hold, the most widely held first unless sorted otherwise, " +
                      "with how many filers hold each and the combined value and shares of their positions.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        IEnumerable<Security> pool = data.Securities;
        if (args.minHolders.HasValue) pool = pool.Where(s => s.Holders.Count >= args.minHolders.Value);

        switch (args.sortBy ?? "holders") {
            case "value": pool = pool.OrderByDescending(s => s.SampleValue); break;
            case "name": pool = pool.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase); break;
            default: pool = pool.OrderByDescending(s => s.Holders.Count).ThenByDescending(s => s.SampleValue); break;
        }

        List<Security> all = pool.ToList();
        int limit = Limit(args.limit);
        result["securities"] = all.Take(limit).Select(x => (object)Describe(x)).ToList();
        NoteCut(result, Math.Min(limit, all.Count), all.Count, "securities");
    }
}

public sealed class GetHoldings : HoldingsTool<GetHoldings.Args> {

    public class Args {
        [Doc("The filer, by name, CIK or id.")]
        public string filer;

        [Optional, Values("value", "shares", "name"), DefaultsTo("value")]
        [Doc("How to order the positions.")]
        public string sortBy;

        [Optional, Limits(1, HoldingsLimits.Max), DefaultsTo(HoldingsLimits.Default)]
        [Doc("How many positions to list, from the top of the order.")]
        public int? limit;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "GetHoldings",
        Description = "One filer's positions in this data: each security it holds, the value and shares, and what " +
                      "share each is of the filer's sample value and of its whole reported portfolio.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        if (!TryResolveFiler(data, args.filer, result, out Filer filer)) return;

        IEnumerable<Holding> order;
        switch (args.sortBy ?? "value") {
            case "shares": order = filer.Holdings.OrderByDescending(h => h.Shares); break;
            case "name": order = filer.Holdings.OrderBy(h => h.Security.DisplayName, StringComparer.OrdinalIgnoreCase); break;
            default: order = filer.Holdings.OrderByDescending(h => h.Value); break;
        }

        long sample = filer.SampleValue;
        int limit = Limit(args.limit);
        result["filer"] = Describe(filer);
        result["holdings"] = order.Take(limit).Select(h => (object)new Dictionary<string, object> {
            { "security", h.Security.DisplayName },
            { "cusip", h.Security.Cusip },
            { "valueUsd", h.Value },
            { "shares", h.Shares },
            { "percentOfSample", Percent(h.Value, sample) },
            { "percentOfReportedPortfolio", Percent(h.Value, filer.ReportedPortfolioValue) },
            { "filed", h.FilingDate }
        }).ToList();
        NoteCut(result, Math.Min(limit, filer.Holdings.Count), filer.Holdings.Count, "positions");
    }
}

public sealed class GetHolders : HoldingsTool<GetHolders.Args> {

    public class Args {
        [Doc("The security, by name, CUSIP or id. Name the share class when there are several.")]
        public string security;

        [Optional, Values("value", "shares", "name"), DefaultsTo("value")]
        [Doc("How to order the holders.")]
        public string sortBy;

        [Optional, Limits(1, HoldingsLimits.Max), DefaultsTo(HoldingsLimits.Default)]
        [Doc("How many holders to list, from the top of the order.")]
        public int? limit;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "GetHolders",
        Description = "Who holds one security: each filer, the value and shares of its position, and what share " +
                      "each is of everything held in that security here.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        if (!TryResolveSecurity(data, args.security, result, out Security security)) return;

        IEnumerable<Holding> order;
        switch (args.sortBy ?? "value") {
            case "shares": order = security.Holders.OrderByDescending(h => h.Shares); break;
            case "name": order = security.Holders.OrderBy(h => h.Filer.Name, StringComparer.OrdinalIgnoreCase); break;
            default: order = security.Holders.OrderByDescending(h => h.Value); break;
        }

        long total = security.SampleValue;
        int limit = Limit(args.limit);
        result["security"] = Describe(security);
        result["holders"] = order.Take(limit).Select(h => (object)new Dictionary<string, object> {
            { "filer", h.Filer.Name },
            { "valueUsd", h.Value },
            { "shares", h.Shares },
            { "percentOfSecurityValue", Percent(h.Value, total) },
            { "percentOfFilerPortfolio", Percent(h.Value, h.Filer.ReportedPortfolioValue) }
        }).ToList();
        NoteCut(result, Math.Min(limit, security.Holders.Count), security.Holders.Count, "holders");
    }
}

public sealed class CompareFilers : HoldingsTool<CompareFilers.Args> {

    public class Args {
        [Doc("Two or more filers, by name, CIK or id.")]
        public string[] filers;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "CompareFilers",
        Description = "What two or more filers have in common: the securities every one of them holds, each one's " +
                      "position in them, and how much of each filer's holdings here are shared or its own.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        if (args.filers == null || args.filers.Length < 2) { result["error"] = "Name at least two filers."; return; }

        var filers = new List<Filer>();
        foreach (string token in args.filers) {
            if (!TryResolveFiler(data, token, result, out Filer f)) return;
            if (filers.Contains(f)) { result["error"] = $"{f.Name} is named twice."; return; }
            filers.Add(f);
        }

        var shared = new HashSet<Security>(filers[0].Holdings.Select(h => h.Security));
        var union = new HashSet<Security>(shared);
        for (int i = 1; i < filers.Count; i++) {
            var held = filers[i].Holdings.Select(h => h.Security).ToList();
            shared.IntersectWith(held);
            union.UnionWith(held);
        }

        var positionsOf = filers.ToDictionary(f => f, f => f.Holdings.ToDictionary(h => h.Security));

        // How many of the filers named hold each security, to tell which are one filer's alone.
        var holders = new Dictionary<Security, int>();
        foreach (Filer f in filers)
            foreach (Holding h in f.Holdings)
                holders[h.Security] = holders.TryGetValue(h.Security, out int n) ? n + 1 : 1;

        result["shared"] = shared
            .OrderByDescending(s => filers.Sum(f => positionsOf[f][s].Value))
            .Select(s => (object)new Dictionary<string, object> {
                { "security", s.DisplayName },
                { "cusip", s.Cusip },
                { "positions", filers.Select(f => {
                    Holding h = positionsOf[f][s];
                    return (object)new Dictionary<string, object> {
                        { "filer", f.Name }, { "valueUsd", h.Value }, { "shares", h.Shares }
                    };
                }).ToList() }
            }).ToList();

        result["filers"] = filers.Select(f => {
            long inShared = f.Holdings.Where(h => shared.Contains(h.Security)).Sum(h => h.Value);
            return (object)new Dictionary<string, object> {
                { "filer", f.Name },
                { "holdingsInSample", f.Holdings.Count },
                { "onlyThisFiler", f.Holdings.Count(h => holders[h.Security] == 1) },
                { "valueInSharedUsd", inShared },
                { "percentOfSampleShared", Percent(inShared, f.SampleValue) }
            };
        }).ToList();

        // Jaccard: the shared securities over every security any of them holds.
        result["overlap"] = union.Count > 0 ? Math.Round((double)shared.Count / union.Count, 3) : 0d;
        result["note"] = "'overlap' is the shared securities as a fraction of all the securities any of them holds.";
    }
}

public sealed class TopPositions : HoldingsTool<TopPositions.Args> {

    public class Args {
        [Optional, Values("value", "shares"), DefaultsTo("value")]
        [Doc("Rank by the position's dollar value or by its share count.")]
        public string sortBy;

        [Optional, Limits(1, HoldingsLimits.Max), DefaultsTo(HoldingsLimits.Default)]
        [Doc("How many positions to list.")]
        public int? limit;
    }

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "TopPositions",
        Description = "The largest single positions in the whole data, across every filer: who holds what, " +
                      "and how much.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(HoldingsData data, Args args, Dictionary<string, object> result) {
        IEnumerable<Holding> order = (args.sortBy ?? "value") == "shares"
            ? data.Holdings.OrderByDescending(h => h.Shares)
            : data.Holdings.OrderByDescending(h => h.Value);

        int limit = Limit(args.limit);
        result["positions"] = order.Take(limit).Select(h => (object)new Dictionary<string, object> {
            { "filer", h.Filer.Name },
            { "security", h.Security.DisplayName },
            { "cusip", h.Security.Cusip },
            { "valueUsd", h.Value },
            { "shares", h.Shares },
            { "percentOfFilerPortfolio", Percent(h.Value, h.Filer.ReportedPortfolioValue) }
        }).ToList();
        NoteCut(result, Math.Min(limit, data.Holdings.Count), data.Holdings.Count, "positions");
    }
}
