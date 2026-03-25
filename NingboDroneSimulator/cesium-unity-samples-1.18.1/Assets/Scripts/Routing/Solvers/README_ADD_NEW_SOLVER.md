# How to Add a New Routing Algorithm

## 3 Steps Only:

### Step 1: Create your solver file
Copy `NearestFirstSolver.cs` and rename it (e.g., `MyGeneticSolver.cs`).

### Step 2: Implement the IRoutingSolver interface
```csharp
using System.Collections.Generic;

public class MyGeneticSolver : IRoutingSolver
{
    public string Name => "Genetic Algorithm";
    public string Description => "GA-based VRPTW solver with crossover and mutation.";

    public List<PlannedRoute> Solve(RoutingContext ctx)
    {
        // ctx.Orders       - List of delivery orders
        // ctx.Drones        - List of drone snapshots (id, capacity)
        // ctx.DepotPosition - WGS84 (lon, lat, alt)
        // ctx.VehicleCount  - Number of available drones
        // ctx.Capacity      - Per-drone capacity
        // ctx.SpeedMps      - Planning speed in m/s

        // Return a List<PlannedRoute>, one per drone used.
        // Each PlannedRoute has: droneId, List<RouteStop>

        var routes = new List<PlannedRoute>();
        // ... your algorithm here ...
        return routes;
    }
}
```

### Step 3: Register in SolverRegistry.cs

Open `SolverRegistry.cs`, find `InitBuiltInSolvers()`, add one line:

```csharp
Register(new MyGeneticSolver());
```

### That's it! Run the project, your algorithm appears in the dropdown.

## Key Data Structures:

* **RoutingContext** - All input data for the solver
* **PlannedRoute** - One route for one drone
* **RouteStop** - One delivery stop (customer name, position, demand, time window)
* **DeliveryOrder** - Order with demand, time window, destination

## Tips:

* Check `SolomonI1Solver.cs` for a complete TW-aware example
* Check `NearestFirstSolver.cs` for the simplest possible implementation
* Use `ctx.DistanceMatrix[i,j]` if you precompute distances
* Return empty list if your algorithm fails (system will show 0 routed)
