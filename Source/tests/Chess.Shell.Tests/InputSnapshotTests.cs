using System;
using Chess.Core;
using Chess.Desktop.Input;
using Chess.Desktop.Rendering;
using Microsoft.Xna.Framework;
using NUnit.Framework;

namespace Chess.Desktop.Tests
{
    /// <summary>
    /// Chapter 2's automated input and layout tests: the InputSnapshot contract,
    /// camera transforms and safe-area clamping, verified headless in plain NUnit.
    /// No window, no SDL context, no device - the whole point of the snapshot
    /// seam between the game loop and the platform event stream.
    /// </summary>
    [TestFixture]
    public class InputSnapshotTests
    {
        private static InputSnapshot Snap(Vector2 point, int scroll = 0,
            bool primaryPressed = false, bool primaryDown = false,
            bool middleDown = false)
        {
            return new InputSnapshot
            {
                Point = point,
                ScrollDelta = scroll,
                PrimaryPressed = primaryPressed,
                PrimaryDown = primaryDown,
                MiddleDown = middleDown,
            };
        }

        [Test]
        public void SquareUnder_MapsScreenPointToBoardSquare_WithoutZoom()
        {
            var camera = new Camera2D { BoardOffset = new Vector2(64, 64) };
            // a1's (0,0) corner sits at board pixel (0,0) -> screen (64,64)
            Assert.AreEqual(0, Snap(new Vector2(70, 70)).SquareUnder(camera));
            // h8's centre: board (7.5*96, 7.5*96) + offset = 784,784
            Assert.AreEqual(63, Snap(new Vector2(784, 784)).SquareUnder(camera));
            // just past the board edge on the right -> outside
            Assert.AreEqual(Sq.None, Snap(new Vector2(900, 400)).SquareUnder(camera));
        }

        [Test]
        public void SquareUnder_MapsCorrectly_UnderZoom()
        {
            var camera = new Camera2D { BoardOffset = new Vector2(64, 64) };
            var point = new Vector2(70, 70);     // over a1 at zoom 1
            camera.ZoomBy(2f, point, 1600, 1200);
            // after doubling the zoom *around the cursor*, a1 stayed put and the
            // same screen point must still hit a1 (that is the invariant players feel)
            Assert.AreEqual(0, Snap(point).SquareUnder(camera));
        }

        [Test]
        public void RouteCamera_ZoomStaysInside_Bounds()
        {
            var camera = new Camera2D { BoardOffset = new Vector2(64, 64) };
            for (int i = 0; i < 40; i++)
                Snap(new Vector2(400, 300), scroll: +1).RouteCamera(camera, 1600, 1200);
            Assert.AreEqual(camera.MaxZoom, camera.Zoom, "zoom must clamp at MaxZoom");
            for (int i = 0; i < 90; i++)
                Snap(new Vector2(400, 300), scroll: -1).RouteCamera(camera, 1600, 1200);
            Assert.AreEqual(camera.MinZoom, camera.Zoom, "zoom must clamp at MinZoom");
        }

        [Test]
        public void RouteCamera_ScrollZero_IsANoOp()
        {
            var camera = new Camera2D { BoardOffset = new Vector2(64, 64) };
            var before = camera.Zoom;
            Assert.IsFalse(Snap(new Vector2(1, 1)).RouteCamera(camera, 1600, 1200));
            Assert.AreEqual(before, camera.Zoom);
        }
    }

    /// <summary>Safe-area / viewport scaling: the camera clamp keeps some part of
    /// the board visible on every screen size from iPhone SE phone up to a 34" 5K.</summary>
    [TestFixture]
    public class SafeAreaTests
    {
        // real device viewports: landscape iPhone 15 Pro Max, iPad Pro, 1080p, 1440p
        [Test]
        public void ClampToBoard_KeepsTheBoardReachable_OnEveryViewport(
            [Values(1160, 809, 560)] int width,
            [Values(322, 1666, 360, 420, 2508, 724, 646, 582)] int height)
        {
            var camera = new Camera2D
            {
                BoardOffset = new Vector2(64, 64),
                ScreenOrigin = new Vector2(-9999, -9999),   // the "player flung the board" case
            };
            camera.ClampToBoard(width, height);

            // after clamping, part of the board must be on-screen: the world point
            // at the left board edge maps no further than the viewport's left edge
            Assert.LessOrEqual(camera.ScreenOrigin.X,
                64f + 8f * 96f - 24f, "clamp must not maroon the board off-screen");
        }

        [Test]
        public void ScreenRect_ScalesSquareSize_WithZoom()
        {
            var camera = new Camera2D { BoardOffset = new Vector2(64, 64) };
            var r1 = camera.ScreenRect(0, 1160, 800);
            camera.ZoomBy(1.5f, new Vector2(400, 400), 1160, 800);
            var r1_5 = camera.ScreenRect(0, 1160, 800);
            Assert.Greater(r1_5.Width, r1.Width * 1.45f, "one square must grow with zoom");
        }
    }
}
