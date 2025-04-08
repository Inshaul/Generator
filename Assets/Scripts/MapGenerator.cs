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
    public int randomFillPercent;   // Roughly interpreted as overall land coverage percentage

    [Header("Optional Features")]
    public GameObject labPrefab;    // Prefab for the research lab (optional)

    private int[,] map;             // 2D grid for map: 1 = land, 0 = water

    void Start()
    {
        GenerateMap();
    }

    void Update()
    {
        // Regenerate map on left mouse click (for testing different layouts)
        if (Input.GetMouseButtonDown(0))
        {
            GenerateMap();
        }
    }

    void GenerateMap()
    {
        map = new int[width, height];

        // ** Stage 1: Initial grid generation (populate map with islands) **
        PopulateMap();  // custom method to fill the map with initial land/water configuration

        // ** Stage 2: Cellular Automata Smoothing **
        for (int i = 0; i < 5; i++)
        {
            SmoothMap();
        }

        // ** Stage 3: Final processing (cleanup and feature placement) **
        ProcessMap();
        // Note: We skip adding a surrounding border of walls, because we want an open ocean boundary.

        // Generate the mesh for visualization
        MeshGenerator meshGen = GetComponent<MeshGenerator>();
        meshGen.GenerateMesh(map, 1f);
    }

    // Stage 1: Populate the map with initial islands
    void PopulateMap()
    {
        if (useRandomSeed)
        {
            // Use current time as seed for randomness if requested
            seed = Time.time.ToString();
        }
        System.Random prng = new System.Random(seed.GetHashCode());

        // Start with all water
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                map[x, y] = 0; // 0 represents water
            }
        }

        // Determine number of islands based on randomFillPercent and map size
        int numIslands = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1, 6, randomFillPercent / 100f)));
        // We allow 1 to 6 islands (approx.) depending on desired land coverage
        // Alternatively, use randomFillPercent more directly:
        // numIslands = prng.Next(1, randomFillPercent/10 + 2);

        // For each island, pick a random center and radius, then fill in land
        for (int i = 0; i < numIslands; i++)
        {
            // Pick a center position away from the map border (to avoid touching edges)
            int centerX = prng.Next(2, width - 2);
            int centerY = prng.Next(2, height - 2);

            // Choose a random radius for the island (based on map size and desired coverage)
            // Larger randomFillPercent -> potentially larger islands
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
            // if (numIslands > 1)
            // {
            //     // If many islands, reduce radius to spread land among them
            //     radius = prng.Next(minRad, maxRad / numIslands + 2);
            // }

            // Fill a circle (or diamond) of land around the center
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int nx = centerX + dx;
                    int ny = centerY + dy;
                    if (IsInMapRange(nx, ny))
                    {
                        // Check distance from center to keep a roughly circular shape
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            map[nx, ny] = 1; // mark as land
                        }
                    }
                }
            }
        }

        // (Optional shaping) We could introduce some randomness in island shapes here.
        // For example, randomly remove a few land tiles to create rough edges:
        // Iterate through map and for each land tile on the edge of an island,
        // have a small chance to set it to water. This can create bays or inlets.
        // We'll rely mostly on the smoothing step to irregularize the coastlines.
    }

    // Stage 2: Smooth the map using cellular automata rules
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
                    newMap[x, y] = map[x, y]; // if exactly 4 neighbors, remain the same
            }
        }
        map = newMap;
    }

    // Count the number of neighboring cells that are land (1) around a given cell
    int GetSurroundingLandCount(int gridX, int gridY)
    {
        int landCount = 0;
        for (int neighborX = gridX - 1; neighborX <= gridX + 1; neighborX++)
        {
            for (int neighborY = gridY - 1; neighborY <= gridY + 1; neighborY++)
            {
                if (neighborX == gridX && neighborY == gridY) continue; // skip itself

                if (IsInMapRange(neighborX, neighborY))
                {
                    landCount += map[neighborX, neighborY]; // add 1 for land, 0 for water
                }
                else
                {
                    // Outside map bounds: treat as water (do not count as land)
                    // (This ensures edges don't count imaginary land beyond the border)
                    // landCount += 1; // [Original cave logic would count out-of-bounds as land]
                }
            }
        }
        return landCount;
    }

    bool IsInMapRange(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    // Stage 3: Process the map to finalize level
    void ProcessMap()
    {
        // 1. Ensure map border is water (clear any land on the outermost edges)
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

        // 2. Remove small isolated land regions (small islands)
        List<List<Vector2Int>> landRegions = GetRegions(1); // get all land regions (1 = land)
        int landThresholdSize = 10;  // minimum size for an island to survive
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
                // If this region is below threshold and is not the largest region (to keep at least one island)
                if (region != largestLand || landRegions.Count > 1)
                {
                    // Convert all these land cells to water
                    foreach (var coord in region)
                    {
                        map[coord.x, coord.y] = 0;
                    }
                }
            }
        }
        // Note: If all islands were below threshold, we spared the largest one above to avoid removing all land.

        // 3. Remove small isolated water regions (fill small lakes inside islands)
        List<List<Vector2Int>> waterRegions = GetRegions(0); // all water regions (0 = water)
        int waterThresholdSize = 10; // minimum size for a water region to be considered a lake
        foreach (var region in waterRegions)
        {
            // Skip the "ocean" (any region touching the border is ocean, not an inland lake)
            bool isOcean = false;
            foreach (var coord in region)
            {
                if (coord.x == 0 || coord.x == width - 1 || coord.y == 0 || coord.y == height - 1)
                {
                    isOcean = true;
                    break;
                }
            }
            if (isOcean) 
                continue; // don't fill the ocean or any water connected to the border

            if (region.Count < waterThresholdSize)
            {
                // Fill small lake with land
                foreach (var coord in region)
                {
                    map[coord.x, coord.y] = 1;
                }
            }
        }

        // 4. (Optional) Place the research lab on the largest island
        if (labPrefab != null)
        {
            if (largestLand == null)
            {
                // Recompute largest land region after removals if needed
                landRegions = GetRegions(1);
                foreach (var region in landRegions)
                {
                    if (region.Count > largestLandSize)
                    {
                        largestLandSize = region.Count;
                        largestLand = region;
                    }
                }
            }
            if (largestLand != null && largestLand.Count > 0)
            {
                // Find center of largest island region
                Vector2Int labPosition = FindRegionCenter(largestLand);
                // Instantiate the lab prefab at the corresponding world position
                Vector3 labWorldPos = CoordToWorldPoint(labPosition.x, labPosition.y);
                Instantiate(labPrefab, labWorldPos, Quaternion.identity);
            }
        }
    }

    // Get all regions (connected components) of a given cell type (0 for water, 1 for land).
    // Uses flood-fill (BFS/DFS) to collect connected cells.
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
                    // Found a new region via an unvisited cell
                    List<Vector2Int> region = new List<Vector2Int>();
                    Queue<Vector2Int> queue = new Queue<Vector2Int>();
                    queue.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;

                    // BFS flood fill
                    while (queue.Count > 0)
                    {
                        Vector2Int coord = queue.Dequeue();
                        region.Add(coord);

                        // Check 4-neighbors for connectivity (up, down, left, right)
                        List<Vector2Int> neighbors = new List<Vector2Int>()
                        {
                            new Vector2Int(coord.x + 1, coord.y),
                            new Vector2Int(coord.x - 1, coord.y),
                            new Vector2Int(coord.x, coord.y + 1),
                            new Vector2Int(coord.x, coord.y - 1)
                        };
                        foreach (Vector2Int n in neighbors)
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

    // Helper to find an approximate center of a region (by average position)
    Vector2Int FindRegionCenter(List<Vector2Int> region)
    {
        if (region.Count == 0) return new Vector2Int(width/2, height/2);
        long sumX = 0, sumY = 0;
        foreach (var coord in region)
        {
            sumX += coord.x;
            sumY += coord.y;
        }
        int avgX = (int)(sumX / region.Count);
        int avgY = (int)(sumY / region.Count);

        // Find the region cell closest to the average point
        Vector2Int center = region[0];
        float minDist = float.MaxValue;
        foreach (var coord in region)
        {
            float dx = coord.x - avgX;
            float dy = coord.y - avgY;
            float dist = dx * dx + dy * dy;
            if (dist < minDist)
            {
                minDist = dist;
                center = coord;
            }
        }
        return center;
    }

    // Convert a grid coordinate (x,y) to a world position (for object placement).
    // Assumes the mesh is centered at (0,0) in XZ plane and each tile = 1 unit.
    Vector3 CoordToWorldPoint(int x, int y)
    {
        // Map is centered at (0,0) in world, with width and height in X and Z axes.
        float worldX = -width / 2f + x + 0.5f;
        float worldZ = -height / 2f + y + 0.5f;
        return new Vector3(worldX, 0f, worldZ);
    }
}
