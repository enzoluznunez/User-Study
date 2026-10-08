using System;
using System.Collections.Generic;
using System.Linq;
using Google.GenAI.Types;
using UnityEngine;

public sealed class DescribeGraph : AgenticTool {

    // Offered only while the network graph is in the room.
    public override bool IsAvailable() => Views.Graph;

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "DescribeGraph",
        Description = "Read what the graph in the room shows right now: how many filers, securities and holdings " +
                      "are drawn and how many are switched on, what the Filter tool has switched off and the smallest " +
                      "holding still drawn, how the Sort tool has arranged it, which node the Profile tool has searched out " +
                      "from and how far, and where the graph stands. This describes the " +
                      "picture, not the data; the data tools read the holdings themselves. Call it when the user " +
                      "asks what is on view, or before changing the filter so you know what is already off."
    };

    protected override void Run(Dictionary<string, object> args, Dictionary<string, object> result) {
        if (!TryResolveGraph(result, "describe", out ManageGraph graph, out GraphView view)) return;

        int filers = graph.VisibleFilerCount;
        int securities = graph.VisibleSecurityCount;

        result["filers"] = new Dictionary<string, object> {
            { "showing", filers }, { "drawn", graph.DrawnFilers.Count }, { "inData", graph.Data.Filers.Count }
        };
        result["securities"] = new Dictionary<string, object> {
            { "showing", securities }, { "drawn", graph.DrawnSecurities.Count }, { "inData", graph.Data.Securities.Count }
        };
        result["edgesShowing"] = graph.VisibleHoldings.Count;
        result["switchedOff"] = graph.Hidden.Select(graph.NameOf).Where(n => n != null).Cast<object>().ToList();
        result["minValueUsd"] = graph.MinValue;

        result["arrangement"] = ManageGraph.OrderName(graph.Order);
        if (!graph.Profile.IsNone) {
            result["profiled"] = graph.NameOf(graph.Profile.root);
            result["hops"] = graph.Profile.hops;
            if (!graph.IsVisible(graph.Profile.root))
                result["profileNote"] = "The profiled node is filtered off, so nothing is lit until it is back.";
            else
                result["reached"] = graph.ProfileHops.Count - 1;
        }

        if (graph.DrawnFilers.Count < graph.Data.Filers.Count || graph.DrawnSecurities.Count < graph.Data.Securities.Count)
            result["note"] = "The graph draws the largest filers and the most widely held securities; the rest are " +
                             "in the data and the data tools read them.";

        PiecePose pose = graph.CommittedPose();
        Vector3 p = pose.pos, s = pose.scale;
        Quaternion r = pose.rot;
        result["position"] = new Dictionary<string, object> {
            { "right", Math.Round(p.x, 3) }, { "up", Math.Round(p.y, 3) }, { "forward", Math.Round(p.z, 3) }
        };
        result["turnedDegrees"] = Math.Round(r.eulerAngles.y, 1);
        result["scale"] = Math.Round(s.x, 3);
        result["legend"] = "Filers are orange cubes, sized by their reported portfolio; securities are blue spheres, " +
                           "sized by the value held in them here. An edge is a holding, thicker for a larger position.";
    }
}
