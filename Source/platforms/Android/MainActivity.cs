using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Chess.Desktop;
using System.IO;
using Microsoft.Xna.Framework;

namespace Chess.Android
{
    /// <summary>
    /// The entire Android bootstrap. MonoGame for Android supplies the GL surface
    /// and touch events; the Activity only has to declare fullscreen-only, the
    /// orientation policy (FullUser: the player rotates, we do not), and keep the
    /// standard lifecycle (onPause, which MonoGame maps onto Game loop pauses for
    /// free). The shared shell never assumes an aspect ratio: the 3D camera takes
    /// it from the live viewport every frame (_cam3d.SetAspect), so portrait and
    /// landscape are both first-class.
    /// </summary>
    [Activity(
        Label = "Buffy Chess",
        MainLauncher = true,
        Icon = "@mipmap/ic_launcher",
        Theme = "@style/Theme.Splash",
        AlwaysRetainTaskState = true,
        LaunchMode = LaunchMode.SingleInstance,
        ScreenOrientation = ScreenOrientation.FullUser,
        ConfigurationChanges = ConfigChanges.Orientation
            | ConfigChanges.Keyboard
            | ConfigChanges.KeyboardHidden
            | ConfigChanges.ScreenSize)]
    public class MainActivity : AndroidGameActivity
    {
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var game = new ChessGame();
            SetContentView(game.Services.GetService<View>());
            game.Run();
        }
    }

    /// <summary>Android storage: context-scoped app files, same SaveDirectory
    /// contract IPlatformStorage defines (chapter 11).</summary>
    public class AndroidStorage : Chess.Core.IPlatformStorage
    {
        public string AppDataDirectory => Application.Context.FilesDir.AbsolutePath;
        public string SaveDirectory => AppDataDirectory;
    }

    /// <summary>Skin root for chapter 5's picker: app-private Files storage, so
    /// even a partially-installed skin never escapes our sandbox on any vendor
    /// fork. The well-known subpath mirrors desktop and iOS exactly.</summary>
    public static class AndroidSkinPaths
    {
        public static string UserRoot => Path.Combine(
            Application.Context.FilesDir.AbsolutePath, "Skins");
    }
}
