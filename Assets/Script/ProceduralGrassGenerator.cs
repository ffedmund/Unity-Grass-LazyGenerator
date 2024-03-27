using UnityEngine;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Mathematics;

[System.Serializable]
public class GrassLOD
{
    public int visibleDistanceThreshold;
    public int density;
    public Material material;
    public Mesh mesh;
}

[System.Serializable]
public class GrassTile
{
    public int lodIndex;
    public Matrix4x4[] tileGrassInstances;
    public uint seed;
}

public class ProceduralGrassGenerator : MonoBehaviour
{
    public Transform playerTransform;
    public Transform cameraHolderTransform;
    [Header("Grass Tile Setting")]
    public GrassLOD[] lods;
    public int gridSize = 5;
    public LayerMask layer;
    public int2 spawnRange;
    [Header("Extra Setting")]
    public bool useFrustumCulling;
    public bool useSmoothDensity;

    private Vector2Int lastUpdatePlayerGridPos;
    private NativeArray<NativeArray<Matrix4x4>> arrayOfLodArray;
    private NativeArray<float4> frustumPlanesNative;
    private Dictionary<Vector2Int,float4> tileCornerHieghtDictionary;
    private int[] lodPivots;
    private Quaternion previousCameraRotation;
    
    public void Setup(Transform localPlayer){
        playerTransform = localPlayer;
    }

    void Awake() {
        tileCornerHieghtDictionary = new Dictionary<Vector2Int, float4>();
        frustumPlanesNative = new NativeArray<float4>(6, Allocator.Persistent);    
        arrayOfLodArray = new NativeArray<NativeArray<Matrix4x4>>(lods.Length, Allocator.Persistent);
        previousCameraRotation = Quaternion.identity;
    }

    void Start(){
        lodPivots = new int[lods.Length];

        int previousTileSize = 0;
        for(int j = 0; j < lods.Length; j++)
        {
            int tileSize = lods[j].visibleDistanceThreshold*2 + 1;
            int arraySize = (tileSize * tileSize - previousTileSize * previousTileSize) * lods[j].density;
            arrayOfLodArray[j] = new NativeArray<Matrix4x4>(arraySize, Allocator.Persistent);
            previousTileSize = tileSize;
            Debug.Log($"Array Size of lod[{j}] is {arraySize}");
        }
    }

    void Update()
    {
        if(playerTransform){
            Vector2 playerPos = new Vector2(playerTransform.position.x, playerTransform.position.z);
            Vector2Int playerGridPos = new Vector2Int(Mathf.RoundToInt(playerPos.x / gridSize), Mathf.RoundToInt(playerPos.y / gridSize));
            if(playerGridPos != lastUpdatePlayerGridPos || (useFrustumCulling && Quaternion.Angle(cameraHolderTransform.rotation, previousCameraRotation) > 1))
            {
                GenerateGrass(playerGridPos,useFrustumCulling?FrustumCulling():null);
                previousCameraRotation = cameraHolderTransform.rotation;
            }
            else
            {
                DrawGrass();
            }
        }
    }

    void GenerateGrass(Vector2Int playerGridPos, NativeArray<bool>? visibleTiles)
    {
        lodPivots = new int[lods.Length];
        int index = 0;

        // Generate new tiles in the visible range
        for (int x = -lods[lods.Length - 1].visibleDistanceThreshold; x <= lods[lods.Length - 1].visibleDistanceThreshold; x++)
        {
            for (int y = -lods[lods.Length - 1].visibleDistanceThreshold; y <= lods[lods.Length - 1].visibleDistanceThreshold; y++)
            {
                if(visibleTiles.HasValue && !visibleTiles.Value[index++]){
                    continue;
                }

                Vector2Int gridPos = new Vector2Int(playerGridPos.x + x, playerGridPos.y + y);
                uint seed = (uint)((gridPos * (gridPos.y % 131 - gridPos.x)).sqrMagnitude + 1);

                // Determine the LOD level based on the distance
                int lodIndex = 0;
                for(int i = 0; i < lods.Length-1; i++)
                {
                    int sqrVisibleDistanceThreshold = lods[i].visibleDistanceThreshold * lods[i].visibleDistanceThreshold;
                    if(x * x > sqrVisibleDistanceThreshold || y * y > sqrVisibleDistanceThreshold)
                    {
                        lodIndex += 1;
                    }
                }

                float4 heights = GetTileCornerHeights(gridPos);
                if(heights.x == -999)
                {
                    continue;
                }

                // Create a new NativeArray to hold the grass instances
                NativeArray<Matrix4x4> tileGrassInstances = arrayOfLodArray[lodIndex];
                NativeArray<int> numOfGrass = new NativeArray<int>(1, Allocator.TempJob);
                int currentPivot = lodPivots[lodIndex];

                // Create a new job and assign the data
                GenerateGrassMatrixJob grassJob = new GenerateGrassMatrixJob
                {
                    gridSize = gridSize,
                    gridPos = gridPos,
                    cornerHeights = heights,
                    tileGrassInstances = tileGrassInstances,
                    random = new Unity.Mathematics.Random(seed),
                    pivot = currentPivot,
                    density = useSmoothDensity?SmoothDensity(lodIndex,gridPos):lods[lodIndex].density,
                    numOfGrass = numOfGrass,
                    minHeight = spawnRange.x,
                    maxHeight = spawnRange.y
                };

                // Schedule the job
                JobHandle grassJobHandle = grassJob.Schedule();
                grassJobHandle.Complete();
                lodPivots[lodIndex] += grassJob.numOfGrass[0];
                numOfGrass.Dispose();
            }
        }
        // Draw the grass instances
        lastUpdatePlayerGridPos = playerGridPos;
        DrawGrass();

        if(visibleTiles.HasValue)
        {
            visibleTiles.Value.Dispose();
        }
    }

