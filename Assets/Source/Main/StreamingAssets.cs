using System.IO;
using UnityEngine;

public static class StreamingAssets
{
    // A file in StreamingAssets as a URL UnityWebRequest can read. On Android
    // the folder is inside the .apk, where only a web request reaches it, so
    // everywhere reads it the same way, through file:// off the headset.
    public static string Url(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        return path.Contains("://") ? path : "file://" + path;
    }
}
