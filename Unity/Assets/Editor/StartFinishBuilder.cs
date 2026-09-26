using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CarRace.UnityGame.EditorTools
{
    /// <summary>
    /// Where the race starts and where it is timed: a chequered start and finish line across
    /// the road at sample 0, where LapTimer counts each lap; a gantry over it reading START
    /// FINISH both ways; a painted box for each grid slot; and a yellow line with a board
    /// either side at each of LapTimer's two sector gates, a third and two thirds of the way
    /// round. The paint has no collider, as the other markings; the gantry legs and boards
    /// stand on the verge and are solid, on the barrier layer so a wheel never reads them as
    /// ground.
    /// </summary>
    static class StartFinishBuilder
    {
        const float LiftM = 0.03f;             // above the edge lines' 2 cm, so they never fight
        const float SquareM = 1f;              // the chequer's squares, about
        const float GantryClearM = 6.5f;       // road to the underside of the beam
        const float BeamDepthM = 1.6f;
        const float LegOutsideM = 3f;          // from the road's edge to the leg
        const float BoardOutsideM = 4f;        // sector boards, from the road's edge
        const float GridBoxHalfWidthM = 1.5f;
        const float GridBoxFrontM = 3f;        // ahead of the car's centre
        const float GridBoxSideM = 1.6f;       // how far the bracket's sides run back
        const float LineM = 0.2f;

        public static void Build(GameObject root, Vector3[] centre, Vector3[] leftEdge, Vector3[] rightEdge,
                                 IEnumerable<(int index, float lateral)> gridSlots, int barrierLayer, PhysicsMaterial barrier)
        {
            var parent = new GameObject("Start and Timing");
            parent.transform.SetParent(root.transform, false);
            var white = Lit("Assets/Materials/TimingWhite.mat", new Color(0.92f, 0.92f, 0.92f));
            var black = Lit("Assets/Materials/TimingBlack.mat", new Color(0.03f, 0.03f, 0.03f));
            var yellow = Lit("Assets/Materials/TimingYellow.mat", new Color(1f, 0.8f, 0.05f));
            var steel = Lit("Assets/Materials/TimingSteel.mat", new Color(0.35f, 0.36f, 0.38f));
            // The signs ignore light: a street lamp over the gantry burned lit white out to a glare.
            var signWhite = Unlit("Assets/Materials/TimingSignWhite.mat", new Color(0.85f, 0.85f, 0.85f));
            var signBlack = Unlit("Assets/Materials/TimingSignBlack.mat", new Color(0.02f, 0.02f, 0.02f));
            var signYellow = Unlit("Assets/Materials/TimingSignYellow.mat", new Color(0.9f, 0.7f, 0.05f));
            int n = centre.Length;

            // The line: two rows of squares across the road, centred on sample 0.
            Vector3 across = rightEdge[0] - leftEdge[0];
            Vector3 along = (centre[1] - centre[n - 1]).normalized;
            int columns = Mathf.Max(2, Mathf.RoundToInt(across.magnitude / SquareM));
            float square = across.magnitude / columns;
            Vector3 lift = Vector3.up * LiftM;
            Chequer("Start Finish Line", parent, leftEdge[0] - along * square + lift, across, along * (2f * square),
                    columns, 2, white, black, doubleSided: false);

            // The gantry: a leg beyond each edge, a beam across, the chequer under it.
            Vector3 flatAlong = Flat(along);
            Vector3 side = Flat(across).normalized;
            Vector3 legLeft = leftEdge[0] - side * LegOutsideM, legRight = rightEdge[0] + side * LegOutsideM;
            float top = Mathf.Max(leftEdge[0].y, rightEdge[0].y) + GantryClearM;
            Quaternion facing = Quaternion.LookRotation(flatAlong, Vector3.up);
            foreach (Vector3 leg in new[] { legLeft, legRight })
                Box("Gantry Leg", parent, new Vector3(leg.x, (leg.y + top + BeamDepthM) * 0.5f, leg.z), facing,
                    new Vector3(0.6f, top + BeamDepthM - leg.y + 0.5f, 0.6f), steel, barrierLayer, barrier);
            Vector3 beamMiddle = (legLeft + legRight) * 0.5f;
            float span = Vector3.Distance(Flat(legLeft), Flat(legRight));
            beamMiddle.y = top + BeamDepthM * 0.5f;
            Box("Gantry Beam", parent, beamMiddle, facing, new Vector3(span, BeamDepthM, 0.5f), steel, -1, null);
            // Each face of the beam: a chequered band along its foot and the words above it.
            Vector3 bandFrom = new Vector3(legLeft.x, top, legLeft.z) + side * 0.3f;
            int bandColumns = Mathf.RoundToInt((span - 0.6f) / 0.4f);
            foreach (float way in new[] { -1f, 1f })
            {
                Vector3 face = -flatAlong * (0.26f * way);
                Chequer("Gantry Chequer", parent, bandFrom + face, side * (span - 0.6f), Vector3.up * 0.4f,
                        bandColumns, 1, signWhite, signBlack, doubleSided: true);
                Sign(parent, "START   FINISH", new Vector3(beamMiddle.x, top + 1f, beamMiddle.z) + face,
                     Quaternion.LookRotation(flatAlong * way), 0.8f, signWhite);
            }

            // A box on the road for each grid slot, open at the back.
            foreach (var (index, lateral) in gridSlots)
            {
                Vector3 forward = (centre[(index + 1) % n] - centre[(index - 1 + n) % n]).normalized;
                Vector3 r = (rightEdge[index] - leftEdge[index]).normalized;
                Vector3 middle = centre[index] + r * lateral + lift;
                Vector3 frontLeft = middle + forward * GridBoxFrontM - r * GridBoxHalfWidthM;
                Paint("Grid Box", parent, white, new[]
                {
                    Quad(frontLeft, r * (2f * GridBoxHalfWidthM), forward * LineM),
                    Quad(frontLeft - forward * GridBoxSideM, r * LineM, forward * GridBoxSideM),
                    Quad(frontLeft + r * (2f * GridBoxHalfWidthM - LineM) - forward * GridBoxSideM, r * LineM, forward * GridBoxSideM),
                });
            }

            // The two sector gates: a yellow line across and a board either side.
            for (int gate = 1; gate <= 2; gate++)
            {
                int i = gate * n / 3;
                Vector3 forward = (centre[(i + 1) % n] - centre[i - 1]).normalized;
                Paint($"Sector {gate} Line", parent, yellow, new[]
                {
                    Quad(leftEdge[i] - forward * (LineM * 1.5f) + lift, rightEdge[i] - leftEdge[i], forward * (LineM * 3f)),
                });
                Vector3 sideways = Flat(rightEdge[i] - leftEdge[i]).normalized;
                Quaternion toward = Quaternion.LookRotation(-Flat(forward), Vector3.up);
                foreach (Vector3 foot in new[] { leftEdge[i] - sideways * BoardOutsideM, rightEdge[i] + sideways * BoardOutsideM })
                {
                    Box("Sector Post", parent, foot + Vector3.up * 1.5f, toward, new Vector3(0.2f, 3.5f, 0.2f), steel, barrierLayer, barrier);
                    Box("Sector Board", parent, foot + Vector3.up * 3.4f - Flat(forward) * 0.15f, toward, new Vector3(3.8f, 1.2f, 0.1f), signYellow, -1, null);
                    Sign(parent, $"S{gate} | S{gate + 1}", foot + Vector3.up * 3.4f - Flat(forward) * 0.21f,
                         Quaternion.LookRotation(Flat(forward)), 0.55f, signBlack);
                }
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        static Vector3[] Quad(Vector3 corner, Vector3 u, Vector3 v) => new[] { corner, corner + u, corner + v, corner + u + v };

        /// <summary>Flat paint from quads given as four corners, facing up.</summary>
        static void Paint(string name, GameObject parent, Material material, Vector3[][] quads)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (Vector3[] q in quads)
            {
                int a = vertices.Count;
                vertices.AddRange(q);
                bool up = Vector3.Cross(q[2] - q[0], q[1] - q[0]).y > 0f;
                triangles.AddRange(up ? new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 } : new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            Renderer(name, parent, mesh, material);
        }

        /// <summary>A board of columns by rows squares spanning u by v from corner, black and
        /// white alternating, the first square white. Facing up, or both ways for a banner.</summary>
        static void Chequer(string name, GameObject parent, Vector3 corner, Vector3 u, Vector3 v, int columns, int rows,
                            Material white, Material black, bool doubleSided)
        {
            var vertices = new List<Vector3>();
            var whites = new List<int>();
            var blacks = new List<int>();
            bool up = Vector3.Cross(v, u).y > 0f;
            for (int c = 0; c < columns; c++)
                for (int r = 0; r < rows; r++)
                {
                    int a = vertices.Count;
                    Vector3 p = corner + u * ((float)c / columns) + v * ((float)r / rows);
                    Vector3 du = u / columns, dv = v / rows;
                    vertices.AddRange(new[] { p, p + du, p + dv, p + du + dv });
                    var list = (c + r) % 2 == 0 ? whites : blacks;
                    if (doubleSided || up) list.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                    if (doubleSided || !up) list.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                }
            var mesh = new Mesh { name = name, subMeshCount = 2 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(whites, 0);
            mesh.SetTriangles(blacks, 1);
            mesh.RecalculateNormals();
            Renderer(name, parent, mesh, white).sharedMaterials = new[] { white, black };
        }

        static MeshRenderer Renderer(string name, GameObject parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>A box, solid on the given layer when layer is not -1.</summary>
        static void Box(string name, GameObject parent, Vector3 position, Quaternion rotation, Vector3 size,
                        Material material, int layer, PhysicsMaterial surface)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent.transform, false);
            box.transform.SetPositionAndRotation(position, rotation);
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (layer < 0) Object.DestroyImmediate(box.GetComponent<Collider>());
            else
            {
                box.layer = layer;
                box.GetComponent<Collider>().sharedMaterial = surface;
            }
        }

        /// <summary>Five by seven pixel letters for the signs, a row a string, top first. The
        /// blocky letters are built as geometry: TextMesh's font shader glares under URP.</summary>
        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['|'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
        };

        /// <summary>Text heightM tall centred on position, facing the one looking along
        /// rotation's forward: a flat square for each lit pixel.</summary>
        static void Sign(GameObject parent, string text, Vector3 position, Quaternion rotation, float heightM, Material material)
        {
            float pixel = heightM / 7f;
            float width = (text.Length * 6 - 1) * pixel;
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up;
            Vector3 topLeft = position - right * (width * 0.5f) + up * (heightM * 0.5f);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int c = 0; c < text.Length; c++)
            {
                string[] rows = Glyphs[text[c]];
                for (int y = 0; y < 7; y++)
                    for (int x = 0; x < 5; x++)
                    {
                        if (rows[y][x] != '#') continue;
                        int a = vertices.Count;
                        Vector3 p = topLeft + right * ((c * 6 + x) * pixel) - up * (y * pixel);
                        vertices.AddRange(new[] { p, p + right * pixel, p - up * pixel, p + right * pixel - up * pixel });
                        triangles.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
                    }
            }
            var mesh = new Mesh { name = $"Sign {text}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            Renderer(mesh.name, parent, mesh, material);
        }

        static Material Unlit(string path, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material Lit(string path, Color colour) =>
            SkidpadSceneBuilder.EnsureMaterial(path, colour, null, Vector2.one);
    }
}