    void DrawGrass(){
        int maxInstancesPerCall = 1023;
        for(int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
        {
            RenderParams rp = new RenderParams(lods[lodIndex].material);
            // rp.camera = Camera.main;

            int instanceCount = lodPivots[lodIndex];
            for (int i = 0; i < instanceCount; i += maxInstancesPerCall)
            {
                int count = Mathf.Min(maxInstancesPerCall, instanceCount - i);
                Graphics.RenderMeshInstanced(rp, lods[lodIndex].mesh, 0, arrayOfLodArray[lodIndex], count, i);
            }
        }
        return;
    }

    float4 GetTileCornerHeights(Vector2Int gridPos)
    {
        if(tileCornerHieghtDictionary.ContainsKey(gridPos))
        {
            return tileCornerHieghtDictionary[gridPos];
        }

        float4 heights = new float4();

        // Perform raycasts at the four corners of the grid cell
        Vector3 topLeft = new Vector3((gridPos.x - 0.5f) * gridSize, 1000f, (gridPos.y -0.5f) * gridSize);
        Vector3 topRight = new Vector3((gridPos.x + 0.5f) * gridSize, 1000f, (gridPos.y -0.5f) * gridSize);
        Vector3 bottomLeft = new Vector3((gridPos.x - 0.5f) * gridSize, 1000f, (gridPos.y + 0.5f) * gridSize);
        Vector3 bottomRight = new Vector3((gridPos.x + 0.5f) * gridSize, 1000f, (gridPos.y + 0.5f) * gridSize);

        RaycastHit hit;

        if (!Physics.Raycast(new Vector3(gridPos.x * gridSize, 1000f, gridPos.y * gridSize), Vector3.down, out hit, 1000, layer))
        {
            tileCornerHieghtDictionary.Add(gridPos,new float4(-999,0,0,0));
            return new float4(-999,0,0,0);
        }


        if (Physics.Raycast(topLeft, Vector3.down, out hit, 1000, layer))
        {
            heights[0] = hit.point.y;
        }

        if (Physics.Raycast(topRight, Vector3.down, out hit, 1000, layer))
        {
            heights[1] = hit.point.y;
        }

        if (Physics.Raycast(bottomLeft, Vector3.down, out hit, 1000, layer))
        {
            heights[2] = hit.point.y;
        }

        if (Physics.Raycast(bottomRight, Vector3.down, out hit, 1000, layer))
        {
            heights[3] = hit.point.y;
        }

        tileCornerHieghtDictionary.Add(gridPos,heights);
        return heights;
    }

    NativeArray<bool> FrustumCulling()
    {
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(Camera.main);

        for (int i = 0; i < 6; i++) {
            frustumPlanesNative[i] = new float4(frustumPlanes[i].normal.x, frustumPlanes[i].normal.y, frustumPlanes[i].normal.z, frustumPlanes[i].distance);
        }
        int largestTileSize = lods[lods.Length - 1].visibleDistanceThreshold*2 + 1;
        int index = 0;
        NativeArray<float3> objectPositions = new NativeArray<float3>(largestTileSize * largestTileSize, Allocator.TempJob);
        NativeArray<float3> objectExtents = new NativeArray<float3>(largestTileSize * largestTileSize, Allocator.TempJob);
        NativeArray<bool> visibleTransforms = new NativeArray<bool>(largestTileSize * largestTileSize, Allocator.TempJob);

        for (int x = -lods[lods.Length - 1].visibleDistanceThreshold; x <= lods[lods.Length - 1].visibleDistanceThreshold; x++)
        {
            for (int y = -lods[lods.Length - 1].visibleDistanceThreshold; y <= lods[lods.Length - 1].visibleDistanceThreshold; y++)
            {
                objectPositions[index] = new float3(playerTransform.position.x + x * gridSize/2, playerTransform.position.y, playerTransform.position.z + y * gridSize/2);
                objectExtents[index] = new float3(gridSize/2, 2, gridSize/2);
                index++;
            }
        }

        CullingJob cullingJob = new CullingJob()
        {
            frustumPlanes = frustumPlanesNative,
            positions = objectPositions,
            extents = objectExtents,
            visibleTransforms = visibleTransforms
        };

        JobHandle cullingJobHandle = cullingJob.Schedule(visibleTransforms.Length,32);
        cullingJobHandle.Complete();
        objectPositions.Dispose();
        objectExtents.Dispose();

        return visibleTransforms;
    }

    int SmoothDensity(int lodIndex,  Vector2Int gridPos)
    {
        if(lodIndex == lods.Length-1)
        {
            return lods[lodIndex].density;
        }

        int gridDistance = Mathf.Max(Mathf.Abs(gridPos.x),Mathf.Abs(gridPos.y));
        int lodStartDistance = lodIndex == 0? 0:lods[lodIndex-1].visibleDistanceThreshold;
        int lodEndDistance = lods[lodIndex].visibleDistanceThreshold;

        return (int)Mathf.Lerp(lods[lodIndex].density, lods[lodIndex+1].density, (gridDistance - lodStartDistance)/(lodEndDistance - lodStartDistance + 1));
    }

    public void OnDestroy()
    {
        for (int i = 0; i < arrayOfLodArray.Length; i++)
        {
            arrayOfLodArray[i].Dispose();
        }

        arrayOfLodArray.Dispose();
        frustumPlanesNative.Dispose();
    }
}