using UnityEngine;
using System;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Dimensions")]
    public int width;
    public int height;

    [Header("Random Seed Controls")]
    public string seed;
    public bool useRandomSeed;
    [Range(0, 100)]
    public int randomFillPercent;

    [Header("Optional Prefabs")]
    public GameObject labPrefab;
    public GameObject zombiePrefab;
    public int zombiesPerIsland = 2;
    public bool allowZombieWandering = true;
    [Range(0f, 1f)] public float zombieMoveChance = 0.02f;

    [Range(0.001f, 0.05f)] public float zombieDensityFactor = 0.01f;
    public int minZombiesPerIsland = 1;
    public int maxZombiesPerIsland = 10;


    private int[,] map;
    private GameObject currentLabInstance;
    private List<GameObject> spawnedZombies = new List<GameObject>();
    private Dictionary<GameObject, List<Vector2Int>> zombieToIsland = new Dictionary<GameObject, List<Vector2Int>>();

    private Dictionary<GameObject, Vector3> zombieTargetPositions = new Dictionary<GameObject, Vector3>();

    [Range(0.1f, 5f)]
    public float zombieMoveSpeed = 1f;

    // [Gizmos]
    private List<List<Vector2Int>> islandRegions = new List<List<Vector2Int>>();
    private List<Color> islandColors = new List<Color>();


    [Header("Player Settings")]
    public GameObject playerPrefab;
    private GameObject currentPlayerInstance;

    private Dictionary<GameObject, Vector3> zombieTargets = new Dictionary<GameObject, Vector3>();

    public GameObject treePrefab;
    [Range(0, 1f)] public float treeSpawnChance = 0.1f; // 10% of land tiles

    private List<GameObject> spawnedTrees = new List<GameObject>();

    void Start()
    {
        GenerateMap();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            GenerateMap();
        }

        RotateZombiesTowardLab();

        if (allowZombieWandering)
        {
            MoveZombiesRandomly();
        }
    }

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
                    // Land: Color by region
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
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawCube(pos, Vector3.one * 0.9f);
                }
            }
        }

        // Lab Gizmo
        if (currentLabInstance != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(currentLabInstance.transform.position + Vector3.up * 1.5f, 0.5f);
        }

        // Zombies Gizmo
        Gizmos.color = Color.red;
        foreach (var z in spawnedZombies)
        {
            if (z != null)
                Gizmos.DrawSphere(z.transform.position + Vector3.up * 1.5f, 0.3f);
        }
    }

    void GenerateMap()
    {
        map = new int[width, height];
        islandRegions.Clear();
        islandColors.Clear();
        zombieToIsland.Clear();

        PopulateMap();

        for (int i = 0; i < 5; i++)
        {
            SmoothMap();
        }

        ProcessMap();

        MeshGenerator meshGen = GetComponent<MeshGenerator>();
        meshGen.GenerateMesh(map, 1f);

        AnalyzeMap();
    }


    void RotateZombiesTowardLab()
    {
        if (currentLabInstance == null) return;

        Vector3 labPos = currentLabInstance.transform.position;

        foreach (var zombie in spawnedZombies)
        {
            if (zombie != null)
            {
                Vector3 dir = labPos - zombie.transform.position;
                dir.y = 0; // Flatten for Y rotation only
                if (dir != Vector3.zero)
                {
                    Quaternion rot = Quaternion.LookRotation(dir);
                    zombie.transform.rotation = Quaternion.Slerp(zombie.transform.rotation, rot, Time.deltaTime * 2f);
                }
            }
        }
    }

    void MoveZombiesRandomly()
    {
        foreach (var zombie in spawnedZombies)
        {
            if (zombie == null || !zombieToIsland.ContainsKey(zombie)) continue;

            Vector3 currentPos = zombie.transform.position;
            Vector3 targetPos = zombieTargetPositions[zombie];

            // Move smoothly toward target
            zombie.transform.position = Vector3.MoveTowards(currentPos, targetPos, zombieMoveSpeed * Time.deltaTime);

            // If close to target, pick a new one
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

    void AnalyzeMap()
    {
        int total = width * height;
        int landCount = 0;
        foreach (var val in map)
            if (val == 1) landCount++;

        float landPercent = (float)landCount / total * 100f;
        Debug.Log($"Map Analysis: Land Coverage = {landPercent:F2}% ({landCount}/{total})");
    }

    Vector3 CoordToWorldPoint(int x, int y)
    {
        float worldX = -width / 2f + x + 0.5f;
        float worldZ = -height / 2f + y + 0.5f;
        return new Vector3(worldX, 0f, worldZ);
    }

    Vector2Int FindRegionCenter(List<Vector2Int> region)
    {
        if (region.Count == 0) return new Vector2Int(width / 2, height / 2);
        long sumX = 0, sumY = 0;
        foreach (var coord in region)
        {
            sumX += coord.x;
            sumY += coord.y;
        }
        int avgX = (int)(sumX / region.Count);
        int avgY = (int)(sumY / region.Count);
        return new Vector2Int(avgX, avgY);
    }

    bool IsInMapRange(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    void PopulateMap()
    {
        if (useRandomSeed)
        {
            seed = Time.time.ToString();
        }
        System.Random prng = new System.Random(seed.GetHashCode());

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = 0; // Water
            }
        }

        int maxPossibleIslands = (width * height) / 300; // You can tweak this divisor to control density
        int numIslands = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(1, maxPossibleIslands, randomFillPercent / 100f)),
            1, maxPossibleIslands
        );

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

    List<List<Vector2Int>> GetRegions(int tileType)
    {
        List<List<Vector2Int>> regions = new List<List<Vector2Int>>();
        bool[,] visited = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!visited[x, y] && map[x, y] == tileType)
                {
                    List<Vector2Int> region = new List<Vector2Int>();
                    Queue<Vector2Int> queue = new Queue<Vector2Int>();
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

    void ProcessMap()
    {
        // 1. Ensure map border is water
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

        // 2. Remove small land regions
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
            if (region.Count < landThresholdSize)
            {
                if (region != largestLand || landRegions.Count > 1)
                {
                    foreach (var coord in region)
                        map[coord.x, coord.y] = 0;
                }
            }
        }

        // 3. Fill small lakes
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
            if (isOcean) continue;

            if (region.Count < waterThresholdSize)
            {
                foreach (var coord in region)
                    map[coord.x, coord.y] = 1;
            }
        }

        // 4. === LAB & ZOMBIE PLACEMENT ===
        if (labPrefab != null)
        {
            if (currentLabInstance != null)
                Destroy(currentLabInstance);

            foreach (var zombie in spawnedZombies)
                Destroy(zombie);
            spawnedZombies.Clear();

            landRegions = GetRegions(1);
            List<List<Vector2Int>> validIslands = new List<List<Vector2Int>>();
            foreach (var region in landRegions)
            {
                if (region.Count >= landThresholdSize)
                    validIslands.Add(region);
            }

            if (validIslands.Count == 0)
                return;

            System.Random prng = new System.Random(seed.GetHashCode());
            int labIslandIndex = prng.Next(0, validIslands.Count);
            List<Vector2Int> labIsland = validIslands[labIslandIndex];

            Vector2Int labPos = FindRegionCenter(labIsland);
            // Ensure the chosen tile is on land
            if (map[labPos.x, labPos.y] != 1)
            {
                // Pick a fallback from within the island region (should be land)
                labPos = labIsland[prng.Next(0, labIsland.Count)];
            }
            Vector3 labWorldPos = CoordToWorldPoint(labPos.x, labPos.y);
            currentLabInstance = Instantiate(labPrefab, labWorldPos, Quaternion.identity);
            currentLabInstance.name = "ResearchLab";

            // Zombies on other islands
            for (int i = 0; i < validIslands.Count; i++)
            {
                List<Vector2Int> island = validIslands[i];

                // Scale number of zombies based on island size
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

                    if (!zombieToIsland.ContainsKey(zombie))
                        zombieToIsland[zombie] = island;
                    zombieTargetPositions[zombie] = zWorldPos;
                }
            }
            // 5. === PLAYER SPAWNING ===
            if (playerPrefab != null)
            {
                if (currentPlayerInstance != null)
                    Destroy(currentPlayerInstance);

                Dictionary<int, int> zombieCounts = new Dictionary<int, int>();
                for (int i = 0; i < validIslands.Count; i++)
                    zombieCounts[i] = 0;

                // Count zombies per island
                foreach (var kvp in zombieToIsland)
                {
                    int index = validIslands.IndexOf(kvp.Value);
                    if (index >= 0)
                        zombieCounts[index]++;
                }

                // Pick the non-lab island with the fewest zombies
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

        // === TREE DECORATION ===
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



    