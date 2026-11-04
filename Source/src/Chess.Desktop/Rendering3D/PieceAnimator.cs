using System;
using System.Collections.Generic;
using Chess.Core;
using Microsoft.Xna.Framework;

namespace Chess.Desktop.Rendering3D
{
    /// <summary>What kind of motion a tween plays. One enum, three curves.</summary>
    public enum AnimKind : byte { Slide = 0, Capture = 1, KnightHop = 2 }

    /// <summary>
    /// One immutable description of a piece tween. <see cref="PieceAnimator.Evaluate"/>
    /// is a pure function of (job, t), so the NUnit "animation sanity" tests on
    /// chapter 7 exercise timelines headless - no GPU, no window.
    /// </summary>
    public struct AnimJob
    {
        public AnimKind Kind;
        public int FromSq;      // LERF square the piece starts on (slide/hop)
        public int ToSq;        // LERF square it ends on
        public Side Mover;
        public Vector3 Apex;    // knight parabola apex, in world units above the midpoint
        public float Duration;  // seconds; slides are distance-proportional, hops clamp at 0.55 s

        public static AnimJob SlideJob(int fromSq, int toSq, float speed = 2.2f)
        {
            float d = Vector2.Distance(
                new Vector2(Sq.FileOf(fromSq), Sq.RankOf(fromSq)),
                new Vector2(Sq.FileOf(toSq), Sq.RankOf(toSq)));
            return new AnimJob { Kind = AnimKind.Slide, FromSq = fromSq, ToSq = toSq,
                                 Duration = Math.Clamp(d / speed, 0.12f, 0.6f) };
        }

        public static AnimJob CaptureJob(int capturedSq, Side remover)
            => new AnimJob { Kind = AnimKind.Capture, FromSq = capturedSq, Mover = remover, Duration = 0.25f };

        public static AnimJob HopJob(int fromSq, int toSq, float apex = 0.9f)
            => new AnimJob { Kind = AnimKind.KnightHop, FromSq = fromSq, ToSq = toSq,
                             Apex = new Vector3(0, apex, 0), Duration = 0.55f };
    }

    /// <summary>
    /// Interpolation physics: piece tweens layered over the *logical* board.
    /// The engine answers every query instantly with the destination position;
    /// the animator only bends *where the pixels are*, never the game state -
    /// that separation is what makes the tests in chapter 7 possible.
    /// </summary>
    public sealed class PieceAnimator
    {
        readonly List<(AnimJob job, float t)> _active = new();

        public bool IsAnimating => _active.Count > 0;
        public IReadOnlyList<(AnimJob job, float t)> Active => _active;

        /// <summary>Queue a move animation: both squares involved. The
        /// destination position may be null (tests queue bare tweens); in that
        /// case knight detection falls back to the side parameter only.</summary>
        public void PlayMove(Move move, Position after, Side mover)
        {
            Piece landed = after != null ? after[move.To] : default;
            if (landed.Type == PieceType.Knight && move.From != move.To)
                _active.Add((AnimJob.HopJob(move.From, move.To), 0f));
            else
                _active.Add((AnimJob.SlideJob(move.From, move.To), 0f));

            // a capture: the dying piece sinks one tick behind its slayer
            Side moverSide = landed.IsNone ? mover : landed.Side;
            if (move.IsCapture)
                _active.Add((AnimJob.CaptureJob(CapturedSquare(move, moverSide), moverSide), 0.02f));
            else if (move.IsCastle)
            {
                // the rook slides on its own two-square run, lerped the same way
                int rookFrom = move.Kind == MoveKind.KingSideCastle ? move.To + 1 : move.To - 2;
                int rookTo   = move.Kind == MoveKind.KingSideCastle ? move.To - 1 : move.To + 1;
                _active.Add((AnimJob.SlideJob(rookFrom, rookTo), 0f));
            }
        }

        /// <summary>Where the captured piece stood: the destination square for
        /// normal captures, the passed-over square for en passant.</summary>
        static int CapturedSquare(Move move, Side mover)
        {
            if (move.Kind != MoveKind.EnPassant) return move.To;
            return Sq.Make(Sq.FileOf(move.To), Sq.RankOf(move.To) + (mover == Side.White ? -1 : +1));
        }

        /// <summary>Queue a capture removal on its own square.</summary>
        public void PlayCapture(int capturedSq) =>
            _active.Add((AnimJob.CaptureJob(capturedSq, Side.White), 0f));

        /// <summary>Cancel everything - used when a saved game is loaded
        /// mid-tween (chapter 8) or the user resizes violently.</summary>
        public void Clear() => _active.Clear();

        public void Advance(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var (job, t) = _active[i];
                t += dt / job.Duration;
                if (t >= 1f) _active.RemoveAt(i);
                else _active[i] = (job, t);
            }
        }

        /// <summary>Where is a piece *visually* right now? Base position is the
        /// logical board spot; any job touching that square overrides it.</summary>
        public Vector3? Evaluate(int square, Vector3 basePos)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                var (job, t) = _active[i];
                switch (job.Kind)
                {
                    case AnimKind.Slide:
                    case AnimKind.KnightHop:
                        if (job.FromSq == square) return Eval(job, t);
                        break;
                    case AnimKind.Capture:
                        if (job.FromSq == square) return CapturePos(job, t, basePos);
                        break;
                }
            }
            return null;   // not animated: draw at the logical spot
        }

        /// <summary>Pure curve evaluation for a job; the book's test target.</summary>
        public static Vector3 Eval(AnimJob job, float t)
        {
            Vector3 a = World(job.FromSq), b = World(job.ToSq);
            float e = EaseInOut(t);

            if (job.Kind == AnimKind.KnightHop)
            {
                Vector3 p = Vector3.Lerp(a, b, e);
                float arc = 4f * t * (1f - t);            // 0 at both ends, 1 at mid
                p.Y += job.Apex.Y * arc;
                return p;
            }
            return Vector3.Lerp(a, b, e);
        }

        /// <summary>Captures sink: slight lift, quick fade shrink, and at the end
        /// the piece is beneath the plane (the renderer clips it away).</summary>
        public static Vector3 CapturePos(AnimJob job, float t, Vector3 basePos)
        {
            float e = EaseIn(t);
            return basePos + new Vector3(0, 0.6f * e, 0) - new Vector3(0, 1.2f * e * e, 0);
        }

        /// <summary>Frame-rate independent ease: smoothstep-ish, symmetric.</summary>
        public static float EaseInOut(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Captures accelerate: a fast-in curve reads as "the piece lost".</summary>
        public static float EaseIn(float t) => Math.Clamp(t, 0f, 1f) * Math.Clamp(t, 0f, 1f);

        public static Vector3 World(int square) =>
            new Vector3(Sq.FileOf(square) * BoardRay.Cell + 0.5f, 0, Sq.RankOf(square) * BoardRay.Cell + 0.5f);
    }
}
