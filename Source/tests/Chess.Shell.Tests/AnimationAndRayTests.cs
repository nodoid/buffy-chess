using System;
using System.Collections.Generic;
using Chess.Core;
using Chess.Desktop.Rendering3D;
using Microsoft.Xna.Framework;
using NUnit.Framework;

namespace Chess.Shell.Tests
{
    /// <summary>
    /// Chapter 7's automated animation sanity + 3D pick tests. Everything here
    /// is pure matrix and vector maths - no GraphicsDevice, no window, no
    /// GPU - which is exactly why the pick path and the tween curves were
    /// designed as static pure functions.
    /// </summary>
    [TestFixture]
    public class AnimationAndRayTests
    {
        // ---------------------------------------------------------- camera

        [Test]
        public void Camera_EyeStaysOnSphere_WhenOrbiting()
        {
            var cam = new OrbitCamera3D(16f / 9f);
            float before = Vector3.Distance(cam.Eye, cam.Target);
            cam.OrbitBy(0.7f, -0.2f);
            float after = Vector3.Distance(cam.Eye, cam.Target);
            Assert.That(after, Is.EqualTo(before).Within(1e-4));
        }

        [Test]
        public void Camera_PitchClamps_AboveHorizonAndBelowZenith()
        {
            var cam = new OrbitCamera3D(16f / 9f);
            cam.OrbitBy(0f, -999f);
            Assert.That(cam.Pitch, Is.EqualTo(OrbitCamera3D.MinPitch).Within(1e-5));
            cam.OrbitBy(0f, 999f);
            Assert.That(cam.Pitch, Is.EqualTo(OrbitCamera3D.MaxPitch).Within(1e-5));
        }

        [Test]
        public void Camera_ZoomClamps_ToTunableExtent()
        {
            var cam = new OrbitCamera3D(16f / 9f);
            for (int i = 0; i < 200; i++) cam.ZoomBy(1.12f);
            Assert.That(cam.Distance, Is.EqualTo(OrbitCamera3D.MinDistance).Within(1e-4));
            for (int i = 0; i < 400; i++) cam.ZoomBy(1 / 1.12f);
            Assert.That(cam.Distance, Is.EqualTo(OrbitCamera3D.MaxDistance).Within(1e-4));
        }

        [Test]
        public void Camera_PanIsScreenRelative_DragLeftDragsThePlayersLeft()
        {
            var cam = new OrbitCamera3D(16f / 9f);
            cam.Yaw = 0f;                               // stand on the +X side exactly
            Vector3 t0 = cam.Target;

            // yaw 0: the player stands on the +X side, so "left" is +Z
            cam.PanBy(-1.5f, 0f);
            Assert.That(cam.Target.X, Is.EqualTo(t0.X).Within(1e-4));
            Assert.That(cam.Target.Z, Is.GreaterThan(t0.Z));

            cam.Target = t0; cam.Yaw = MathF.PI / 2f;   // now standing on +Z
            cam.PanBy(-1.5f, 0f);
            Assert.That(cam.Target.X, Is.LessThan(t0.X));
            Assert.That(cam.Target.Z, Is.EqualTo(t0.Z).Within(1e-4));
        }

        // ---------------------------------------------------------- picking

        [Test]
        public void Ray_StraightDown_HitsTheRightSquare()
        {
            // straight down onto the centre of d4 (file 3, rank 3 -> square 27)
            var origin = new Vector3(3.5f, 5f, 3.5f);
            var dir = new Vector3(0f, -1f, 0f);
            var (sq, hitPiece) = BoardRay.Pick(origin, dir, _ => false);
            Assert.That(hitPiece, Is.False);
            Assert.That(Sq.Name(sq), Is.EqualTo("d4"));
        }

        [Test]
        public void Ray_OffTheBoard_ReportsNone()
        {
            var origin = new Vector3(-3f, 4f, -3f);
            var dir = new Vector3(0f, -1f, 0f);
            var (sq, _) = BoardRay.Pick(origin, dir, _ => false);
            Assert.That(sq, Is.EqualTo(Sq.None));
        }

