// Which views of the holdings are in the room: the network graph, the bar
// sheet, or both, as HoldingsLoader's Inspector switches say. HoldingsLoader
// runs before every other script, so they are set before anyone reads them.
//
// With one view showing, every tool acts on it and nobody has to say which.
// With both, a hand tool acts on whichever view the hand touched, and the
// assistant passes 'view' to say which it means.
public enum ViewKind { Sheet, Graph }

public static class Views
{
    public static bool Graph { get; private set; } = true;
    public static bool Sheet { get; private set; }
    public static bool Both => Graph && Sheet;

    public static void Configure(bool graph, bool sheet)
    {
        // An app showing nothing is not a configuration anyone means; the graph
        // stays on rather than leaving the room empty.
        Graph = graph || !sheet;
        Sheet = sheet;
    }

    // The one view showing, when there is only one.
    public static bool TrySingle(out ViewKind view)
    {
        view = Graph ? ViewKind.Graph : ViewKind.Sheet;
        return !Both;
    }

    public static string Name(ViewKind view) => view == ViewKind.Graph ? "graph" : "sheet";

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Configure(true, false);
}
