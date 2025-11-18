using UnityEngine;
using CesiumForUnity;

public class BuildingColliderAdder : MonoBehaviour
{
    [Tooltip("只为指定范围内的瓦片添加碰撞")]
    public float collisionRadius = 200f;
    
    [Tooltip("忽略过大的建筑（避免性能问题）")]
    public float maxBuildingSize = 100f;
    
    private Transform playerTransform;
    
    private void Start()
    {
        var tileset = GetComponent<Cesium3DTileset>();
        if (tileset != null)
        {
            tileset.OnTileGameObjectCreated += AddColliders;
        }
        
        // 假设玩家是无人机
        playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    private void AddColliders(GameObject tile)
    {
        // 只为玩家附近的建筑添加碰撞
        if (playerTransform != null)
        {
            float distance = Vector3.Distance(tile.transform.position, playerTransform.position);
            if (distance > collisionRadius)
                return;
        }
        
        var meshes = tile.GetComponentsInChildren<MeshRenderer>();
        foreach (var mesh in meshes)
        {
            Bounds bounds = mesh.bounds;
            float size = bounds.size.magnitude;
            
            // 忽略过大的建筑
            if (size > maxBuildingSize)
            {
                Debug.LogWarning($"[碰撞] 忽略过大建筑: {mesh.name} ({size:F0}m)");
                continue;
            }
            
            // 使用非凸MeshCollider（更精确）
            var collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.convex = false; // 更精确但性能稍差
            mesh.gameObject.layer = LayerMask.NameToLayer("Buildings");
        }
    }
}