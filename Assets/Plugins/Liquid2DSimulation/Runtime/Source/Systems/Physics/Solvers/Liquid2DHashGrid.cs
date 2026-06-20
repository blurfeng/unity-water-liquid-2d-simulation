using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
// 类型别名：避免 `using static math` 引入的 floatN/intN(...) 方法在类型位置遮蔽对应类型（CS0119）。
// Type aliases: prevent floatN/intN(...) methods from `using static math` shadowing the corresponding types (CS0119).
// 型エイリアス：`using static math` の方法が型位置で型を隠すのを防ぐ（CS0119）。
using float2 = Unity.Mathematics.float2;
using int2 = Unity.Mathematics.int2;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 均匀空间哈希网格（计数排序）。把活动粒子按 cell（边长=核半径 h）分桶，邻居查询遍历 3×3 cell。
    /// 采用哈希到固定大小表 + 候选 cell 坐标精确过滤，避免哈希碰撞导致的重复计数。
    /// Uniform spatial hash grid (counting sort). Buckets active particles by cell (size = kernel radius h); neighbor
    /// queries scan 3×3 cells. Uses fixed-size-table hashing + exact candidate-cell filtering to avoid double counting
    /// from hash collisions.
    /// 一様空間ハッシュグリッド（計数ソート）。アクティブ粒子を cell（辺長=核半径 h）でバケット化し、3×3 cell を走査。
    /// 固定サイズ表へのハッシュ + 候補 cell 座標の厳密フィルタで、ハッシュ衝突による重複計数を回避。
    /// </summary>
    public struct Liquid2DHashGrid
    {
        public NativeArray<int> CellStart;   // 长度 tableSize+1。 // length tableSize+1. // 長さ tableSize+1。
        public NativeArray<int> SortedSlots; // 长度 activeCount，按桶排序的 slot id。 // length activeCount, slot ids sorted by bucket. // 長さ activeCount。
        public int TableSize;
        public float InvCellSize;

        public bool IsCreated => CellStart.IsCreated;

        public static int2 CellCoord(float2 p, float invCell) => (int2)floor(p * invCell);

        // 此哈希必须与 GPU 侧 Liquid2DSph.compute 的 HashCell 逐位一致（同算法前提），改动需同步两处。
        // This hash must stay bit-identical to HashCell in the GPU-side Liquid2DSph.compute; change both together.
        // このハッシュは GPU 側 Liquid2DSph.compute の HashCell とビット一致させること。
        public static int Hash(int2 c, int tableSize)
        {
            unchecked
            {
                int h = (c.x * 73856093) ^ (c.y * 19349663);
                int m = h % tableSize;
                return m < 0 ? m + tableSize : m;
            }
        }
    }

    /// <summary>
    /// 单线程（Burst）构建哈希网格：计数排序填充 cellStart 与 sortedSlots。
    /// Single-threaded (Burst) hash-grid build: counting sort fills cellStart and sortedSlots.
    /// シングルスレッド（Burst）ハッシュグリッド構築：計数ソートで cellStart と sortedSlots を充填。
    /// </summary>
    [BurstCompile]
    public struct BuildHashGridJob : IJob
    {
        [ReadOnly] public NativeArray<float2> Predicted;
        [ReadOnly] public NativeArray<int> ActiveIndices;
        public int ActiveCount;
        public int TableSize;
        public float InvCellSize;

        [WriteOnly] public NativeArray<int> CellStart;   // tableSize+1
        [WriteOnly] public NativeArray<int> SortedSlots; // activeCount

        public void Execute()
        {
            var counts = new NativeArray<int>(TableSize, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var bucketOfK = new NativeArray<int>(ActiveCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (int k = 0; k < ActiveCount; k++)
            {
                int slot = ActiveIndices[k];
                int2 cc = Liquid2DHashGrid.CellCoord(Predicted[slot], InvCellSize);
                int b = Liquid2DHashGrid.Hash(cc, TableSize);
                bucketOfK[k] = b;
                counts[b] = counts[b] + 1;
            }

            // 前缀和 → 每个桶的起始写位置。 // Prefix sum → start write position per bucket. // 前置和 → 各バケットの開始書込位置。
            int acc = 0;
            var cursor = new NativeArray<int>(TableSize, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int b = 0; b < TableSize; b++)
            {
                CellStart[b] = acc;
                cursor[b] = acc;
                acc += counts[b];
            }
            CellStart[TableSize] = acc;

            for (int k = 0; k < ActiveCount; k++)
            {
                int b = bucketOfK[k];
                int pos = cursor[b];
                cursor[b] = pos + 1;
                SortedSlots[pos] = ActiveIndices[k];
            }

            counts.Dispose();
            bucketOfK.Dispose();
            cursor.Dispose();
        }
    }
}
