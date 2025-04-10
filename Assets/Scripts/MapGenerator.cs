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

    // Unity calls this once on scene start
    void Start() => GenerateMap();

    // Unity calls this every frame
    void Update()
    {
        // Left-click to regenerate the map during Play mode
        if (Input.GetMouseButtonDown(0))
        {
            GenerateMap();
        }

        // Make zombies rotate toward the lab
        RotateZombiesTowardLab();

        // Move zombies if wandering is enabled
        if (allowZombieWandering)
        {
            MoveZombiesRandomly();
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

    // Moves zombies toward a random point on their island
    void MoveZombiesRandomly()
    {
        foreach (var zombie in spawnedZombies)
        {
            if (zombie == null || !zombieToIsland.ContainsKey(zombie)) continue;

            Vector3 currentPos = zombie.transform.position;
            Vector3 targetPos = zombieTargetPositions[zombie];

            // Move towards the current target
            zombie.transform.position = Vector3.MoveTowards(currentPos, targetPos, zombieMoveSpeed * Time.deltaTime);

            // If near target, choose a new one
            if (Vector3.Distance(currentPos, targetPos) < 0.1f)
            {
                List<Vector2Int> islandTiles = zombieToIsland[zombie];
                List<Vector2Int> validTiles = islandTiles.FindAll(tile => IsInMapRange(tile.x, tile.y) && map[tile.x, tile.y] == 1);
                if (validTiles.Count == 0) continue;

                Vector2Int newCoord = validTiles[UnityEngine.Random.Range(0, validTiles.Count)];
                Vector3 newWorldPos = CoordToWorldPoint(newCoord.x, newCoord.y) + Vector3.up * 0.5f;

                zombieTargetPositions[zombie] = newWorldPos;
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

    // Applies cellular automata smoothing to make landmasses cohesive
    void SmoothMap()
    {
        int[,] newMap = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int neighborLandTiles = GetSurroundingLandCount(x, y);
                if (neighborLandTiles > 4)
                    newMap[x, y] = 1;
                else if (neighborLandTiles < 4)
                    newMap[x, y] = 0;
                else
                    newMap[x, y] = map[x, y];
            }
        }
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

    // Finds all contiguous regions of a given tile type (land or water)
    List<List<Vector2Int>> GetRegions(int tileType)
    {
        List<List<Vector2Int>> regions = new();
        bool[,] visited = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!visited[x, y] && map[x, y] == tileType)
                {
                    List<Vector2Int> region = new();
                    Queue<Vector2Int> queue = new();
                    queue.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;

                    while (queue.Count > 0)
                    {
                        Vector2Int coord = queue.Dequeue();
                        region.Add(coord);

                        foreach (var n in new Vector2Int[]
                        {
                            new Vector2Int(coord.x + 1, coord.y),
                            new Vector2Int(coord.x - 1, coord.y),
                            new Vector2Int(coord.x, coord.y + 1),
                            new Vector2Int(coord.x, coord.y - 1)
                        })
                        {
                            if (IsInMapRange(n.x, n.y) && !visited[n.x, n.y] && map[n.x, n.y] == tileType)
                            {
                                visited[n.x, n.y] = true;
                                queue.Enqueue(n);
                            }
                        }
                    }

                    regions.Add(region);
                }
            }
        }
        return regions;
    }

       // Final map cleanup and game object placement
    void ProcessMap()
    {
        // 1. Set all map borders to water to ensure isolation
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

        // 2. Remove small land patches (less than threshold)
        List<List<Vector2Int>> landRegions = GetRegions(1);
        int landThresholdSize = 10;
        List<Vector2Int> largestLand = null;
        int largestLandSize = 0;
        foreach (var region in landRegions)
        {
            if (region.Count > largestLandSize)
            {
                largestLandSize = region.Count;
                largestLand = region;
            }
        }
        foreach (var region in landRegions)
        {
            if (region.Count < landThresholdSize && (region != largestLand || landRegions.Count > 1))
            {
                foreach (var coord in region)
                    map[coord.x, coord.y] = 0;
            }
        }

        // 3. Fill small lakes (water regions not touching edges)
        List<List<Vector2Int>> waterRegions = GetRegions(0);
        int waterThresholdSize = 10;
        foreach (var region in waterRegions)
        {
            bool isOcean = false;
            foreach (var coord in region)
            {
                if (coord.x == 0 || coord.x == width - 1 || coord.y == 0 || coord.y == height - 1)
                {
                    isOcean = true;
                    break;
                }
            }
            if (!isOcean && region.Count < waterThresholdSize)
            {
                foreach (var coord in region)
                    map[coord.x, coord.y] = 1;
            }
        }

        // === Lab and Zombie Placement ===
        if (labPrefab != null)
        {
            if (currentLabInstance != null)
                Destroy(currentLabInstance);

            // Remove old zombies
            foreach (var zombie in spawnedZombies)
                Destroy(zombie);
            spawnedZombies.Clear();

            landRegions = GetRegions(1);
            List<List<Vector2Int>> validIslands = new();
            foreach (var region in landRegions)
            {
                if (region.Count >= landThresholdSize)
                    validIslands.Add(region);
            }

            if (validIslands.Count == 0) return;

            System.Random prng = new System.Random(seed.GetHashCode());
            int labIslandIndex = prng.Next(0, validIslands.Count);
            List<Vector2Int> labIsland = validIslands[labIslandIndex];

            // Place lab at island center or fallback to random land tile
            Vector2Int labPos = FindRegionCenter(labIsland);
            if (map[labPos.x, labPos.y] != 1)
                labPos = labIsland[prng.Next(0, labIsland.Count)];
            Vector3 labWorldPos = CoordToWorldPoint(labPos.x, labPos.y);
            currentLabInstance = Instantiate(labPrefab, labWorldPos, Quaternion.identity);
            currentLabInstance.name = "ResearchLab";

            // Spawn zombies on all islands (including lab's)
            for (int i = 0; i < validIslands.Count; i++)
            {
                List<Vector2Int> island = validIslands[i];

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

                    // Register zombie
                    if (!zombieToIsland.ContainsKey(zombie))
                        zombieToIsland[zombie] = island;
                    zombieTargetPositions[zombie] = zWorldPos;
                }
            }

            // === Player Spawn on Safest Island ===
            if (playerPrefab != null)
            {
                if (currentPlayerInstance != null)
                    Destroy(currentPlayerInstance);

                Dictionary<int, int> zombieCounts = new();
                for (int i = 0; i < validIslands.Count; i++) zombieCounts[i] = 0;

                foreach (var kvp in zombieToIsland)
                {
                    int index = validIslands.IndexOf(kvp.Value);
                    if (index >= 0)
                        zombieCounts[index]++;
                }

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

        // === Tree Decoration on Land Tiles ===
        if (treePrefab != null)
        {
            // Remove previous trees
            foreach (var tree in spawnedTrees)
            {
                if (tree != null)
                    Destroy(tree);
            }
            spawnedTrees.Clear();

            System.Random prng = new System.Random(seed.GetHashCode());

            // Randomly place trees on land
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




    