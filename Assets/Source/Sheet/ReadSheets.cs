using UnityEngine;

public class ReadSheets : ReadHub<ReadSheets.Reading, SheetTouch>
{
    public struct Reading
    {
        public bool valid;
        public CreateSheet sheet;
        public CreateCube cube;
        public int visRow;
        public int visCol;
        public int dataRow;
        public int dataCol;
        public Vector3 point;
        public Vector3 tip;
        public Vector3 wrist;
        public Vector3 normal;
    }

    protected override bool IsValid(Reading reading) => reading.valid;

    // A poke commits on the sheet it started on, wherever on it the bar is.
    protected override UnityEngine.Object PressTarget(Reading reading) => reading.sheet;

    public static Reading Describe(CreateCube cube, Vector3 point) => new Reading
    {
        valid = true,
        sheet = cube.Sheet,
        cube = cube,
        visRow = cube.visRow,
        visCol = cube.visCol,
        dataRow = cube.dataRow,
        dataCol = cube.dataCol,
        point = point
    };

    public static Reading Describe(PokeHit<CreateCube> hit, Vector3 tip, Vector3 wrist)
    {
        Reading reading = Describe(hit.target, hit.point);
        reading.tip = tip;
        reading.wrist = wrist;
        reading.normal = hit.normal;
        return reading;
    }
}
