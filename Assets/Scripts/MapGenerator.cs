using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Procedural generation for a zombie apocalypse scenario on an archipelago.
/// Generates islands, places a lab, spawns zombies and a player, adds trees, and handles simple zombie movement.
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("Map Dimensions")]
    public int width;  // Width of the map grid
    public int height; // Height of the map grid

    [Header("Random Seed Controls")]
    public string seed;             // Seed for deterministic generation
    public bool useRandomSeed;     // Use a random seed if true
    [Range(0, 100)]
    public int randomFillPercent;  // % of the grid initially filled with land

    [Header("Optional Prefabs")]
    public GameObject labPrefab;       // Yellow cube with light (goal)
    public GameObject zombiePrefab;    // Red cube (enemy)
    public int zombiesPerIsland = 2;   // Deprecated: use density-based spawning
    public bool allowZombieWandering = true; // Toggle zombie movement
    [Range(0f, 1f)] public float zombieMoveChance = 0.02f; // Chance to choose a new target
    [Range(0.001f, 0.05f)] public float zombieDensityFactor = 0.01f; // Zombies per tile
    public int minZombiesPerIsland = 1; // Lower bound
    public int maxZombiesPerIsland = 10; // Upper bound

    private int[,] map; // 2D map grid, 1 = land, 0 = water
    private GameObject currentLabInstance; // Spawned lab reference
    private List<GameObject> spawnedZombies = new(); // All zombies
    private Dictionary<GameObject, List<Vector2Int>> zombieToIsland = new(); // Which island each zombie belongs to
    private Dictionary<GameObject, Vector3> zombieTargetPositions = new(); // Where each zombie is walking toward

    [Range(0.1f, 5f)]
    public float zombieMoveSpeed = 1f; // Speed zombies walk

    private List<List<Vector2Int>> islandRegions = new(); // Store islands for spawning
    private List<Color> islandColors = new(); // For drawing islands with Gizmos

    [Header("Player Settings")]
    public GameObject playerPrefab; // Player (cylinder + light)
    private GameObject currentPlayerInstance;

    public GameObject treePrefab; // Tree decoration (green sphere)
    [Range(0, 1f)] public float treeSpawnChance = 0.1f; // 10% chance to spawn on each land tile
    private List<GameObject> spawnedTrees = new(); // Keep track of spawned trees

    [Header("Civilian Settings")]
    public GameObject civilianPrefab; // Prefab for civilians
    public int civiliansPerIsland = 1; // Number of civilians per island
    public float civilianFleeDistance = 3f; // Distance at which civilians flee
    public float civilianMoveSpeed = 1.5f; // Speed at which civilians move
    public float zombieChaseRange = 4f; // Distance zombies detect and chase civilians

    private List<GameObject> spawnedCivilians = new List<GameObject>();
    private Dictionary<GameObject, Vector3> civilianTargets = new Dictionary<GameObject, Vector3>();

    // Unity calls this once on scene start
    void Start() => GenerateMap();

    // Per-frame logic for behavior updates
    void Update()
    {
        // Regenerate map on left-click during Play mode
        if (Input.GetMouseButtonDown(0))
        {
            GenerateMap();
        }

        // Makes all zombies face the lab
        RotateZombiesTowardLab();
        
        // Moves civilians randomly or makes them flee from zombies
        MoveCivilians();

        // If allowed, zombies will chase civilians or patrol
        if (allowZombieWandering)
        {
            MoveZombiesWithChase();
        }
    }


    /// <summary>
    /// Controls civilian wandering and fleeing behavior.
    /// Civilians flee from zombies if they come too close (within flee distance).
    /// Otherwise, they wander randomly within their assigned island.
    /// </summary>
    void MoveCivilians()
    {
        foreach (var civilian in spawnedCivilians)
        {
            if (civilian == null) continue;

            Vector3 currentPos = civilian.transform.position;
            Vector3 targetPos = civilianTargets[civilian];

            bool fleeing = false;

            // Check proximity to zombies — trigger fleeing if within range
            foreach (var zombie in spawnedZombies)
            {
                if (zombie == null) continue;
                float dist = Vector3.Distance(zombie.transform.position, currentPos);

                if (dist < civilianFleeDistance)
                {
                    // Set target position in opposite direction from the zombie
                    Vector3 fleeDir = (currentPos - zombie.transform.position).normalized;
                    targetPos = currentPos + fleeDir * 3f;
                    fleeing = true;
                    break;
                }
            }

            // Move the civilian toward their target
            civilian.transform.position = Vector3.MoveTowards(currentPos, targetPos, civilianMoveSpeed * Time.deltaTime);

            // Pick a new idle destination if not fleeing and close to current target
            if (!fleeing && Vector3.Distance(currentPos, targetPos) < 0.2f)
            {
                if (zombieToIsland.TryGetValue(civilian, out var island))
                {
                    var validTiles = island.FindAll(tile => map[tile.x, tile.y] == 1);
                    if (validTiles.Count > 0)
                    {
                        var coord = validTiles[UnityEngine.Random.Range(0, validTiles.Count)];
                        civilianTargets[civilian] = CoordToWorldPoint(coord.x, coord.y) + Vector3.up * 0.5f;
                    }
                }
            }
        }
    }



    /// <summary>
    /// Handles zombie chasing and patrolling behavior.
    /// If a civilian is in range, the zombie chases.
    /// Otherwise, it moves to a random point on its island.
    /// If it reaches a civilian, that civilian is destroyed and turned into a zombie.
    /// </summary>
    void MoveZombiesWithChase()
    {
        foreach (var zombie in spawnedZombies)
        {
            if (zombie == null || !zombieToIsland.ContainsKey(zombie)) continue;

            Vector3 currentPos = zombie.transform.position;
            Vector3 targetPos = zombieTargetPositions[zombie];

            GameObject nearestCivilian = null;
            float minDist = float.MaxValue;

            // Identify nearest civilian in chase range
            foreach (var civilian in spawnedCivilians)
            {
                if (civilian == null) continue;
                float dist = Vector3.Distance(civilian.transform.position, currentPos);
                if (dist < zombieChaseRange && dist < minDist)
                {
                    minDist = dist;
                    nearestCivilian = civilian;
                }
            }

            // Set target position for chasing or random movement
            if (nearestCivilian != null)
            {
                targetPos = nearestCivilian.transform.position;
                zombieTargetPositions[zombie] = targetPos;

                // If very close to the civilian — "catch" them
                if (Vector3.Distance(currentPos, nearestCivilian.transform.position) < 0.4f)
                {
                    // Replace the civilian with a zombie at the same location
                    Vector3 pos = nearestCivilian.transform.position;
                    Quaternion rot = Quaternion.identity;

                    GameObject newZombie = Instantiate(zombiePrefab, pos, rot);
                    newZombie.name = $"Zombie_Converted";
                    spawnedZombies.Add(newZombie);
                    zombieTargetPositions[newZombie] = pos;

                    if (zombieToIsland.ContainsKey(zombie))
                    {
                        var island = zombieToIsland[zombie];
                        zombieToIsland[newZombie] = island;
                    }

                    // Clean up the civilian
                    civilianTargets.Remove(nearestCivilian);
                    spawnedCivilians.Remove(nearestCivilian);
                    Destroy(nearestCivilian);
                }
            }

            // Move zombie toward its current target (civilian or random patrol)
            zombie.transform.position = Vector3.MoveTowards(currentPos, targetPos, zombieMoveSpeed * Time.deltaTime);

            // If idle (reached patrol point), pick a new one
            if (Vector3.Distance(currentPos, targetPos) < 0.2f && nearestCivilian == null)
            {
                List<Vector2Int> islandTiles = zombieToIsland[zombie];
                var validTiles = islandTiles.FindAll(tile => map[tile.x, tile.y] == 1);
                if (validTiles.Count > 0)
                {
                    var coord = validTiles[UnityEngine.Random.Range(0, validTiles.Count)];
                    zombieTargetPositions[zombie] = CoordToWorldPoint(coord.x, coord.y) + Vector3.up * 0.5f;
                }
            }
        }
    }


    // Draws a debug view of the map in the Scene view
    void OnDrawGizmos()
    {
        if (map == null) return;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 pos = CoordToWorldPoint(x, y);

                if (map[x, y] == 1)
                {
                    for (int i = 0; i < islandRegions.Count; i++)
                    {
                        if (islandRegions[i].Contains(new Vector2Int(x, y)))
                        {
                            Gizmos.color = islandColors[i];
                            Gizmos.DrawCube(pos, Vector3.one * 0.95f);
                            break;
                        }
                    }
                }
                else
                {
                    Gizmos.color = Color.cyan; // Water
                    Gizmos.DrawCube(pos, Vector3.one * 0.9f);
                }
            }
        }

        // Draw lab as a blue sphere
        if (currentLabInstance != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(currentLabInstance.transform.position + Vector3.up * 1.5f, 0.5f);
        }

        // Draw zombies as red spheres
        Gizmos.color = Color.red;
        foreach (var z in spawnedZombies)
        {
            if (z != null)
                Gizmos.DrawSphere(z.transform.position + Vector3.up * 1.5f, 0.3f);
        }
    }

    // Handles the entire map generation pipeline
    void GenerateMap()
    {
        map = new int[width, height];
        islandRegions.Clear();
        islandColors.Clear();
        zombieToIsland.Clear();

        PopulateMap(); // Stage 1 - Create base map

        for (int i = 0; i < 5; i++)
        {
            SmoothMap(); // Stage 2 - Cellular Automata
        }

        ProcessMap(); // Stage 3 - Remove small regions and place objects

        MeshGenerator meshGen = GetComponent<MeshGenerator>();
        meshGen.GenerateMesh(map, 1f); // Rebuild mesh from map

        AnalyzeMap(); // Debug logging
    }

    // Makes zombies rotate to face the lab
    void RotateZombiesTowardLab()
    {
        if (currentLabInstance == null) return;

        Vector3 labPos = currentLabInstance.transform.position;

        foreach (var zombie in spawnedZombies)
        {
            if (zombie != null)
            {
                Vector3 dir = labPos - zombie.transform.position;
                dir.y = 0; // Don't tilt the zombie up/down
                if (dir != Vector3.zero)
                {
                    Quaternion rot = Quaternion.LookRotation(dir);
                    zombie.transform.rotation = Quaternion.Slerp(zombie.transform.rotation, rot, Time.deltaTime * 2f);
                }
            }
        }
    }

    // Logs how much land exists in the generated map
    void AnalyzeMap()
    {
        int total = width * height;
        int landCount = 0;
        foreach (var val in map)
            if (val == 1) landCount++;

        float landPercent = (float)landCount / total * 100f;
        Debug.Log($"Map Analysis: Land Coverage = {landPercent:F2}% ({landCount}/{total})");
    }

    // Converts grid coordinates to Unity world space
    Vector3 CoordToWorldPoint(int x, int y)
    {
        float worldX = -width / 2f + x + 0.5f;
        float worldZ = -height / 2f + y + 0.5f;
        return new Vector3(worldX, 0f, worldZ);
    }

    // Computes the average position of a region to find its "center"
    Vector2Int FindRegionCenter(List<Vector2Int> region)
    {
        if (region.Count == 0) return new Vector2Int(width / 2, height / 2);
        long sumX = 0, sumY = 0;
        foreach (var coord in region)
        {
            sumX += coord.x;
            sumY += coord.y;
        }
        return new Vector2Int((int)(sumX / region.Count), (int)(sumY / region.Count));
    }

    // Checks if a given coordinate is within the map bounds
    bool IsInMapRange(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    // Randomly scatters circular islands across the grid
    void PopulateMap()
    {
        if (useRandomSeed)
        {
            seed = Time.time.ToString(); // New seed each run
        }
        System.Random prng = new System.Random(seed.GetHashCode());

        // Initialize everything to water
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = 0;
            }
        }

        // Calculate number of islands to create
        int maxPossibleIslands = (width * height) / 300;
        int numIslands = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, maxPossibleIslands, randomFillPercent / 100f)), 1, maxPossibleIslands);

        // Spawn islands as filled circles
        for (int i = 0; i < numIslands; i++)
        {
            int centerX = prng.Next(2, width - 2);
            int centerY = prng.Next(2, height - 2);

            int maxRad = Mathf.Min(width, height) / 4;
            int minRad = Mathf.Max(2, maxRad / 2);
            int radius;

            if (numIslands > 1)
            {
                int reducedMax = maxRad / numIslands + 2;
                int effectiveMax = Mathf.Max(minRad + 1, reducedMax);
                radius = prng.Next(minRad, effectiveMax);
            }
            else
            {
                radius = prng.Next(minRad, maxRad);
            }

            // Fill circle
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int nx = centerX + dx;
                    int ny = centerY + dy;
                    if (IsInMapRange(nx, ny) && dx * dx + dy * dy <= radius * radius)
                    {
                        map[nx, ny] = 1;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies cellular automata smoothing to the map to make landmasses more natural.
    /// For each tile, checks the number of neighboring land tiles and adjusts the tile based on that.
    /// This helps eliminate noise and creates cohesive island shapes.
    /// </summary>
    void SmoothMap()
    {
        int[,] newMap = new int[width, height]; // Temporary map to hold smoothed values

        // Loop through every tile in the grid
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Count how many of the surrounding 8 tiles are land (value = 1)
                int neighborLandTiles = GetSurroundingLandCount(x, y);

                // Rule 1: If more than 4 neighbors are land, become land
                if (neighborLandTiles > 4)
                    newMap[x, y] = 1;

                // Rule 2: If fewer than 4 neighbors are land, become water
                else if (neighborLandTiles < 4)
                    newMap[x, y] = 0;

                // Rule 3: Otherwise, stay the same as the original map
                else
                    newMap[x, y] = map[x, y];
            }
        }

        // Replace the old map with the smoothed version
        map = newMap;
    }


    // Counts how many surrounding tiles are land
    int GetSurroundingLandCount(int gridX, int gridY)
    {
        int landCount = 0;
        for (int x = gridX - 1; x <= gridX + 1; x++)
        {
            for (int y = gridY - 1; y <= gridY + 1; y++)
            {
                if (x == gridX && y == gridY) continue;
                if (IsInMapRange(x, y))
                    landCount += map[x, y];
            }
        }
        return landCount;
    }

    /// <summary>
    /// Finds all contiguous regions on the map that match a specific tile type (e.g., land = 1, water = 0).
    /// Uses a breadth-first search (BFS) flood fill algorithm to group connected tiles into regions.
    /// </summary>
    List<List<Vector2Int>> GetRegions(int tileType)
    {
        List<List<Vector2Int>> regions = new(); // Holds all discovered regions
        bool[,] visited = new bool[width, height]; // Keeps track of which tiles we've already checked

        // Loop through each tile in the map
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // If this tile matches the target type and hasn't been visited yet
                if (!visited[x, y] && map[x, y] == tileType)
                {
                    List<Vector2Int> region = new();            // Holds coordinates for this particular region
                    Queue<Vector2Int> queue = new();            // Queue for BFS
                    queue.Enqueue(new Vector2Int(x, y));        // Start from this tile
                    visited[x, y] = true;

                    // Begin breadth-first search to find all connected tiles
                    while (queue.Count > 0)
                    {
                        Vector2Int coord = queue.Dequeue();     // Dequeue tile for processing
                        region.Add(coord);                       // Add it to the current region

                        // Check all 4 neighboring tiles (up, down, left, right)
                        foreach (var n in new Vector2Int[]
                        {
                            new Vector2Int(coord.x + 1, coord.y),
                            new Vector2Int(coord.x - 1, coord.y),
                            new Vector2Int(coord.x, coord.y + 1),
                            new Vector2Int(coord.x, coord.y - 1)
                        })
                        {
                            // If the neighbor is in bounds, not visited yet, and same tile type — enqueue it
                            if (IsInMapRange(n.x, n.y) && !visited[n.x, n.y] && map[n.x, n.y] == tileType)
                            {
                                visited[n.x, n.y] = true;
                                queue.Enqueue(n);
                            }
                        }
                    }

                    // Finished discovering one full region — add it to the list
                    regions.Add(region);
                }
            }
        }

        return regions; // Return all discovered regions
    }


    /// <summary>
    /// Final stage of the generation pipeline: cleans up map, removes small regions,
    /// places core objects like the lab, player, zombies, civilians, and trees.
    /// This is where most of the scene setup logic happens.
    /// </summary>
    void ProcessMap()
    {
        // === STEP 1: Ensure all edges of the map are water to avoid weird borders ===
        for (int x = 0; x < width; x++)
        {
            map[x, 0] = 0;
            map[x, height - 1] = 0;
        }
        for (int y = 0; y < height; y++)
        {
            map[0, y] = 0;
            map[width - 1, y] = 0;
        }

        // === STEP 2: Remove tiny land regions that are too small to be meaningful ===
        List<List<Vector2Int>> landRegions = GetRegions(1);
        int landThresholdSize = 10;
        List<Vector2Int> largestLand = null;
        int largestLandSize = 0;

        // Find the largest landmass so it doesn't get removed accidentally
        foreach (var region in landRegions)
        {
            if (region.Count > largestLandSize)
            {
                largestLandSize = region.Count;
                largestLand = region;
            }
        }

        // Remove all land patches smaller than threshold, except for the largest one
        foreach (var region in landRegions)
        {
            if (region.Count < landThresholdSize && (region != largestLand || landRegions.Count > 1))
            {
                foreach (var coord in region)
                    map[coord.x, coord.y] = 0;
            }
        }

        // === STEP 3: Fill in small lakes (closed water bodies that don’t touch the map edges) ===
        List<List<Vector2Int>> waterRegions = GetRegions(0);
        int waterThresholdSize = 10;

        foreach (var region in waterRegions)
        {
            bool isOcean = false;

            // If any water tile touches the edge, it's part of the ocean
            foreach (var coord in region)
            {
                if (coord.x == 0 || coord.x == width - 1 || coord.y == 0 || coord.y == height - 1)
                {
                    isOcean = true;
                    break;
                }
            }

            // Fill the lake if it's isolated and too small
            if (!isOcean && region.Count < waterThresholdSize)
            {
                foreach (var coord in region)
                    map[coord.x, coord.y] = 1;
            }
        }

        // === STEP 4: Place LAB and ZOMBIES ===
        if (labPrefab != null)
        {
            // Remove previously spawned lab
            if (currentLabInstance != null)
                Destroy(currentLabInstance);

            // Destroy old zombies before spawning new ones
            foreach (var zombie in spawnedZombies)
                Destroy(zombie);
            spawnedZombies.Clear();

            // Recalculate valid land regions after cleanup
            landRegions = GetRegions(1);
            List<List<Vector2Int>> validIslands = new();
            foreach (var region in landRegions)
            {
                if (region.Count >= landThresholdSize)
                    validIslands.Add(region);
            }

            if (validIslands.Count == 0) return;

            // Pick a random island to place the lab
            System.Random prng = new System.Random(seed.GetHashCode());
            int labIslandIndex = prng.Next(0, validIslands.Count);
            List<Vector2Int> labIsland = validIslands[labIslandIndex];

            // Place lab in the center of the island, or a fallback tile
            Vector2Int labPos = FindRegionCenter(labIsland);
            if (map[labPos.x, labPos.y] != 1)
                labPos = labIsland[prng.Next(0, labIsland.Count)];

            Vector3 labWorldPos = CoordToWorldPoint(labPos.x, labPos.y);
            currentLabInstance = Instantiate(labPrefab, labWorldPos, Quaternion.identity);
            currentLabInstance.name = "ResearchLab";

            // === Spawn zombies on all valid islands ===
            for (int i = 0; i < validIslands.Count; i++)
            {
                List<Vector2Int> island = validIslands[i];

                // Calculate how many zombies to spawn based on island size
                int rawZombieCount = Mathf.RoundToInt(island.Count * zombieDensityFactor);
                int clampedCount = Mathf.Clamp(rawZombieCount, minZombiesPerIsland, maxZombiesPerIsland);

                for (int j = 0; j < clampedCount; j++)
                {
                    int zombieIndex = prng.Next(0, island.Count);
                    Vector2Int zPos = island[zombieIndex];
                    Vector3 zWorldPos = CoordToWorldPoint(zPos.x, zPos.y);

                    GameObject zombie = Instantiate(zombiePrefab, zWorldPos + Vector3.up * 0.5f, Quaternion.identity);
                    zombie.name = $"Zombie_{i}_{j}";
                    spawnedZombies.Add(zombie);

                    // Track which island this zombie belongs to
                    if (!zombieToIsland.ContainsKey(zombie))
                        zombieToIsland[zombie] = island;

                    zombieTargetPositions[zombie] = zWorldPos;
                }
            }

            // === Spawn CIVILIANS and register their islands ===
            if (civilianPrefab != null)
            {
                // Clean up previous civilians
                foreach (var civilian in spawnedCivilians)
                    if (civilian != null) Destroy(civilian);
                spawnedCivilians.Clear();
                civilianTargets.Clear();

                // Place civilians on each island
                for (int i = 0; i < validIslands.Count; i++)
                {
                    var island = validIslands[i];
                    for (int j = 0; j < civiliansPerIsland; j++)
                    {
                        Vector2Int cPos = island[UnityEngine.Random.Range(0, island.Count)];
                        Vector3 worldPos = CoordToWorldPoint(cPos.x, cPos.y) + Vector3.up * 0.5f;

                        GameObject civilian = Instantiate(civilianPrefab, worldPos, Quaternion.identity);
                        civilian.name = $"Civilian_{i}_{j}";
                        spawnedCivilians.Add(civilian);
                        civilianTargets[civilian] = worldPos;
                        zombieToIsland[civilian] = island;
                    }
                }
            }

            // === Spawn PLAYER on the safest island (fewest zombies and not lab island) ===
            if (playerPrefab != null)
            {
                if (currentPlayerInstance != null)
                    Destroy(currentPlayerInstance);

                // Count number of zombies per island
                Dictionary<int, int> zombieCounts = new();
                for (int i = 0; i < validIslands.Count; i++) zombieCounts[i] = 0;

                foreach (var kvp in zombieToIsland)
                {
                    int index = validIslands.IndexOf(kvp.Value);
                    if (index >= 0)
                        zombieCounts[index]++;
                }

                // Find the island with the least zombies that is not the lab island
                int safestIslandIndex = -1;
                int minZombies = int.MaxValue;
                for (int i = 0; i < validIslands.Count; i++)
                {
                    if (i == labIslandIndex) continue;
                    if (zombieCounts[i] < minZombies)
                    {
                        minZombies = zombieCounts[i];
                        safestIslandIndex = i;
                    }
                }

                if (safestIslandIndex != -1)
                {
                    List<Vector2Int> playerIsland = validIslands[safestIslandIndex];
                    Vector2Int playerTile = playerIsland[prng.Next(0, playerIsland.Count)];
                    Vector3 playerWorldPos = CoordToWorldPoint(playerTile.x, playerTile.y);

                    currentPlayerInstance = Instantiate(playerPrefab, playerWorldPos + Vector3.up * 0.5f, Quaternion.identity);
                    currentPlayerInstance.name = "Player";
                }
            }
        }

        // === STEP 5: Randomly Decorate Land Tiles with Trees ===
        if (treePrefab != null)
        {
            // Remove previously spawned trees
            foreach (var tree in spawnedTrees)
            {
                if (tree != null)
                    Destroy(tree);
            }
            spawnedTrees.Clear();

            System.Random prng = new System.Random(seed.GetHashCode());

            // Decorate land tiles based on spawn chance
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (map[x, y] == 1 && prng.NextDouble() < treeSpawnChance)
                    {
                        Vector3 treePos = CoordToWorldPoint(x, y) + Vector3.up * 0.5f;
                        Quaternion rot = Quaternion.Euler(0, prng.Next(0, 360), 0);
                        GameObject newTree = Instantiate(treePrefab, treePos, rot);
                        spawnedTrees.Add(newTree);
                    }
                }
            }
        }
    }

    
}




    