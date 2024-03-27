using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using UnityEngine.Rendering.Universal;

[BurstCompile]
struct GenerateGrassMatrixJob : IJob
{
    [ReadOnly]
    public float4 cornerHeights;
    public Unity.Mathematics.Random random;
    public Vector2Int gridPos;
    public NativeArray<Matrix4x4> tileGrassInstances;
    public NativeArray<int> numOfGrass;
    public int gridSize;
    public int pivot;
    public int density;
    public int minHeight;
    public int maxHeight;

    public void Execute()
    {
        for (int i = 0; i < density; i++)
        {
            float u = random.NextUInt(1,100)/100.0f;
            float v = random.NextUInt(1,100)/100.0f;
            float height = BilinearInterpolate(u, v);

            if(height < minHeight || height > maxHeight){
                continue;
            }

            float randomRotation = random.NextFloat(0, 360);
            Quaternion rotation = Quaternion.Euler(0, randomRotation, 0);

            Vector3 worldPos = new Vector3(gridPos.x * gridSize + (u - 0.5f) * gridSize, height, gridPos.y * gridSize + (v - 0.5f) * gridSize);
            Matrix4x4 matrix = Matrix4x4.TRS(worldPos, rotation, new Vector3(1,0.7f,1));
            tileGrassInstances[pivot + i] = matrix;
            numOfGrass[0]++;
        }
    }

    private float BilinearInterpolate(float u, float v)
    {
        float top = math.lerp(cornerHeights.x, cornerHeights.y, u);
        float bottom = math.lerp(cornerHeights.z, cornerHeights.w, u);
        return math.lerp(top, bottom, v);
    }
}