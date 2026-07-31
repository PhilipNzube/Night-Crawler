using UnityEngine;
using System.Collections;

public class ShadowVoidFX : MonoBehaviour
{
    private MeshRenderer _renderer;
    private Material _mat;
    
    public float duration = 0.8f;
    public float maxRadius = 12f;

    void Awake()
    {
        // 1. CREATE A PURE VOID SPHERE
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.SetParent(transform);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.zero; // Start at zero
        
        _renderer = sphere.GetComponent<MeshRenderer>();
        
        // 2. USE THE SAME "BITCH BLACK" SHADER AS THE SWIPE
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            _mat = new Material(shader);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.black);
            else if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", Color.black);
            
            _renderer.material = _mat;
        }

        // Remove the collider since it's just a visual
        Destroy(sphere.GetComponent<SphereCollider>());

        StartCoroutine(EruptionRoutine());
    }

    private IEnumerator EruptionRoutine()
    {
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            
            // 1. Rapidly expand to consumption radius
            float currentScale = Mathf.Lerp(0, maxRadius, t * 1.5f);
            transform.localScale = new Vector3(currentScale, currentScale, currentScale);

            // 2. Pulse slightly as it expands
            float pulse = 1f + Mathf.PingPong(t * 10, 0.2f);
            transform.localScale *= pulse;

            // 3. Fade out at the end
            if (_mat != null)
            {
                Color c = _mat.HasProperty("_BaseColor") ? _mat.GetColor("_BaseColor") : _mat.GetColor("_Color");
                c.a = 1.0f - t;
                if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
                else _mat.SetColor("_Color", c);
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}
