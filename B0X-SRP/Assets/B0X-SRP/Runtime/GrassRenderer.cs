using UnityEngine;

public class GrassRenderer : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private int instanceCount = 1000;
    [SerializeField] private float spreadRadius = 7.0f;
    [SerializeField] private Vector3 fireCenter = Vector3.zero;
    [SerializeField] private float bareRadius = 1.5f;
    [SerializeField] private float densityFadeRadius = 1.5f;
    [SerializeField, Min(0.1f)] private float bladeHeight = 1.5f;

    private Matrix4x4[] matrices;
    private MaterialPropertyBlock propertyBlock;

    void Awake()
    {
        if (grassMaterial == null)
        {
            Debug.LogError("if (grassMaterial == null) error", this);
            enabled = false;
            return;
        }

        if (!grassMaterial.enableInstancing)
        {
            Debug.LogError(
                "if (!grassMaterial.enableInstancing)",
                this
            );
            enabled = false;
            return;
        }

        if (instanceCount < 1)
        {
            Debug.LogWarning("if (instanceCount < 1) error", this);
            instanceCount = 1;
        }
        else if (instanceCount > 2047)
        {
            Debug.LogWarning("if (instanceCount > 2047) error", this);
            instanceCount = 2047;
        }

        if (grassMesh == null || grassMesh.name == "Quad")
            grassMesh = CreateCrossedBladeMesh();

        var placements = new System.Collections.Generic.List<Matrix4x4>(instanceCount);
        float outerRadius = Mathf.Max(0f, bareRadius) + Mathf.Max(0f, densityFadeRadius);
        Vector3 center = transform.position + fireCenter;
        for (int i = 0; i < instanceCount; i++)
        {
            Vector3 position;
            float density;
            int attempts = 0;
            do
            {
                position = center + new Vector3(
                    Random.Range(-spreadRadius, spreadRadius), 0f,
                    Random.Range(-spreadRadius, spreadRadius));
                float distance = Vector2.Distance(
                    new Vector2(position.x, position.z), new Vector2(center.x, center.z));
                density = distance <= bareRadius ? 0f :
                    (densityFadeRadius <= 0f ? 1f : Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(bareRadius, outerRadius, distance)));
                attempts++;
            } while (Random.value > density && attempts < 20);

            if (Random.value > density)
                continue;

            Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float variation = Random.Range(0.7f, 1.3f);
            Vector3 scale = new Vector3(variation, bladeHeight * variation, variation);

            placements.Add(Matrix4x4.TRS(position, rotation, scale));
        }
        matrices = placements.ToArray();
    }

    void Update()
    {
        if (matrices == null || grassMesh == null || grassMaterial == null)
            return;

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        Graphics.DrawMeshInstanced(
            grassMesh, 
            0, 
            grassMaterial, 
            matrices, 
            matrices.Length, 
            propertyBlock,
            UnityEngine.Rendering.ShadowCastingMode.On,
            true
        );
    }

    private static Mesh CreateCrossedBladeMesh()
    {
        var mesh = new Mesh { name = "Generated Crossed Grass Blades" };
        mesh.vertices = new[] {
            new Vector3(-0.25f, 0f, 0f), new Vector3(0.25f, 0f, 0f), new Vector3(-0.12f, 1f, 0f), new Vector3(0.12f, 1f, 0f),
            new Vector3(0f, 0f, -0.25f), new Vector3(0f, 0f, 0.25f), new Vector3(0f, 1f, -0.12f), new Vector3(0f, 1f, 0.12f)
        };
        mesh.uv = new[] {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5, 5, 6, 7 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
