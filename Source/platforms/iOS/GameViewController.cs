using System;
using Foundation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Chess.Core;
using Chess.Desktop;
using Microsoft.Xna.Framework;
using UIKit;

namespace Chess.iOS
{
    /// <summary>
    /// The entire iOS/macOS bootstrap. MonoGame for iOS creates and owns its
    /// own UIWindow, so the shell's whole contribution is a delegate that
    /// hands UIKit a scene configuration (mandatory as of the Xcode 27 SDK)
    /// and lets <see cref="SceneDelegate"/> construct the shared
    /// <code>ChessGame</code> when the scene connects. Every orientation
    /// question is answered by view controller rotation methods MonoGame
    /// installs; the shell answers none itself.
    /// </summary>
    [Register("AppDelegate")]
    public class AppDelegate : UIApplicationDelegate
    {
        public override UIWindow Window { get; set; }

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            return true;   // window creation moved to the scene delegate
        }

        [Export("application:configurationForConnectingSceneSession:options:")]
        public UISceneConfiguration GetConfigurationForConnectingSceneSession(
            UIApplication application, UISceneSession connectingSceneSession,
            UISceneConnectionOptions options)
        {
            return new UISceneConfiguration("Default Configuration",
                connectingSceneSession.Role);
        }

        /* Heartbeat notes for the reader:
         *  - Suspended/reopen map straight onto MonoGame's Game loop (no
         *    code here): the same contract chapter 2 documented for Android.
         *  - Game.Exit() is a compile ERROR on iOS (CS0619 platform policy),
         *    so the shared shell's exit path is compiled out; the home
         *    gesture and task switcher control lifetime, as Apple intends. */
    }

    static class Program
    {
        static void Main(string[] args)
        {
            UIApplication.Main(args, null, "AppDelegate");
        }
    }

    /// <summary>iOS storage: the app's Documents directory (visible in the
    /// Files app), same IPlatformStorage contract as desktop.</summary>
    public class IOSStorage : IPlatformStorage
    {
        public string AppDataDirectory =>
            NSSearchPath.GetDirectories(NSSearchPathDirectory.DocumentDirectory,
                NSSearchPathDomain.User)[0];
        public string SaveDirectory => AppDataDirectory;
    }
}
