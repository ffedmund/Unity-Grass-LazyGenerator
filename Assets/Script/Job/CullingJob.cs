using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;
using System.Numerics;

//Original Sample Code by Enno, https://ennogames.com/blog/frustum-culling-with-unity-jobs

[BurstCompile]
struct CullingJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float4> frustumPlanes;
    [ReadOnly] public NativeArray<float3> positions;
    [ReadOnly] public NativeArray<float3> extents;
    public NativeArray<bool> visibleTransforms;


    public void Execute(int index) {
        visibleTransforms[index] = FrustumContainsBox(positions[index] - extents[index], positions[index] + extents[index]);
    }


    float DistanceToPlane( float4 plane, float3 position )
    {
        return math.dot(plane.xyz, position) + plane.w;
    }

    bool FrustumContainsBox(float3 bboxMin, float3 bboxMax) {
        float3 pos;

        for (int i = 0; i < 6; i++) {
            pos.x = frustumPlanes[i].x > 0 ? bboxMax.x : bboxMin.x;
            pos.y = frustumPlanes[i].y > 0 ? bboxMax.y : bboxMin.y;
            pos.z = frustumPlanes[i].z > 0 ? bboxMax.z : bboxMin.z;

            if (DistanceToPlane(frustumPlanes[i], pos) < 0) {
                return false;
            }
        }

        return true;
    }
}