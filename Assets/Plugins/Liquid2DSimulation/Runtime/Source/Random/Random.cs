using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fs.Liquid2D.Utility
{
    /// <summary>
    /// 提供随机相关的工具方法。
    /// 比如可以通过权重配置随机选取一个对象。
    /// 如果没有复杂的随机需求，可以直接使用 UnityEngine.Random 类。
    /// Provides random-related utility methods.
    /// For example, you can randomly select an object through weight configuration.
    /// If there are no complex random requirements, you can directly use the UnityEngine.Random class.
    /// ランダム関連のユーティリティメソッドを提供。
    /// 例えば、重み設定によりオブジェクトをランダムに選択できます。
    /// 複雑なランダム要件がない場合は、UnityEngine.Randomクラスを直接使用できます。
    /// </summary>
    public static class Random
    {
        #region Weight 按权重随机 // Weight-based randomization // 重みベースのランダム化
        
        /// <summary>
        /// 通过权重随机，获取一个对象。
        /// Get an object through weight-based randomization.
        /// 重みベースのランダム化によりオブジェクトを取得。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="getWeight">获取权重方法。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T Weight<T>(IEnumerable<T> items, Func<T, int> getWeight)
        {
            return WeightInternal(items, getWeight);
        }
        
        /// <summary>
        /// 通过权重随机，获取一个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="getWeight">获取权重方法。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T RandomWeight<T>(this IEnumerable<T> items, Func<T, int> getWeight)
        {
            return WeightInternal(items, getWeight);
        }
        
        /// <summary>
        /// 通过权重随机，获取一个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T Weight<T>(IEnumerable<T> items) where T : IRandomData
        {
            // 方便实现 IRandomData 接口的对象调用 Weight 方法。
            return WeightInternal(items, item => item.GetWeight());
        }
        
        /// <summary>
        /// 通过权重随机，获取一个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T RandomWeight<T>(this IEnumerable<T> items) where T : IRandomData
        {
            // 方便实现 IRandomData 接口的对象调用 Weight 方法。
            return WeightInternal(items, item => item.GetWeight());
        }
        
        /// <summary>
        /// 通过权重随机，获取多个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="getWeight">获取权重方法。</param>
        /// <param name="count">获取数量。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> Weight<T>(IEnumerable<T> items, Func<T, int> getWeight, int count)
        {
            return WeightManyInternal(items, getWeight, count);
        }
        
        /// <summary>
        /// 通过权重随机，获取多个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="getWeight">获取权重方法。</param>
        /// <param name="count">获取数量。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> RandomWeight<T>(this IEnumerable<T> items, Func<T, int> getWeight, int count)
        {
            return WeightManyInternal(items, getWeight, count);
        }
        
        /// <summary>
        /// 通过权重随机，获取多个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="count">获取数量。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> Weight<T>(IEnumerable<T> items,int count) where T : IRandomData
        {
            // 方便实现 IRandomData 接口的对象调用 WeightMany 方法。
            return WeightManyInternal(items, item => item.GetWeight(), count);
        }
        
        /// <summary>
        /// 通过权重随机，获取多个对象。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="count">获取数量。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static List<T> RandomWeight<T>(this IEnumerable<T> items,int count) where T : IRandomData
        {
            // 方便实现 IRandomData 接口的对象调用 WeightMany 方法。
            return WeightManyInternal(items, item => item.GetWeight(), count);
        }
        
        private static T WeightInternal<T>(IEnumerable<T> items, Func<T, int> getWeight)
        {
            // 确认参数合法。
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (getWeight == null) throw new ArgumentNullException(nameof(getWeight));
            
            // Tips: 多次遍历。创建 Array 会占用内存但提高了多次遍历的性能。
            // 顺序是否固定由具体传入的集合决定。比如 List<T> 会保持插入顺序，而 HashSet<T> 则不保证顺序。
            var itemArray = items.ToArray();
            if (itemArray.Length == 0)
            {
                Debug.LogWarning("对象列表为空，无法进行随机。");
                return default(T);
            }
            
            // 只有一个对象，直接返回。
            if (itemArray.Length == 1)
            {
                return itemArray[0];
            }
            
            // 计算权重总和。
            int weightTotal = 0;
            for (int i = 0; i < itemArray.Length; i++)
            {
                weightTotal += getWeight(itemArray[i]);
            }
            
            // 在权重总和大于 0 的情况下进行随机。
            if (weightTotal > 0)
            {
                int randomNum = UnityEngine.Random.Range(1, weightTotal + 1);
            
                int cumulative = 0;
            
                // 确认 randomNum 命中了哪个区段。这里通过按顺序累加权重来判断命中区段。
                for (int i = 0; i < itemArray.Length; i++)
                {
                    cumulative += getWeight(itemArray[i]);
                    if (randomNum <= cumulative)
                    {
                        return itemArray[i];
                    }
                }
            }
            else
            {
                Debug.LogWarning($"权重总和必须大于 0。 WeightTotal: {weightTotal}");
            }

            Debug.LogError("权重逻辑错误，返回第一个可用对象。");
            return itemArray.First();
        }
        
        private static List<T> WeightManyInternal<T>(IEnumerable<T> items, Func<T, int> getWeight, int count)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (getWeight == null) throw new ArgumentNullException(nameof(getWeight));

            // 转为 List<T> 以便多次遍历。并确认参数合法。
            var itemList = items.ToList();
            if (itemList.Count == 0 || count <= 0)
            {
                Debug.LogWarning("对象列表为空或请求的数量小于等于 0。");
                return itemList;
            }
            if (count >= itemList.Count)
            {
                Debug.LogWarning($"请求数量 {count} 大于可用对象数量 {itemList.Count}，将返回所有对象。");
                return itemList;
            }

            // 随机获取指定数量的对象。
            List<T> result = new List<T>(count);
            for (int pick = 0; pick < count; pick++)
            {
                // 计算总权重。随着每次随机，itemList 会减少，所以需要重新计算。
                int currentWeightTotal = itemList.Sum(getWeight);
                
                // 当权重总和为 0 时，无法继续随机。
                if (currentWeightTotal <= 0)
                {
                    Debug.LogWarning($"随机过程中有权重总和为 0 的情况，无法继续随机。当前 Pick: {pick}");
                    break;
                }
                
                // 生成一个随机数，范围从 1 到当前权重总和。并获取命中对象。
                int randomNum = UnityEngine.Random.Range(1, currentWeightTotal + 1);
                int cumulative = 0;
                for (int i = 0; i < itemList.Count; i++)
                {
                    cumulative += getWeight(itemList[i]);
                    if (randomNum <= cumulative)
                    {
                        result.Add(itemList[i]);
                        itemList.RemoveAt(i);
                        break;
                    }
                }
            }
            
            return result;
        }

        /// <summary>
        /// 复杂的随机选择。可以根据自定义条件、概率和优先级来筛选对象。
        /// 必须实现 IRandomData 接口的对象才能使用。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="customData">自定义数据包，用于 CheckCondition 方法。比如你当前游戏的状态，环境等因素。当然你也可以自己存储静态数据包字段并在 CheckCondition 方法内获取。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static T Complex<T>(IEnumerable<T> items, object customData = null) where T : IRandomData
        {
            return ComplexInternal(items, customData);
        }
        
        /// <summary>
        /// 复杂的随机选择。可以根据自定义条件、概率和优先级来筛选对象。
        /// 必须实现 IRandomData 接口的对象才能使用。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="customData">自定义数据包，用于 CheckCondition 方法。比如你当前游戏的状态，环境等因素。当然你也可以自己存储静态数据包字段并在 CheckCondition 方法内获取。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static T RandomComplex<T>(this IEnumerable<T> items, object customData = null) where T : IRandomData
        {
            return ComplexInternal(items, customData);
        }
        
        /// <summary>
        /// 复杂的随机选择。可以根据自定义条件、概率和优先级来筛选对象。
        /// 必须实现 IRandomData 接口的对象才能使用。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="count">获取数量。</param>
        /// <param name="customData">自定义数据包，用于 CheckCondition 方法。比如你当前游戏的状态，环境等因素。当然你也可以自己存储静态数据包字段并在 CheckCondition 方法内获取。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static List<T> Complex<T>(IEnumerable<T> items, int count, object customData = null) where T : IRandomData
        {
            return ComplexManyInternal(items, count, customData);
        }
        
        /// <summary>
        /// 复杂的随机选择。可以根据自定义条件、概率和优先级来筛选对象。
        /// 必须实现 IRandomData 接口的对象才能使用。
        /// </summary>
        /// <param name="items">对象容器。</param>
        /// <param name="count">获取数量。</param>
        /// <param name="customData">自定义数据包，用于 CheckCondition 方法。比如你当前游戏的状态，环境等因素。当然你也可以自己存储静态数据包字段并在 CheckCondition 方法内获取。</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static List<T> RandomComplex<T>(this IEnumerable<T> items, int count, object customData = null) where T : IRandomData
        {
            return ComplexManyInternal(items, count, customData);
        }
        
        private static T ComplexInternal<T>(IEnumerable<T> items, object customData = null) where T : IRandomData
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            
            // 自定义条件筛选。玩家可以自定义一个对象的 CheckCondition 方法，来按需求判断是否满足条件。
            // 例如：可以根据玩家的状态、游戏进度等来决定是否满足条件。
            var candidates = items.Where(x => x.CheckCondition(customData)).ToList();
            if (candidates.Count == 0) return default;

            // 发生概率筛选。玩家可以为每个对象定义一个 GetProbability 方法，来决定该对象是否会发生。
            // 例如：对象是一个事件，GetProbability 可以返回该事件发生的概率。
            candidates = candidates.Where(x =>
            {
                int prob = x.GetProbability();
                if (prob <= 0) return false;
                if (prob >= 10000000) return true;
                int rand = UnityEngine.Random.Range(1, 10000001);
                return rand <= prob;
            }).ToList();
            if (candidates.Count == 0) return default;
            if (candidates.Count == 1) return candidates[0];

            // 优先级筛选。在多个候选对象中，可能有些对象的优先级更高。
            // 例如：当多个事件发生时，可能有些事件更重要。
            int maxPriority = candidates.Max(x => x.GetPriority());
            candidates = candidates.Where(x => x.GetPriority() == maxPriority).ToList();
            if (candidates.Count == 0) return default;
            if (candidates.Count == 1) return candidates[0];

            // 权重随机。在剩余的候选对象中，使用权重随机选择一个。
            return Weight(candidates);
        }
        
        private static List<T> ComplexManyInternal<T>(IEnumerable<T> items, int count, object customData = null) where T : IRandomData
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (count <= 0) throw new ArgumentException("Count must be greater than 0.", nameof(count));
            
            // 自定义条件筛选。
            var candidates = items.Where(x => x.CheckCondition(customData)).ToList();
            if (candidates.Count == 0) return null;
            if (candidates.Count <= count) return candidates;
            
            // 发生概率筛选。
            candidates = candidates.Where(x =>
            {
                int prob = x.GetProbability();
                if (prob <= 0) return false;
                if (prob >= 10000000) return true;
                int rand = UnityEngine.Random.Range(1, 10000001);
                return rand <= prob;
            }).ToList();
            if (candidates.Count == 0) return null;
            if (candidates.Count <= count) return candidates;
            
            // 优先级筛选。从最低优先级开始排除，直到剩余数量小于等于 count。
            int priorityMax = candidates.Max(x => x.GetPriority());
            int priorityMin = candidates.Min(x => x.GetPriority());
            if (priorityMax != priorityMin)
            {
                while (candidates.Count > count)
                {
                    int priority = candidates.Min(x => x.GetPriority());
                    
                    // 所有候选对象优先级相同，无法进一步筛选。
                    if (priority == priorityMax) break;
                    
                    candidates.RemoveAll(x => x.GetPriority() == priority && candidates.Count > count);
                }
            }
            if (candidates.Count == 0) return null;
            if (candidates.Count <= count) return candidates;
            
            // 权重随机。
            return Weight(candidates, count);
        }
        
        #endregion
    }
}