using System.Collections.Generic;

// Breadth-first search over holdings, kept apart from the scene so it can be
// tested on its own.
//
// The network is bipartite, so hops alternate kinds: from a filer, odd hops are
// securities and even hops are filers. One hop is what a filer holds, two is who
// else holds those, three is what they hold besides; from a security the same
// runs the other way round. Depth-first search is not offered: its depth is
// the order it happened to walk the edges in, not a distance, so the same data
// could profile differently from one run to the next.
public static class GraphSearch
{
    // Every node within 'hops' of the root, with how many hops out it is. The
    // root is at 0, and is all there is when nothing among those given joins it.
    public static Dictionary<string, int> Hops(IEnumerable<Holding> holdings, string root, int hops)
    {
        var next = new Dictionary<string, List<string>>();
        foreach (Holding h in holdings)
        {
            Link(next, h.Filer.Id, h.Security.Id);
            Link(next, h.Security.Id, h.Filer.Id);
        }

        var found = new Dictionary<string, int>();
        if (root == null) return found;
        found[root] = 0;
        if (!next.ContainsKey(root)) return found;

        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            string at = queue.Dequeue();
            int hop = found[at];
            if (hop >= hops) continue;
            foreach (string n in next[at])
            {
                if (found.ContainsKey(n)) continue;
                found[n] = hop + 1;
                queue.Enqueue(n);
            }
        }
        return found;
    }

    private static void Link(Dictionary<string, List<string>> next, string from, string to)
    {
        if (!next.TryGetValue(from, out List<string> list)) next[from] = list = new List<string>();
        list.Add(to);
    }
}
