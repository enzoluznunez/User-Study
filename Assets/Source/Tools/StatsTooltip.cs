public static class StatsTooltip
{
    // Takes the card down, or only the one 'owner' put up when one is named.
    public static void Hide(object owner = null)
    {
        Tooltip tooltip = Scene.Tooltip;
        if (tooltip != null) tooltip.HideStats(owner);
    }

    public static bool TryResolve(ManageSheets sheetManager, ReadSheets.Reading reading,
        out Tooltip tooltip, out DataSource data, out CreateSheet piece)
    {
        tooltip = Scene.Tooltip;
        data = Scene.Data;
        piece = null;
        if (tooltip == null || data == null || sheetManager == null) return false;

        piece = reading.sheet != null
            ? reading.sheet
            : sheetManager.SheetAt(reading.visRow, reading.visCol);
        return piece != null;
    }
}
