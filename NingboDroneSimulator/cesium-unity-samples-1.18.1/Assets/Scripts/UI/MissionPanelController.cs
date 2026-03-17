// Assets/Scripts/UI/MissionPanelController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;

public class MissionPanelController : MonoBehaviour
{
    [Header("Tab Buttons")]
    public Button fleetTabButton;
    public Button pointsTabButton;
    public Button ordersTabButton;
    public Button routingTabButton;

    [Header("Tab Panels")]
    public GameObject fleetTab;
    public GameObject pointsTab;
    public GameObject ordersTab;
    public GameObject routingTab;

    [Header("=== Fleet Tab ===")]
    public TMP_InputField droneNameInput;
    public TMP_Dropdown spawnPointDropdown;
    public Button spawnDroneButton;
    public TMP_Dropdown droneListDropdown;
    public Button removeDroneButton;

    [Header("=== Points Tab ===")]
    public TMP_InputField pointNameInput;
    public TMP_Dropdown pointTypeDropdown;
    public Button placePointButton;

    [Header("=== Orders Tab ===")]
    public TMP_Dropdown pickupDropdown;
    public TMP_Dropdown deliveryDropdown;
    public Button createOrderButton;
    public Button importOrdersButton;
    public Button clearOrdersButton;

    [Header("=== Solomon Import (Orders Tab) ===")]
    public TMP_Dropdown solomonFileDropdown;
    public TMP_InputField solomonPathInput;

    [Header("=== Routing Tab ===")]
    public TMP_Dropdown strategyDropdown;
    public Button solveRoutesButton;
    public Button dispatchAllButton;
    public Button stopAllButton;

    [Header("=== Navigation ===")]
    public Button exitMissionButton;

    [Header("Color Palette")]
    public Color[] droneColors = new Color[]
    {
        Color.cyan, Color.green, Color.yellow,
        new Color(1f, 0.5f, 0f), Color.magenta, Color.red
    };
    private int _colorIndex = 0;

    // References
    private DroneFactory _factory;
    private LocationManager _locationManager;
    private OrderManager _orderManager;
    private MapPickController _picker;
    private UIPanelManager _uiManager;
    private SolomonImporter _solomonImporter;
    private VehicleRouter _router;
    private RouteDispatcher _dispatcher;

    // State
    private bool _waitingForPointPick = false;
    private string _pendingPointName = "";
    private LocationPoint.PointType _pendingPointType;
    private List<string> _solomonFilePaths = new List<string>();

    void Awake()
    {
        _factory = FindObjectOfType<DroneFactory>();
        _locationManager = FindObjectOfType<LocationManager>();
        _orderManager = FindObjectOfType<OrderManager>();
        _picker = FindObjectOfType<MapPickController>();
        _uiManager = GetComponentInParent<UIPanelManager>();
        _solomonImporter = FindObjectOfType<SolomonImporter>();
        _router = FindObjectOfType<VehicleRouter>();
        _dispatcher = FindObjectOfType<RouteDispatcher>();
    }

    void OnEnable()
    {
        BindEvents();
        ShowTab(fleetTab);
        RefreshAll();

        if (_solomonImporter != null)
        {
            _solomonImporter.OnImportStatus -= OnSolomonStatus;
            _solomonImporter.OnImportStatus += OnSolomonStatus;
            _solomonImporter.OnDatasetImported -= OnSolomonImported;
            _solomonImporter.OnDatasetImported += OnSolomonImported;
        }
    }

    void OnDisable()
    {
        if (_solomonImporter != null)
        {
            _solomonImporter.OnImportStatus -= OnSolomonStatus;
            _solomonImporter.OnDatasetImported -= OnSolomonImported;
        }
    }

    void Update()
    {
        if (_waitingForPointPick && _picker != null && _picker.HasEnd)
        {
            var llh = _picker.EndLLH;
            _waitingForPointPick = false;

            if (_locationManager != null)
            {
                _locationManager.CreatePointFromMapPick(_pendingPointName, _pendingPointType, llh);
                _picker.Clear();
                RefreshPointsTab();
                SetOutput($"[OK] Point '{_pendingPointName}' placed successfully");
            }
        }
    }

