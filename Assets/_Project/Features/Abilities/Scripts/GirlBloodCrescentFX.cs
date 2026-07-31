using UnityEngine;
using System.Collections;

public class GirlBloodCrescentFX : MonoBehaviour
{
    private LineRenderer _line;
    public float lifetime = 0.5f;
    public float speed = 15f;
    public Color color = Color.red;

    void Awake()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        
        // 1. SAFE SHADER SEARCH (Prevents ArgumentNullException)
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            _line.material = new Material(shader);
            
            // Set black on whichever property the shader uses
            if (_line.material.HasProperty("_BaseColor")) _line.material.SetColor("_BaseColor", Color.black);
            else if (_line.material.HasProperty("_Color")) _line.material.SetColor("_Color", Color.black);
        }
        
        // PITCH SHADOW BLACK
        _line.startColor = Color.black;
        _line.endColor = Color.black;
        
        // 2. SHARPER CRESCENT SHAPE
        _line.alignment = LineAlignment.TransformZ;
        _line.widthCurve = new AnimationCurve(
            new Keyframe(0, 0.0f), 
            new Keyframe(0.2f, 0.3f), 
            new Keyframe(0.5f, 0.6f), 
            new Keyframe(0.8f, 0.3f), 
            new Keyframe(1, 0.0f)
        );
        _line.positionCount = 60;
        _line.useWorldSpace = true;

        DrawArc();
        StartCoroutine(AnimateFade());
    }

    // [Removed SetupBloodParticles to keep it clean and non-poly]

    void Update()
    {
        // Move forward constantly
        transform.position += transform.forward * speed * Time.deltaTime;
        DrawArc();
    }

    private void DrawArc()
    {
        float radius = 3.0f; // Bigger sweep
        float angleRange = 170f; // Nearly a full semi-circle
        
        for (int i = 0; i < 60; i++)
        {
            float angle = -angleRange / 2f + (angleRange * i / 59f);
            float rad = angle * Mathf.Deg2Rad;
            
            // Fixed height: It stays strictly horizontal/tilted centered on the hands
            // Tilted slightly downwards (y: -0.2f) for a "slash" feel
            Vector3 localPos = new Vector3(Mathf.Sin(rad) * radius, Mathf.Cos(rad) * -0.2f, Mathf.Cos(rad) * radius);
            _line.SetPosition(i, transform.TransformPoint(localPos));
        }
    }

    private IEnumerator AnimateFade()
    {
        float elapsed = 0;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / lifetime);
            
            // Fade the URP material transparency
            if (_line.material.HasProperty("_BaseColor"))
            {
                Color c = _line.material.GetColor("_BaseColor");
                c.a = alpha;
                _line.material.SetColor("_BaseColor", c);
            }
            
            yield return null;
        }
        Destroy(gameObject);
    }
}
