using System;
using Chess.Core;
using Microsoft.Xna.Framework;

namespace Chess.Desktop.Rendering3D
{
    /// <summary>
    /// Pure ray maths for picking: screen point -> world ray -> board plane and
    /// piece spheres. No GraphicsDevice anywhere, so NUnit verifies the whole
    /// chapter 7 pick path headless (see AnimationAndRayTests).
    /// World convention: +X runs files a to h, +Z runs ranks 1 to 8, y = 0 is the
    /// board surface. Square s maps to world (FileOf(s), 0, RankOf(s)) + 0.5 centre.
    /// </summary>
    public static class BoardRay
    {
        public const float Cell = 1f;   // one board square = one world metre

        /// <summary>Ray-vs-plane hit on the board surface.
        /// Returns t &gt;= 0 on the ray (<= far...) or -1 when the ray misses
        /// the plane entirely (parallel or pointing away).</summary>
        public static float HitBoardPlane(Vector3 origin, Vector3 dir, float planeY = 0f)
        {
            if (MathF.Abs(dir.Y) < 1e-6f) return -1f;
            float t = (planeY - origin.Y) / dir.Y;
            return t >= 0f ? t : -1f;
        }

        /// <summary>Board plane point of a ray, or null.</summary>
        public static Vector3? PlanePoint(Vector3 origin, Vector3 dir, float planeY = 0f)
        {
            float t = HitBoardPlane(origin, dir, planeY);
            return t < 0f ? null : origin + dir * t;
        }

        /// <summary>Square whose cell contains the plane point, or Sq.None.
        /// LERF-consistent: square = rank * 8 + file.</summary>
        public static int SquareAt(Vector3 planePoint)
        {
            int file = (int)MathF.Floor(planePoint.X / Cell);
            int rank = (int)MathF.Floor(planePoint.Z / Cell);
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return Sq.None;
            return Sq.Make(file, rank);
        }

        /// <summary>Ray vs a piece bounding sphere (centre, radius in metres).
        /// Handles an unnormalised direction: the quadratic's a = d dot d.
        /// Returns the ray parameter t of the first hit, or -1.</summary>
        public static float HitSphere(Vector3 origin, Vector3 dir, Vector3 centre, float radius)
        {
            float a = dir.LengthSquared();
            if (a < 1e-10f) return -1f;
            var oc = origin - centre;
            float halfB = Vector3.Dot(oc, dir);
            float c = oc.LengthSquared() - radius * radius;
            float disc = halfB * halfB - a * c;
            if (disc < 0f) return -1f;
            float root = MathF.Sqrt(disc);
            float t0 = (-halfB - root) / a, t1 = (-halfB + root) / a;
            if (t0 >= 0f) return t0;
            if (t1 >= 0f) return t1;
            return -1f;
        }

        /// <summary>Full pick: nearest piece sphere wins; otherwise the board
        /// plane; otherwise none. The 'occupied' callback must reflect the
        /// *logical* board, so captured-away pieces never report a hit.</summary>
        public static (int Square, bool Piece) Pick(
            Vector3 origin, Vector3 dir,
            Func<int, bool> occupiedSquares)
        {
            // piece spheres: centre at cell centre, radius tuned per piece height class
            float bestT = float.MaxValue;
            int bestSq = Sq.None;
            for (int sq = 0; sq < 64; sq++)
            {
                if (!occupiedSquares(sq)) continue;
                float h = PieceRadius(sq, occupiedSquares);
                Vector3 centre = new Vector3(Sq.FileOf(sq) * Cell + 0.5f, h, Sq.RankOf(sq) * Cell + 0.5f);
                float t = HitSphere(origin, dir, centre, h * 0.95f);
                if (t >= 0f && t < bestT) { bestT = t; bestSq = sq; }
            }
            if (bestSq != Sq.None) return (bestSq, true);

            float planeT = HitBoardPlane(origin, dir);
            if (planeT < 0f) return (Sq.None, false);
            return (SquareAt(PlanePoint(origin, dir).Value), false);
        }

        /// <summary>Piece hit-sphere radius by file pair: queen/king sit on the
        /// d- and e-files (files 3 and 4) and are easier to poke than pawns.</summary>
        static float PieceRadius(int sq, Func<int, bool> occupied)
        {
            int file = Sq.FileOf(sq);
            return file == 3 || file == 4 ? 0.45f : 0.35f;
        }
    }
}