    // ================================================================
    //  Tab Switching
    // ================================================================

    private void ShowTab(GameObject tab)
    {
        if (fleetTab) fleetTab.SetActive(tab == fleetTab);
        if (pointsTab) pointsTab.SetActive(tab == pointsTab);
        if (ordersTab) ordersTab.SetActive(tab == ordersTab);
        if (routingTab) routingTab.SetActive(tab == routingTab);

        if (tab == fleetTab) ShowFleetStatus();
        else if (tab == pointsTab) ShowPointsStatus();
        else if (tab == ordersTab) ShowOrdersStatus();
        else if (tab == routingTab) ShowRoutingStatus();
    }

    // ================================================================
    //  Event Binding
    // ================================================================

    private void BindEvents()
    {
        BindButton(fleetTabButton, () => { ShowTab(fleetTab); RefreshFleetTab(); });
        BindButton(pointsTabButton, () => { ShowTab(pointsTab); RefreshPointsTab(); });
        BindButton(ordersTabButton, () => { ShowTab(ordersTab); RefreshOrdersTab(); });
        BindButton(routingTabButton, () => { ShowTab(routingTab); RefreshRoutingTab(); });

        BindButton(exitMissionButton, () =>
        {
            if (_uiManager) _uiManager.ExitMission();
        });

        // Fleet
        BindButton(spawnDroneButton, OnSpawnDrone);
        BindButton(removeDroneButton, OnRemoveDrone);

        // Points
        BindButton(placePointButton, OnPlacePoint);

        // Orders
        BindButton(createOrderButton, OnCreateOrder);
        BindButton(importOrdersButton, OnImportOrders);
        BindButton(clearOrdersButton, OnClearOrders);

        // Routing
        BindButton(solveRoutesButton, OnSolveRoutes);
        BindButton(dispatchAllButton, OnDispatchAll);
        BindButton(stopAllButton, OnStopAll);

        if (strategyDropdown != null)
        {
            strategyDropdown.onValueChanged.RemoveAllListeners();
            strategyDropdown.onValueChanged.AddListener(OnStrategyChanged);
        }
    }

    // ================================================================
    //  Fleet Tab
    // ================================================================

    private void RefreshFleetTab()
    {
        RefreshSpawnPointDropdown();
        RefreshDroneListDropdown();
        ShowFleetStatus();
    }

    private void RefreshSpawnPointDropdown()
    {
        if (!spawnPointDropdown || _locationManager == null) return;
        spawnPointDropdown.ClearOptions();
        var names = _locationManager.GetPointNames(LocationPoint.PointType.SpawnPoint);
        if (names.Count == 0) names.Add("(no spawn points)");
        spawnPointDropdown.AddOptions(names);
    }

    private void RefreshDroneListDropdown()
    {
        if (!droneListDropdown || _factory == null) return;
        droneListDropdown.ClearOptions();
        var names = _factory.GetDroneNames();
        if (names.Count == 0) names.Add("(no drones)");
        droneListDropdown.AddOptions(names);
    }

    private void ShowFleetStatus()
    {
        var cc = FindObjectOfType<DroneCommandCenter>();
        if (cc != null) SetOutput(cc.GetFleetStatusText());
    }

    private void OnSpawnDrone()
    {
        if (_factory == null) { SetOutput("[FAIL] DroneFactory not found"); return; }

        string name = droneNameInput != null ? droneNameInput.text.Trim() : "";
        string spawnName = "";
        if (spawnPointDropdown != null && spawnPointDropdown.options.Count > 0)
            spawnName = spawnPointDropdown.options[spawnPointDropdown.value].text;

        if (spawnName.StartsWith("(")) { SetOutput("[FAIL] No spawn points available"); return; }

        Color color = droneColors[_colorIndex % droneColors.Length];
        _colorIndex++;

        var drone = _factory.SpawnDrone(name, spawnName, color);
        if (drone != null)
        {
            SetOutput($"[OK] Drone '{drone.name}' deployed at {spawnName}\\n\\nIt will receive orders automatically when idle.");
            if (droneNameInput) droneNameInput.text = "";
            RefreshDroneListDropdown();
        }
        else
        {
            SetOutput("[FAIL] Failed to deploy drone. Check console for details.");
        }
    }

