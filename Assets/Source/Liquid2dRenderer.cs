using System;
using UnityEngine;
using UnityEngine.Rendering;

public class Liquid2dRenderer : MonoBehaviour
{
    private static readonly int _color = Shader.PropertyToID("_Color");
    private static readonly int _mainTex = Shader.PropertyToID("_MainTex");

    [SerializeField]
    private Sprite sprite;
    
    [SerializeField, ColorUsage(true, true)]
    private Color color = new Color(0f, 1f, 4f, 1f);
    
    [SerializeField]
    private Material material;
    
    private static Mesh _quadMesh;
    private MaterialPropertyBlock _mpb;
    private void Awake()
    {
        if (!IsValid()) return;
        
        if (_quadMesh == null)
            _quadMesh = GenerateQuadMesh();

        if (_mpb == null)
        {
            _mpb = new MaterialPropertyBlock();
            _mpb.SetColor(_color, color);
            _mpb.SetTexture(_mainTex, sprite.texture);
        }
    }

    private void OnEnable()
    {
        if (!IsValid()) return;
        
        Liquid2dFeature.RegisterLiquidParticle(this);
    }
    
    private void OnDisable()
    {
        if (!IsValid()) return;
        
        Liquid2dFeature.UnregisterLiquidParticle(this);
    }
    
    public void Render(CommandBuffer cmd)
    {
        if (!IsValid()) return;
        
        Matrix4x4 matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.localScale);
        cmd.DrawMesh(_quadMesh, matrix, material, 0, 0, _mpb);
    }

    Mesh GenerateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.vertices = new Vector3[] {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(0.5f, -0.5f, 0),
            new Vector3(0.5f, 0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0)
        };
        mesh.uv = new Vector2[] {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1)
        };
        mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
        return mesh;
    }
    
    private bool IsValid()
    {
        if (material == null || sprite == null)
            return false;

        return true;
    }
}