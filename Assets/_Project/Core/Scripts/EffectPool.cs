using UnityEngine;
using System.Collections.Generic;

public class EffectPool : MonoBehaviour
{
    public static EffectPool Instance { get; private set; }

    [System.Serializable]
    public class PoolItem
    {
        public string key;
        public GameObject prefab;
        public int initialSize = 10;
    }

    public List<PoolItem> poolConfig;
    private Dictionary<string, Queue<GameObject>> _pools = new Dictionary<string, Queue<GameObject>>();

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;

        InitializePools();
    }

    private void InitializePools()
    {
        foreach (var item in poolConfig)
        {
            Queue<GameObject> objectPool = new Queue<GameObject>();
            for (int i = 0; i < item.initialSize; i++)
            {
                GameObject obj = Instantiate(item.prefab, transform);
                obj.SetActive(false);
                objectPool.Enqueue(obj);
            }
            _pools.Add(item.key, objectPool);
        }
    }

    public GameObject Get(string key, Vector3 position, Quaternion rotation)
    {
        if (!_pools.ContainsKey(key))
        {
            Debug.LogWarning($"[EffectPool] Pool for key '{key}' not found!");
            return null;
        }

        Queue<GameObject> pool = _pools[key];
        GameObject obj;

        if (pool.Count > 0)
        {
            obj = pool.Dequeue();
        }
        else
        {
            // Expand pool if empty
            obj = Instantiate(FindConfig(key).prefab, transform);
        }

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.SetActive(true);
        
        // Auto-return to pool after delay (Assumes visual effects handle their own disable or we use a separate system)
        return obj;
    }

    public void ReturnToPool(string key, GameObject obj)
    {
        if (!_pools.ContainsKey(key)) return;
        
        obj.SetActive(false);
        obj.transform.SetParent(transform);
        _pools[key].Enqueue(obj);
    }

    private PoolItem FindConfig(string key)
    {
        return poolConfig.Find(x => x.key == key);
    }
}