    private void OnRemoveDrone()
    {
        if (_factory == null || droneListDropdown == null) return;
        if (droneListDropdown.options.Count == 0) return;

        string droneName = droneListDropdown.options[droneListDropdown.value].text;
        if (droneName.StartsWith("(")) return;

        bool ok = _factory.RemoveDrone(droneName);
        SetOutput(ok
            ? $"[OK] Drone '{droneName}' removed from fleet"
            : $"[FAIL] Could not remove '{droneName}'");
        RefreshDroneListDropdown();
    }

    // ================================================================
    //  Points Tab
    // ================================================================

    private void RefreshPointsTab()
    {
        RefreshPointTypeDropdown();
        ShowPointsStatus();
    }

    private void RefreshPointTypeDropdown()
    {
        if (!pointTypeDropdown) return;
        pointTypeDropdown.ClearOptions();
        pointTypeDropdown.AddOptions(new List<string> { "Spawn Point", "Pickup Point", "Delivery Point" });
    }

    private void ShowPointsStatus()
    {
        if (_locationManager != null)
            SetOutput(_locationManager.GetStatusText());
    }

    private void OnPlacePoint()
    {
        if (_picker == null) { SetOutput("[FAIL] MapPickController not found"); return; }
        if (_locationManager == null) { SetOutput("[FAIL] LocationManager not found"); return; }

        string name = pointNameInput != null ? pointNameInput.text.Trim() : "";
        if (string.IsNullOrEmpty(name))
        {
            SetOutput("[FAIL] Enter a name for the point first");
            return;
        }

        int typeIndex = pointTypeDropdown != null ? pointTypeDropdown.value : 0;
        _pendingPointType = typeIndex switch
        {
            0 => LocationPoint.PointType.SpawnPoint,
            1 => LocationPoint.PointType.PickupPoint,
            2 => LocationPoint.PointType.DeliveryPoint,
            _ => LocationPoint.PointType.PickupPoint
        };
        _pendingPointName = name;

        _picker.SetPickEnd();
        _waitingForPointPick = true;

        SetOutput($"Click on the map to place '{name}' ({_pendingPointType})");
    }

    // ================================================================
    //  Orders Tab
    // ================================================================

    private void RefreshOrdersTab()
    {
        RefreshPickupDropdown();
        RefreshDeliveryDropdown();
        RefreshSolomonFileDropdown();
        ShowOrdersStatus();
    }

    private void RefreshPickupDropdown()
    {
        if (!pickupDropdown || _locationManager == null) return;
        pickupDropdown.ClearOptions();
        var names = _locationManager.GetPointNames(LocationPoint.PointType.PickupPoint);
        if (names.Count == 0) names.Add("(no pickup points)");
        pickupDropdown.AddOptions(names);
    }

    private void RefreshDeliveryDropdown()
    {
        if (!deliveryDropdown || _locationManager == null) return;
        deliveryDropdown.ClearOptions();
        var names = _locationManager.GetPointNames(LocationPoint.PointType.DeliveryPoint);
        if (names.Count == 0) names.Add("(no delivery points)");
        deliveryDropdown.AddOptions(names);
    }

    private void RefreshSolomonFileDropdown()
    {
        if (solomonFileDropdown == null) return;

        solomonFileDropdown.ClearOptions();
        _solomonFilePaths.Clear();

        string solomonDir = Path.Combine(Application.streamingAssetsPath, "Solomon");
        string persistentDir = Path.Combine(Application.persistentDataPath, "Solomon");

        var allFiles = new List<string>();

        if (Directory.Exists(solomonDir))
        {
            allFiles.AddRange(Directory.GetFiles(solomonDir, "*.txt"));
            allFiles.AddRange(Directory.GetFiles(solomonDir, "*.json"));
        }

        if (Directory.Exists(persistentDir))
        {
            allFiles.AddRange(Directory.GetFiles(persistentDir, "*.txt"));
            allFiles.AddRange(Directory.GetFiles(persistentDir, "*.json"));
        }

        allFiles = allFiles.OrderBy(f => Path.GetFileName(f)).ToList();

        if (allFiles.Count == 0)
        {
            solomonFileDropdown.AddOptions(new List<string> { "(no Solomon files found)" });
            _solomonFilePaths.Clear();
        }
        else
        {
            var displayNames = allFiles.Select(f => Path.GetFileName(f)).ToList();
            solomonFileDropdown.AddOptions(displayNames);
            _solomonFilePaths = allFiles;
        }
    }

