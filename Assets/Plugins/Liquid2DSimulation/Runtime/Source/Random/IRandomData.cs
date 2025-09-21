
namespace Fs.Liquid2D.Utility
{
    /// <summary>
    /// 随机数据接口。
    /// 用于为目标对象提供更复杂的随机数据。并使用更多的条件来控制随机结果。
    /// 如果之用进行按权重的简单随机，可以使用 Fs.Utility.Random.Weight() 系列方法。
    /// 流程：CheckCondition()排除不满足自定义条件的。 -> GetProbability()排除未发生的。 -> GetPriority()排除低优先级的。 -> GetWeight()按权重随机。
    /// </summary>
    public interface IRandomData
    {
        /// <summary>
        /// 确认自身条件是否符合。
        /// </summary>
        /// <returns></returns>
        public bool CheckCondition(object customData = null)
        {
            // 默认实现为总是符合条件。
            // 如果需要更复杂的条件判断，可以重写此方法。
            return true;
        }

        /// <summary>
        /// 发生概率。
        /// 范围0-10000000。对应0-100%精确到小数点后7位，最小千万分之一。
        /// </summary>
        /// <returns></returns>
        public int GetProbability()
        {
            // 默认实现为10000000，表示100%概率。
            // 如果需要更复杂的概率计算，可以重写此方法。
            return 10000000;
        }
        
        /// <summary>
        /// 优先级。
        /// </summary>
        /// <returns></returns>
        public int GetPriority()
        {
            // 默认实现为0，表示无优先级。
            // 如果需要更复杂的优先级计算，可以重写此方法。
            return 0;
        }
        
        /// <summary>
        /// 权重。
        /// </summary>
        /// <returns></returns>
        public int GetWeight();
    }
}
