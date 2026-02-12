using UnityEngine;

public class DroneInfo : MonoBehaviour
{
    [Header("显示名字（不填就用 GameObject.name）")]
    public string displayName;

    [Header("UI 颜色")]
    public Color uiColor = Color.cyan;

    [Header("用于 UI 锚点的 Transform（不填就用自身）")]
    public Transform uiAnchor;

    [Header("可选：导航器，用于读巡航速度")]
    public DroneGeoNavigator navigator;

    [Header("面板预制体")]
    public GameObject infoPanelPrefab; // ←←← 在 Inspector 中拖你的 DroneInfoPanel Prefab 到这里！

    private DroneInfoPanel panel;

    void Start()
    {
        if (infoPanelPrefab == null)
        {
            Debug.LogWarning($"[{gameObject.name}] 没有设置 InfoPanel Prefab！");
            return;
        }

        // 直接实例化预制体，并作为子物体挂载
        GameObject panelObj = Instantiate(infoPanelPrefab, transform);
        panel = panelObj.GetComponent<DroneInfoPanel>();

        if (panel == null)
        {
            Debug.LogError($"[{gameObject.name}] 实例化的面板上没有找到 DroneInfoPanel 脚本！");
            return;
        }

        // 自动设置跟随目标和名字
        panel.targetDrone = transform;
        panel.SetName(GetName());

        Debug.Log($"[{gameObject.name}] 成功创建 InfoPanel！");
    }

    void Update()
    {
        // 点击检测：点击无人机时切换面板显示
        if (panel != null && Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f))
            {
                // 只响应点击自己
                if (hit.transform.GetComponentInParent<DroneInfo>() == this)
                {
                    panel.TogglePanel();
                    Debug.Log($"[{gameObject.name}] 面板切换：{panel.isVisible}");
                }
            }
        }

        // 实时更新速度（可选，每帧或每秒更新一次）
        if (panel != null && navigator != null)
        {
            panel.UpdateSpeed((float)navigator.cruiseSpeed); // 假设你有实时速度，或用 GetCruiseSpeed()
        }
    }

    public string GetName()
    {
        return string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;
    }

    public Transform GetAnchor()
    {
        return uiAnchor ? uiAnchor : transform;
    }

    public double GetCruiseSpeed()
    {
        return navigator ? navigator.cruiseSpeed : 0.0;
    }
}