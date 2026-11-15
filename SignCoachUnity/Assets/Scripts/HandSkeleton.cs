using UnityEngine;

namespace SignCoach
{
    public sealed class HandSkeleton
    {
        public static readonly int[,] Edges = {
            {0,1},{1,2},{2,3},{3,4},{0,5},{5,6},{6,7},{7,8},
            {5,9},{9,10},{10,11},{11,12},{9,13},{13,14},{14,15},
            {15,16},{13,17},{0,17},{17,18},{18,19},{19,20}
        };
        readonly GameObject root;
        readonly Transform[] points = new Transform[21];
        readonly MeshRenderer[] dots = new MeshRenderer[21];
        readonly LineRenderer[] lines = new LineRenderer[21];
        readonly Vector3[] positions = new Vector3[21];
        readonly Vector3[] pair = new Vector3[2];

        public HandSkeleton(Transform parent, Material material, Mesh dot, int index)
        {
            root = new GameObject("Hand " + index); root.transform.SetParent(parent, false);
            for (int i = 0; i < 21; i++)
            {
                var point = new GameObject("Point " + i); point.transform.SetParent(root.transform, false);
                point.AddComponent<MeshFilter>().sharedMesh = dot;
                dots[i] = point.AddComponent<MeshRenderer>(); dots[i].sharedMaterial = material;
                points[i] = point.transform;
                var line = new GameObject("Bone " + i); line.transform.SetParent(root.transform, false);
                lines[i] = line.AddComponent<LineRenderer>(); lines[i].sharedMaterial = material;
                lines[i].positionCount = 2; lines[i].startWidth = lines[i].endWidth = .025f;
                lines[i].numCapVertices = 3; lines[i].useWorldSpace = true;
            }
            Hide();
        }
        public void Show(HandData hand, Material material)
        {
            root.SetActive(true);
            foreach (var p in hand.landmarks) positions[p.id] = HandProtocol.Position(p);
            for (int i = 0; i < 21; i++)
            {
                points[i].localPosition = positions[i] + new Vector3(0, 0, -.01f);
                dots[i].sharedMaterial = material;
                lines[i].sharedMaterial = material;
                pair[0] = positions[Edges[i, 0]]; pair[1] = positions[Edges[i, 1]];
                lines[i].SetPositions(pair);
            }
        }
        public void Hide() { root.SetActive(false); }
        public bool Visible { get { return root.activeSelf; } }
        public static Mesh CreateDot()
        {
            var mesh = new Mesh { name = "Reusable landmark dot" };
            var vertices = new Vector3[13]; var triangles = new int[36];
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2 / 12;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * .045f;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % 12 + 1; triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateBounds(); return mesh;
        }
    }
}
