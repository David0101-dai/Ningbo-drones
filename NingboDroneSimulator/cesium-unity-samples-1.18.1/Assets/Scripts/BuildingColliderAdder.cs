using UnityEngine;
using CesiumForUnity;

public class BuildingColliderAdder : MonoBehaviour
{
    private void Start()
    {
        var tileset = GetComponent<Cesium3DTileset>();
        if (tileset != null)
        {
            tileset.OnTileGameObjectCreated += AddColliders;
        }
    }

    private void AddColliders(GameObject tile)
    {
        //Debug.Log("添加碰撞体 Adding colliders to tile: " + tile.name);
        var meshes = tile.GetComponentsInChildren<MeshRenderer>();
        foreach (var mesh in meshes)
        {
            var collider = mesh.gameObject.AddComponent<MeshCollider>();
            collider.convex = true; // Improves performance for collisions
            mesh.gameObject.layer = LayerMask.NameToLayer("Buildings"); // Assigns to custom layer
        }
    }
}