    private void ShowOrdersStatus()
    {
        if (_orderManager != null)
        {
            string status = _orderManager.GetStatusText();

            if (_solomonImporter != null && _solomonImporter.CurrentDataset != null)
                status += "\\n--- Solomon Dataset ---\\n" + _solomonImporter.GetImportSummary();

            SetOutput(status);
        }
    }

    private void OnCreateOrder()
    {
        if (_orderManager == null) { SetOutput("[FAIL] OrderManager not found"); return; }

        string pickup = pickupDropdown != null && pickupDropdown.options.Count > 0
            ? pickupDropdown.options[pickupDropdown.value].text : "";
        string delivery = deliveryDropdown != null && deliveryDropdown.options.Count > 0
            ? deliveryDropdown.options[deliveryDropdown.value].text : "";

        if (pickup.StartsWith("(") || delivery.StartsWith("("))
        {
            SetOutput("[FAIL] Select valid pickup and delivery points first");
            return;
        }

        var order = _orderManager.CreateOrderByNames(pickup, delivery);
        if (order != null)
            SetOutput($"[OK] Order {order.orderId}: {pickup} → {delivery}\\n\\n{_orderManager.GetStatusText()}");
        else
            SetOutput("[FAIL] Failed to create order. Check point names.");
    }

    // ================================================================
    //  Solomon Import
    // ================================================================

    private void OnImportOrders()
    {
        // Priority 1: Manual path input
        if (solomonPathInput != null && !string.IsNullOrEmpty(solomonPathInput.text.Trim()))
        {
            string manualPath = solomonPathInput.text.Trim();

            if (solomonFileDropdown != null &&
                _solomonFilePaths.Count > 0 &&
                !solomonFileDropdown.options[solomonFileDropdown.value].text.StartsWith("("))
            {
                SetOutput($"[NOTE] Using manual path (clear the path input field to use dropdown instead):\\n{manualPath}");
            }

            TryImportSolomon(manualPath);
            return;
        }

        // Priority 2: Solomon file dropdown
        if (_solomonImporter != null &&
            solomonFileDropdown != null &&
            _solomonFilePaths.Count > 0 &&
            solomonFileDropdown.value >= 0 &&
            solomonFileDropdown.value < _solomonFilePaths.Count)
        {
            string selectedText = solomonFileDropdown.options[solomonFileDropdown.value].text;
            if (selectedText.StartsWith("("))
            {
                SetOutput(GetSolomonHelpText());
                return;
            }

            string selectedPath = _solomonFilePaths[solomonFileDropdown.value];
            TryImportSolomon(selectedPath);
            return;
        }

        // Priority 3: Legacy JSON import
        if (_orderManager != null)
        {
            string samplePath = Path.Combine(Application.persistentDataPath, "sample_orders.json");
            string ordersPath = Path.Combine(Application.persistentDataPath, "orders.json");

            string filePath = File.Exists(samplePath) ? samplePath :
                              File.Exists(ordersPath) ? ordersPath : "";

            if (!string.IsNullOrEmpty(filePath))
            {
                int count = _orderManager.ImportOrdersFromFile(filePath);
                SetOutput($"[OK] Legacy import: {count} orders\\n\\n{_orderManager.GetStatusText()}");
                return;
            }
        }

        SetOutput(GetSolomonHelpText());
    }

    private void TryImportSolomon(string filePath)
    {
        if (_solomonImporter == null)
        {
            SetOutput("[FAIL] SolomonImporter not found in scene.");
            return;
        }

        if (!File.Exists(filePath))
        {
            SetOutput($"[FAIL] File not found:\\n{filePath}");
            return;
        }

        SetOutput($"Importing Solomon dataset...\\n{Path.GetFileName(filePath)}\\nPlease wait...");

        bool success = _solomonImporter.ImportFromFile(filePath);

        if (success)
        {
            RefreshAllAfterImport();
            string summary = _solomonImporter.GetImportSummary();
            SetOutput($"[OK] Solomon import successful!\\n\\n{summary}");
        }
        else
        {
            SetOutput($"[FAIL] Solomon import failed for:\\n{filePath}\\nCheck console for details.");
        }
    }