        [Test]
        public void Ray_HitsTheNearestPieceSphere_NotThePlaneBehindIt()
        {
            var origin = new Vector3(0.5f, 3f, 0.5f);   // straight above the a1 rook
            var dir = new Vector3(0f, -1f, 0f);
            var pos = Fens.ToPosition("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
            var (sq, hitPiece) = BoardRay.Pick(origin, dir,
                s => !pos[s].IsNone);
            Assert.That(hitPiece, Is.True, "a piece must win before the board plane");
            Assert.That(Sq.Name(sq), Is.EqualTo("a1"));
        }

        [Test]
        public void Ray_UnnormalizedDirection_HitsTheSameSphere()
        {
            var origin = new Vector3(0.5f, 3f, 0.5f);
            var dirLong = new Vector3(0f, -7f, 0f);     // same line, sloppier vector
            int a = BoardRay.Pick(origin, dirLong, _ => false).Square;
            var dirShort = new Vector3(0f, -1f, 0f);
            int b = BoardRay.Pick(origin, dirShort, _ => false).Square;
            Assert.That(Sq.Name(a), Is.EqualTo(Sq.Name(b)));
        }

        [Test]
        public void Camera_RayThrough_DownTheCentre_OfTheTeachingView()
        {
            // unproject the screen centre at the default teaching angle: the
            // ray must come down somewhere inside the 8x8 cell region
            var cam = new OrbitCamera3D(16f / 9f) { Pitch = 1.4f };
            var (_, dir) = cam.RayThrough(800, 450, 1600, 900);
            Assert.That(dir.Y, Is.LessThan(-0.3f), "must look downward at the board");
        }

        // ---------------------------------------------------------- tweens

        [Test]
        public void Tween_Slide_LandsExactly_OnBothEnds()
        {
            var job = AnimJob.SlideJob(Sq.FromName("e2"), Sq.FromName("e4"));
            var at0 = PieceAnimator.Eval(job, 0f);
            var at1 = PieceAnimator.Eval(job, 1f);
            Assert.That(at0.X, Is.EqualTo(PieceAnimator.World(job.FromSq).X).Within(1e-4));
            Assert.That(at1.X, Is.EqualTo(PieceAnimator.World(job.ToSq).X).Within(1e-4));
        }

        [Test]
        public void Tween_Slide_IsMonotonicAlongItsPath()
        {
            var job = AnimJob.SlideJob(Sq.FromName("a1"), Sq.FromName("h8"));
            float prev = -1f;
            for (float t = 0; t <= 1.001f; t += 0.05f)
            {
                float x = PieceAnimator.Eval(job, t).X;
                Assert.That(x, Is.GreaterThanOrEqualTo(prev - 1e-5));
                prev = x;
            }
        }

        [Test]
        public void Tween_KnightHop_LeavesAndReturnsTo_TheBoardPlane()
        {
            var job = AnimJob.HopJob(Sq.FromName("g1"), Sq.FromName("f3"), apex: 0.9f);
            float start = PieceAnimator.Eval(job, 0f).Y;
            float end = PieceAnimator.Eval(job, 1f).Y;
            float mid = PieceAnimator.Eval(job, 0.5f).Y;

            Assert.That(start, Is.EqualTo(0f).Within(1e-5));
            Assert.That(end, Is.EqualTo(0f).Within(1e-5));
            Assert.That(mid, Is.EqualTo(0.9f).Within(1e-4), "apex reached at t = 0.5");
        }

        [Test]
        public void Tween_Capture_SinksBelowThePlane_AndStaysThere()
        {
            var job = AnimJob.CaptureJob(Sq.FromName("e5"), Side.Black);
            var end = PieceAnimator.CapturePos(job, 1f, PieceAnimator.World(Sq.FromName("e5")));
            Assert.That(end.Y, Is.LessThan(-0.3f), "the captured piece ends under the board");
        }

        [Test]
        public void Animator_TimelineCompletes_JobsAreRemoved()
        {
            var anim = new PieceAnimator();
            anim.PlayMove(new Move(Sq.FromName("e2"), Sq.FromName("e4")), null, Side.White);
            Assert.That(anim.IsAnimating, Is.True);

            for (int i = 0; i < 600; i++) anim.Advance(1f / 60f);   // 10 virtual seconds
            Assert.That(anim.IsAnimating, Is.False, "a 0.6 s max tween must complete");
        }
    }

    /// <summary>FEN string -> Position. The engine's own parser does the
    /// decoding, so the tests are pinned to the real rules, not a copy.</summary>
    static class Fens
    {
        public static Position ToPosition(string board)
        {
            var p = new Position();
            p.SetFen(board + " w KQkq - 0 1");
            return p;
        }
    }
}
