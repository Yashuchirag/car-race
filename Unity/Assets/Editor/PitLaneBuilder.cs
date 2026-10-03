using System.Collections.Generic;
using CarRace.Track;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// The pit lane's furniture, from the same TrackData.EnsurePitLane the AI drive, so the paint
    /// is where the cars go: the pit wall between the entry and exit lines, a catch fence on it,
    /// the line between the fast and working lanes, the lane's outer line, the entry and exit
    /// lines across it, a yellow box for each car, and 60 boards at the entry line. Called from
    /// TrackSceneBuilder.Build, as StartFinishBuilder is; the asphalt itself is TrackSceneBuilder's,
    /// laid where the right verge was.
    /// </summary>
    public static class PitLaneBuilder
    {
        const float WallHeightM = 1.0f, WallThicknessM = 0.5f, WallFootM = 0.5f;
        const float FenceTopM = 3.3f;
        const float LineM = 0.15f, CrossLineM = 0.4f, LiftM = 0.02f;
        const float BoxLengthM = 5f, BoxWidthM = 3.2f;
        const float BoardHeightM = 2.6f;

        /// <summary>How far past the road's edge the pit lane's far side is, for whatever has to
        /// stand clear of it (the gantry's leg).</summary>
        public static float OuterFromEdgeM => TrackData.PitWallGapM + TrackData.PitFastLaneM + TrackData.PitWorkingLaneM;

        public static void Build(GameObject root, TrackData pit, Vector3[] centre, Vector3[] right, Vector3[] rightEdge,
                                 float[] widthRight, int barrierLayer, PhysicsMaterial barrier)
        {
            if (!pit.HasPitLane) return;
            var parent = new GameObject("Pit Lane");
            parent.transform.SetParent(root.transform, false);
            int n = centre.Length;
            var concrete = StartFinishBuilder.Lit("Assets/Materials/PitWall.mat", new Color(0.62f, 0.62f, 0.6f));
            var white = StartFinishBuilder.Lit("Assets/Materials/TimingWhite.mat", new Color(0.92f, 0.92f, 0.92f));
            var yellow = StartFinishBuilder.Lit("Assets/Materials/TimingYellow.mat", new Color(1f, 0.8f, 0.05f));
            var steel = StartFinishBuilder.Lit("Assets/Materials/TimingSteel.mat", new Color(0.35f, 0.36f, 0.38f));
            var signBlack = StartFinishBuilder.Unlit("Assets/Materials/TimingSignBlack.mat", new Color(0.02f, 0.02f, 0.02f));
            var signWhite = StartFinishBuilder.Unlit("Assets/Materials/TimingSignWhite.mat", new Color(0.85f, 0.85f, 0.85f));

            // Samples from the entry line to the exit line, the stretch under the limit.
            var limit = new List<int>();
            for (int i = pit.PitEntryLine; ; i = (i + 1) % n)
            {
                limit.Add(i);
                if (i == pit.PitExitLine) break;
            }

            Vector3 At(int i, float fromCentre) => new Vector3(0f, rightEdge[i].y, 0f)
                + Flat(centre[i] + right[i] * fromCentre);
            Vector3 Forward(int i) => Flat(centre[(i + 1) % n] - centre[(i - 1 + n) % n]).normalized;

            // The pit wall: half way across the gap between the road and the fast lane.
            var wallLine = limit.ConvertAll(i => At(i, widthRight[i] + TrackData.PitWallGapM * 0.5f));
            var wallRight = limit.ConvertAll(i => right[i]);
            Wall(parent, wallLine, wallRight, concrete, barrierLayer, barrier);
            Fence(parent, wallLine, wallRight, StructureModels.Chainlink());

            // Lines: between the fast and working lanes, dashed; the lane's outer side, solid;
            // and across the lane at the entry and exit lines.
            var quads = new List<Vector3[]>();
            var dashes = new List<Vector3[]>();
            for (int k = 0; k + 1 < limit.Count; k++)
            {
                int i = limit[k], j = limit[k + 1];
                float divide = pit.PitOffsetM[i] + TrackData.PitFastLaneM * 0.5f;
                float divideNext = pit.PitOffsetM[j] + TrackData.PitFastLaneM * 0.5f;
                float outer = divide + TrackData.PitWorkingLaneM, outerNext = divideNext + TrackData.PitWorkingLaneM;
                if (k % 3 < 2) dashes.Add(Band(At(i, divide), At(j, divideNext), right[i], LineM));
                quads.Add(Band(At(i, outer), At(j, outerNext), right[i], LineM));
            }
            foreach (int i in new[] { pit.PitEntryLine, pit.PitExitLine })
            {
                Vector3 from = At(i, widthRight[i] + TrackData.PitWallGapM);
                Vector3 to = At(i, pit.PitOffsetM[i] + TrackData.PitFastLaneM * 0.5f + TrackData.PitWorkingLaneM);
                quads.Add(Band(from - Forward(i) * (CrossLineM * 0.5f), to - Forward(i) * (CrossLineM * 0.5f), Forward(i), CrossLineM, across: true));
            }
            StartFinishBuilder.Paint("Pit Lines", parent, white, Lifted(quads));
            StartFinishBuilder.Paint("Pit Lane Divider", parent, white, Lifted(dashes));

            // A box for each car, an outline in the working lane.
            var boxes = new List<Vector3[]>();
            foreach (int b in pit.PitBoxIndex)
            {
                Vector3 middle = At(b, pit.PitOffsetM[b] + pit.PitBoxShiftM);
                Vector3 f = Forward(b), r = Flat(right[b]).normalized;
                Vector3 corner = middle - f * (BoxLengthM * 0.5f) - r * (BoxWidthM * 0.5f);
                boxes.Add(StartFinishBuilder.Quad(corner, f * BoxLengthM, r * LineM));
                boxes.Add(StartFinishBuilder.Quad(corner + r * (BoxWidthM - LineM), f * BoxLengthM, r * LineM));
                boxes.Add(StartFinishBuilder.Quad(corner, f * LineM, r * BoxWidthM));
                boxes.Add(StartFinishBuilder.Quad(corner + f * (BoxLengthM - LineM), f * LineM, r * BoxWidthM));
            }
            StartFinishBuilder.Paint("Pit Boxes", parent, yellow, Lifted(boxes));

            // 60 boards at the entry line, beyond the lane's far side, facing the cars coming in.
            {
                int i = pit.PitEntryLine;
                Vector3 f = Forward(i), r = Flat(right[i]).normalized;
                Vector3 foot = At(i, pit.PitOffsetM[i] + TrackData.PitFastLaneM * 0.5f + TrackData.PitWorkingLaneM + 1.5f);
                Quaternion toward = Quaternion.LookRotation(-f, Vector3.up);
                StartFinishBuilder.Box("Pit Limit Post", parent, foot + Vector3.up * (BoardHeightM * 0.5f), toward,
                                       new Vector3(0.15f, BoardHeightM, 0.15f), steel, barrierLayer, barrier);
                StartFinishBuilder.Box("Pit Limit Board", parent, foot + Vector3.up * BoardHeightM - f * 0.1f, toward,
                                       new Vector3(1.4f, 1.1f, 0.08f), signWhite, -1, null);
                StartFinishBuilder.Sign(parent, "60", foot + Vector3.up * BoardHeightM - f * 0.15f,
                                        Quaternion.LookRotation(f), 0.7f, signBlack);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>A band LineM wide from a to b, across <paramref name="side"/> (or, with across,
        /// the band runs from a to b and is <paramref name="side"/> deep).</summary>
        static Vector3[] Band(Vector3 a, Vector3 b, Vector3 side, float width, bool across = false)
        {
            Vector3 s = Flat(side).normalized * width;
            return across ? StartFinishBuilder.Quad(a, b - a, s) : StartFinishBuilder.Quad(a - s * 0.5f, b - a, s);
        }

        static Vector3[][] Lifted(List<Vector3[]> quads)
        {
            var lifted = new Vector3[quads.Count][];
            for (int q = 0; q < quads.Count; q++)
            {
                lifted[q] = new Vector3[4];
                for (int c = 0; c < 4; c++) lifted[q][c] = quads[q][c] + Vector3.up * LiftM;
            }
            return lifted;
        }

        /// <summary>The pit wall: a concrete wall along <paramref name="line"/>, seen and solid, on
        /// the barrier layer the wheels ignore, its foot sunk so no gap shows where the ground
        /// rises and falls.</summary>
        static void Wall(GameObject parent, List<Vector3> line, List<Vector3> right, Material material, int layer, PhysicsMaterial surface)
        {
            int n = line.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < n; i++)
            {
                Vector3 half = Flat(right[i]).normalized * (WallThicknessM * 0.5f);
                Vector3 p = line[i];
                vertices.Add(p - half + Vector3.down * WallFootM);
                vertices.Add(p - half + Vector3.up * WallHeightM);
                vertices.Add(p + half + Vector3.up * WallHeightM);
                vertices.Add(p + half + Vector3.down * WallFootM);
            }
            for (int i = 0; i + 1 < n; i++)
            {
                int a = 4 * i, b = 4 * (i + 1);
                for (int f = 0; f < 3; f++)
                    triangles.AddRange(new[] { a + f, b + f, a + f + 1, a + f + 1, b + f, b + f + 1 });
            }
            // Both ends closed.
            foreach (int a in new[] { 0, 4 * (n - 1) }) triangles.AddRange(new[] { a, a + 1, a + 2, a, a + 2, a + 3 });
            var mesh = new Mesh { name = "Pit Wall", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Pit Wall") { layer = layer, isStatic = true };
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // Double sided by geometry would cost twice the triangles; the shader's own culling is
            // off for this one material instead, so the wall reads from the track and the lane.
            material.SetFloat("_Cull", (float)CullMode.Off);
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.sharedMaterial = surface;
        }

        /// <summary>A chain-link sheet on the wall, from its top to FenceTopM, as the catch fence
        /// stands on the start straight's barrier elsewhere.</summary>
        static void Fence(GameObject parent, List<Vector3> line, List<Vector3> right, Material chainlink)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float along = 0f;
            for (int i = 0; i < line.Count; i++)
            {
                if (i > 0) along += Vector3.Distance(line[i], line[i - 1]);
                vertices.Add(line[i] + Vector3.up * WallHeightM);
                vertices.Add(line[i] + Vector3.up * FenceTopM);
                uvs.Add(new Vector2(along / 2f, 0f));
                uvs.Add(new Vector2(along / 2f, (FenceTopM - WallHeightM) / 2f));
                if (i == 0) continue;
                int a = vertices.Count - 4;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            var mesh = new Mesh { name = "Pit Wall Fence", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Pit Wall Fence") { isStatic = true };
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = chainlink;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
