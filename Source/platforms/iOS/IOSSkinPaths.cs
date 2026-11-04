using System.IO;
using Foundation;

namespace Chess.iOS
{
    /// <summary>Skin root for chapter 5's picker: the Documents directory, so
    /// players can also drop a skin in through the Files app. The well-known
    /// subpath mirrors desktop and Android exactly.</summary>
    public static class IOSSkinPaths
    {
        public static string UserRoot => Path.Combine(
            NSSearchPath.GetDirectories(NSSearchPathDirectory.DocumentDirectory,
                NSSearchPathDomain.User)[0], "Skins");
    }
}
