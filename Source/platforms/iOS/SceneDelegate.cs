using System;
using System.Collections.Generic;
using System.Reflection;
using Chess.Desktop;
using Foundation;
using Microsoft.Xna.Framework;
using UIKit;

namespace Chess.iOS
{
    /// <summary>
    /// Scene-lifecycle adoption shim for iOS 27 (Xcode 27 SDK mandates the
    /// UIScene life cycle). MonoGame 3.8.2 predates it: its iOSGamePlatform
    /// still builds <code>new UIWindow(UIScreen.MainScreen.Bounds)</code>, a
    /// window attached to no scene, which UIKit now kills at launch with
    /// <code>UIApplicationEvaluateRuntimeIssueForNoSceneLifecycleAdoption</code>.
    /// The fix is small: when the scene connects, build a scene-attached window,
    /// move MonoGame's GL view across, and point the platform's private
    /// <code>_mainWindow</code> field at the new window before StartRunLoop
    /// calls MakeKeyAndVisible. The shared shell itself never changes.
    /// </summary>
    [Register("SceneDelegate")]
    public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
    {
        private ChessGame _game;

        [Export("scene:willConnectToSession:options:")]
        public void WillConnect(UIScene scene, UISceneSession session,
                                UISceneConnectionOptions connectionOptions)
        {
            if (scene is not UIWindowScene windowScene)
                return;

            // The shared shell - the exact same class the desktop and Android
            // booms construct. MonoGame's platform constructor runs here and
            // creates its detached window, which we immediately re-parent.
            _game = new ChessGame();
            AdoptSceneWindow(_game, windowScene);
            _game.Run();
        }

        [Export("sceneDidBecomeActive:")]
        public void SceneDidBecomeActive(UIScene scene) { /* MonoGame observes UIApplication notifications itself */ }

        [Export("sceneWillResignActive:")]
        public void SceneWillResignActive(UIScene scene) { }

        [Export("sceneDidEnterBackground:")]
        public void SceneDidEnterBackground(UIScene scene) { }

        [Export("sceneWillEnterForeground:")]
        public void SceneWillEnterForeground(UIScene scene) { }

        /// <summary>Move MonoGame's detached window under a real UIWindowScene
        /// by reflective field swap — the one pragmatic glue line this platform
        /// needs until MonoGame ships scene support.</summary>
        private static void AdoptSceneWindow(Game game, UIWindowScene windowScene)
        {
            var view = ((UIViewController)game.Services
                .GetService(typeof(UIViewController))).View;

            var win = new UIWindow(windowScene)
                { Frame = windowScene.CoordinateSpace.Bounds };
            win.Add(view); // removes the view from the detached window implicitly

            var platform = FindGamePlatform(game);
            var field = platform?.GetType().GetField("_mainWindow",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(platform, win);
        }

        private static object FindGamePlatform(Game game)
        {
            var dict = game.Services.GetType()
                .GetField("services", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(game.Services) as IEnumerable<KeyValuePair<Type, object>>;
            if (dict == null) return null;

            foreach (var entry in dict)
            {
                if (entry.Value.GetType().GetField("_mainWindow",
                        BindingFlags.NonPublic | BindingFlags.Instance) != null)
                    return entry.Value;
            }
            return null;
        }
    }
}
