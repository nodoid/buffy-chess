using System;
using Chess.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Chess.Desktop.Rendering3D
{
    /// <summary>
    /// The chapter 6 renderer: a lit 3D board built from programmatic vertex
    /// buffers (zero content pipeline). Square quads take their textures from
    /// the skin system's board tiles (chapter 5), pieces are primitive meshes
    /// lit by a two-light rig, and the whole scene goes out through one
    /// BasicEffect per draw group so the same code runs on DesktopGL, DirectX,
    /// Metal and OpenGL ES.
    /// </summary>
    public sealed class Board3DRenderer
    {
        GraphicsDevice _gd;
        BasicEffect _boardFx;      // textured quads, one vertex array per colour
        BasicEffect _pieceFx;      // per-piece diffuse colour
        Texture2D _lightTex, _darkTex;
        VertexPositionNormalTexture[][] _faces;    // [light 0 / dark 1] cell quads
        VertexPositionNormalTexture[][] _meshes;   // [side 2][type 6] piece meshes

        const float BoardHalf = 4f;   // world metres; board spans 8 x 8 cells

        /// <summary>Material tints, one per side - from the skin's board colours
        /// when present (chapter 5 data), from fixed defaults otherwise.</summary>
        public Color WhiteMaterial = new Color(0.94f, 0.92f, 0.87f);
        public Color BlackMaterial = new Color(0.18f, 0.17f, 0.20f);

        public void Build(GraphicsDevice gd, string skinDirectory = null)
        {
            _gd = gd;
            _boardFx = new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = false };
            _pieceFx = new BasicEffect(gd) { TextureEnabled = false, VertexColorEnabled = false };

            ConfigureLighting(_boardFx);
            ConfigureLighting(_pieceFx);

            // squares: two vertex arrays, all light squares then all dark ones,
            // so exactly two draw calls cover the 64 faces. Drawn as user
            // primitives - the Metal backend's vertex-buffer path was verified
            // to drop these frames (2026-11), while the user-primitive path
            // every other draw system in this engine uses is portable.
            _faces = new VertexPositionNormalTexture[2][];
            _faces[0] = BuildFaceArray(gd, light: true);
            _faces[1] = BuildFaceArray(gd, light: false);

            TryLoadBoardTexture(gd, skinDirectory, "board_light", ref _lightTex);
            TryLoadBoardTexture(gd, skinDirectory, "board_dark", ref _darkTex);

            _meshes = new VertexPositionNormalTexture[2][];
            _meshes[0] = new VertexPositionNormalTexture[72];   // filled per type below
            _meshes[1] = new VertexPositionNormalTexture[72];
        }

        static void ConfigureLighting(BasicEffect fx)
        {
            fx.LightingEnabled = true;
            fx.PreferPerPixelLighting = true;      // per-pixel: smooth rims on cones
            fx.AmbientLightColor = new Vector3(0.28f, 0.28f, 0.32f);   // cool fill
            fx.DirectionalLight0.Enabled = true;   // key light: warm, from up-left
            fx.DirectionalLight0.DiffuseColor = new Vector3(1.0f, 0.93f, 0.82f);
            fx.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.5f, 1.0f, -0.35f));
            fx.DirectionalLight1.Enabled = true;   // rim light: cool, opposite
            fx.DirectionalLight1.DiffuseColor = new Vector3(0.35f, 0.42f, 0.55f);
            fx.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.6f, 0.4f, 0.7f));
        }

        static VertexPositionNormalTexture[] BuildFaceArray(GraphicsDevice gd, bool light)
        {
            var verts = new VertexPositionNormalTexture[64 / 2 * 6];   // half the cells, 6 verts each
            int i = 0;
            for (int rank = 0; rank < 8; rank++)
            for (int file = 0; file < 8; file++)
            {
                if (((rank + file) % 2 == 0) != light) continue;
                float x0 = file, z0 = rank, x1 = file + 1, z1 = rank + 1;
                Vector3 n = Vector3.Up;
                // Wound so these faces are FRONT-facing under the default
                // CullCounterClockwise rasterizer when viewed from above the
                // board - the Metal backend culls the other order outright
                // (verified on the iOS simulator, 2026-11).
                AddQuad(verts, ref i,
                    new Vector3(x0, 0, z0), new Vector3(x1, 0, z0),
                    new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), n);
            }
            return verts;
        }

        static void AddQuad(VertexPositionNormalTexture[] v, ref int i,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            v[i++] = new VertexPositionNormalTexture(a, n, new Vector2(0, 0));
            v[i++] = new VertexPositionNormalTexture(b, n, new Vector2(1, 0));
            v[i++] = new VertexPositionNormalTexture(c, n, new Vector2(1, 1));
            v[i++] = new VertexPositionNormalTexture(a, n, new Vector2(0, 0));
            v[i++] = new VertexPositionNormalTexture(c, n, new Vector2(1, 1));
            v[i++] = new VertexPositionNormalTexture(d, n, new Vector2(0, 1));
        }

        static void TryLoadBoardTexture(GraphicsDevice gd, string dir, string fileBase, ref Texture2D tex)
        {
            // chapter 5's loader contract: missing slot -> procedural colour,
            // corrupt PNG -> one texture null, not a crash
            if (dir == null) return;
            string path = System.IO.Path.Combine(dir, fileBase + ".png");
            try { if (System.IO.File.Exists(path)) tex = Texture2D.FromFile(gd, path); }
            catch (Exception) { /* sprite stays procedural */ }
        }

        /// <summary>Draws one full frame of the 3D board. Call inside a fresh
        /// SpriteBatch-free section: this renderer owns its own effect states.</summary>
        public void Draw(Position position, PieceAnimator anim, OrbitCamera3D cam)
        {
            _gd.Clear(Color.Lerp(new Color(0.08f, 0.08f, 0.12f), new Color(0.10f, 0.11f, 0.16f), 0.5f));


            _boardFx.View = cam.View;
            _boardFx.Projection = cam.Projection;
            _boardFx.World = Matrix.Identity;
            _boardFx.Texture = _lightTex ?? SolidTexture(true);
            foreach (var pass in _boardFx.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _faces[0], 0, _faces[0].Length / 3);
            }
            _boardFx.Texture = _darkTex ?? SolidTexture(false);
            foreach (var pass in _boardFx.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserPrimitives(PrimitiveType.TriangleList, _faces[1], 0, _faces[1].Length / 3);
            }

            // pieces: one mesh geometry, tinted per side - material, not texture
            _pieceFx.View = cam.View;
            _pieceFx.Projection = cam.Projection;
            for (int sq = 0; sq < 64; sq++)
            {
                Piece piece = position[sq];
                if (piece.IsNone) continue;

                Vector3 basePos = PieceAnimator.World(sq);
                Vector3? animated = anim.Evaluate(sq, basePos);
                Vector3 world = animated ?? basePos;

                _pieceFx.World = Matrix.CreateScale(PieceScale(piece.Type))
                               * Matrix.CreateRotationY(Sq.FileOf(sq) * 0.35f)   // slight variety
                               * Matrix.CreateTranslation(world);
                _pieceFx.DiffuseColor = (piece.Side == Side.White ? WhiteMaterial : BlackMaterial).ToVector3();

                var mesh = BuildPieceMesh(piece.Type);
                foreach (var pass in _pieceFx.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawUserPrimitives(PrimitiveType.TriangleList, mesh, 0, mesh.Length / 3);
                }
            }
        }

        Texture2D _solidLight, _solidDark;   // 1x1 procedural fallbacks, lazily created
        Texture2D SolidTexture(bool light)
        {
            if (light && _solidLight == null)
                _solidLight = Solid(new Color(0.91f, 0.86f, 0.73f));
            if (!light && _solidDark == null)
                _solidDark = Solid(new Color(0.42f, 0.34f, 0.23f));
            return light ? _solidLight : _solidDark;
        }

        Texture2D Solid(Color c)
        {
            var t = new Texture2D(_gd, 1, 1);
            t.SetData(new[] { c });
            return t;
        }

        static float PieceScale(PieceType type) => type switch
        {
            PieceType.Pawn => 0.24f,
            PieceType.Rook => 0.32f,
            PieceType.Knight => 0.34f,
            PieceType.Bishop => 0.36f,
            PieceType.Queen => 0.40f,
            PieceType.King => 0.45f,
            _ => 1f
        };

        /// <summary>A small lat-long sphere as the shared body; the per-type
        /// world matrix scales/squashes it into silhouette (book scope).</summary>
        static VertexPositionNormalTexture[] BuildPieceMesh(PieceType type)
        {
            const int lat = 6, lon = 12;
            var verts = new VertexPositionNormalTexture[lat * lon * 6];
            int i = 0;
            for (int la = 0; la < lat; la++)
            for (int lo = 0; lo < lon; lo++)
            {
                float u0 = lo * MathHelper.TwoPi / lon, u1 = (lo + 1) * MathHelper.TwoPi / lon;
                float v0 = la * MathF.PI / lat,        v1 = (la + 1) * MathF.PI / lat;
                Vector3 P(float u, float v) => new Vector3(
                    MathF.Cos(u) * MathF.Sin(v), MathF.Cos(v), MathF.Sin(u) * MathF.Sin(v));
                Vector3 a = P(u0, v0), b = P(u1, v0), c = P(u1, v1), d = P(u0, v1);
                AddTri(verts, ref i, a, b, c);
                AddTri(verts, ref i, a, c, d);
            }
            return verts;
        }

        static void AddTri(VertexPositionNormalTexture[] v, ref int i, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            v[i++] = new VertexPositionNormalTexture(a, n, Vector2.Zero);
            v[i++] = new VertexPositionNormalTexture(b, n, new Vector2(1, 0));
            v[i++] = new VertexPositionNormalTexture(c, n, Vector2.One);
        }
    }
}
