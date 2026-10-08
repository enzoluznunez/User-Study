public class MoveTool : GrabTool
{
    protected override ToolType Kind => ToolType.Move;
    protected override EditKind EditKind => EditKind.Move;
    protected override bool TwoHanded => false;
    protected override string Verb(string what, float metres) => $"moved the {what} {metres:0.00}m";
}