    private void RefreshAllAfterImport()
    {
        if (_locationManager != null)
            _locationManager.RefreshPoints();

        RefreshFleetTab();
        RefreshPointsTab();
        RefreshOrdersTab();
        MoveCameraToImportedArea();
    }

    private void MoveCameraToImportedArea()
    {
        if (_solomonImporter == null || _solomonImporter.CurrentDataset == null) return;
        var dataset = _solomonImporter.CurrentDataset;
        if (dataset.depot == null) return;

        Debug.Log($"[MissionPanel] Import area center: " +
                  $"lat={_solomonImporter.centerLatitude:F4}, " +
                  $"lon={_solomonImporter.centerLongitude:F4}");
    }

    private string GetSolomonHelpText()
    {
        string streamingPath = Path.Combine(Application.streamingAssetsPath, "Solomon");
        string persistentPath = Path.Combine(Application.persistentDataPath, "Solomon");

        return $"[INFO] No Solomon dataset files found.\\n\\n" +
               $"Place .txt or .json files in:\\n" +
               $"  1. {streamingPath}\\n" +
               $"  2. {persistentPath}\\n\\n" +
               $"Supported: Raw .txt or converted .json";
    }

    private void OnSolomonStatus(string msg) => SetOutput(msg);

    private void OnSolomonImported(SolomonDataset dataset)
    {
        Debug.Log($"[MissionPanel] Solomon imported: {dataset.name}, {dataset.CustomerCount} customers");
        RefreshAllAfterImport();
    }

    // ================================================================
    //  Orders: Clear
    // ================================================================

    private void OnClearOrders()
    {
        if (_orderManager == null) return;
        _orderManager.ClearAllOrders();
        SetOutput("[OK] All orders cleared");
    }

    // ================================================================
    //  Routing Tab
    // ================================================================

    private void RefreshRoutingTab()
    {
        RefreshStrategyDropdown();
        ShowRoutingStatus();
    }

    private void RefreshStrategyDropdown()
    {
        if (strategyDropdown == null) return;
        strategyDropdown.ClearOptions();
        strategyDropdown.AddOptions(new List<string>
        {
            "Balanced",
            "Efficiency (Fast)",
            "Economy (Low Cost)"
        });

        if (_router != null)
        {
            strategyDropdown.value = (int)_router.strategy;
            strategyDropdown.RefreshShownValue();
        }
    }

    private void ShowRoutingStatus()
    {
        var sb = new System.Text.StringBuilder();

        // Orders summary
        if (_orderManager != null)
        {
            sb.AppendLine($"Orders: {_orderManager.PendingCount} pending, " +
                          $"{_orderManager.ActiveCount} active, " +
                          $"{_orderManager.CompletedCount} completed " +
                          $"(total: {_orderManager.TotalCount})");
        }

        // Fleet summary
        var cc = FindObjectOfType<DroneCommandCenter>();
        if (cc != null)
        {
            var fleet = cc.GetFleetSnapshot();
            int idle = fleet.Count(f => f.isIdle);
            int flying = fleet.Count(f => !f.isIdle && !f.isPaused);
            sb.AppendLine($"Fleet: {fleet.Count} drones ({idle} idle, {flying} flying)");
        }

        sb.AppendLine();

        // Routing solution
        if (_router != null && _router.LastSolution != null && _router.LastSolution.Count > 0)
        {
            sb.AppendLine(_router.GetSolutionSummary());
        }
        else
        {
            sb.AppendLine("No routes planned yet.");
            sb.AppendLine();
            sb.AppendLine("Workflow:");
            sb.AppendLine("  1. Import Solomon data (Orders tab)");
            sb.AppendLine("  2. Choose strategy above");
            sb.AppendLine("  3. Click 'Solve Routes'");
            sb.AppendLine("  4. Click 'Dispatch All'");
        }

        // Dispatch status
        if (_dispatcher != null && _dispatcher.ActiveDispatchCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine(_dispatcher.GetDispatchStatus());
        }

        SetOutput(sb.ToString());
    }

    private void OnStrategyChanged(int index)
    {
        if (_router == null) return;

        _router.strategy = index switch
        {
            0 => VehicleRouter.RoutingStrategy.Balanced,
            1 => VehicleRouter.RoutingStrategy.Efficiency,
            2 => VehicleRouter.RoutingStrategy.Economy,
            _ => VehicleRouter.RoutingStrategy.Balanced
        };

        SetOutput($"[OK] Strategy set to: {_router.strategy}\\n\\n" +
                  $"Click 'Solve Routes' to re-plan with new strategy.");
    }

    private void OnSolveRoutes()
    {
        if (_router == null)
        {
            SetOutput("[FAIL] VehicleRouter not found.\\nAdd VehicleRouter to a GameObject under LLMRoot.");
            return;
        }

        if (_orderManager == null || _orderManager.TotalCount == 0)
        {
            SetOutput("[FAIL] No orders to route.\\nImport a Solomon dataset first (Orders tab).");
            return;
        }

        var cc = FindObjectOfType<DroneCommandCenter>();
        if (cc == null)
        {
            SetOutput("[FAIL] DroneCommandCenter not found");
            return;
        }

        var fleet = cc.GetFleetSnapshot();
        if (fleet.Count == 0)
        {
            SetOutput("[FAIL] No drones available.\\nSpawn drones first (Fleet tab).");
            return;
        }

        // Find depot LLH
        double3 depotLLH = new double3(121.55, 29.87, 25.0);
        if (_solomonImporter != null && _solomonImporter.CurrentDataset?.depot != null)
            depotLLH = _solomonImporter.CurrentDataset.depot.GetLLH();
        else if (_locationManager != null)
        {
            var spawns = _locationManager.GetSpawnPoints();
            if (spawns.Count > 0) depotLLH = spawns[0].GetLLH();
        }

        // Get vehicle capacity
        int capacity = 200;
        if (_solomonImporter?.CurrentDataset != null)
            capacity = _solomonImporter.CurrentDataset.vehicleCapacity;

        SetOutput($"Solving routes...\\n" +
                  $"Strategy: {_router.strategy}\\n" +
                  $"Orders: {_orderManager.PendingCount} pending\\n" +
                  $"Fleet: {fleet.Count} drones (cap={capacity})\\n" +
                  $"Please wait...");

        var routes = _router.PlanRoutes(_orderManager.AllOrders, fleet, depotLLH, capacity);

        if (routes.Count > 0)
            SetOutput(_router.GetSolutionSummary());
        else
            SetOutput("[FAIL] No feasible routes found.\\nCheck capacity and time windows.");
    }

    private void OnDispatchAll()
    {
        if (_router == null || _router.LastSolution == null || _router.LastSolution.Count == 0)
        {
            SetOutput("[FAIL] No routes to dispatch.\\nClick 'Solve Routes' first.");
            return;
        }

        if (_dispatcher == null)
        {
            SetOutput("[FAIL] RouteDispatcher not found in scene.");
            return;
        }

        int count = _dispatcher.DispatchAll(_router.LastSolution);

        if (count > 0)
            SetOutput($"[OK] Dispatched {count} routes!\\n\\n{_dispatcher.GetDispatchStatus()}");
        else
            SetOutput("[FAIL] Failed to dispatch routes. Check console.");
    }

    private void OnStopAll()
    {
        var cc = FindObjectOfType<DroneCommandCenter>();
        if (cc != null)
        {
            cc.PauseAll(true);
            SetOutput("[OK] All drones stopped.\\nClick 'Resume All' or use chat to resume.");
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private void RefreshAll()
    {
        RefreshFleetTab();
        RefreshPointsTab();
        RefreshOrdersTab();
    }

    private void SetOutput(string msg)
    {
        if (_uiManager != null)
            _uiManager.UpdateOutputText(msg);
    }

    private void BindButton(Button btn, UnityEngine.Events.UnityAction action)
    {
        if (!btn) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(action);
    }
}