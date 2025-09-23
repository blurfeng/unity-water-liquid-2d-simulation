
namespace Fs.Liquid2D.Utility
{
    /// <summary>
    /// 随机数据接口。
    /// 用于为目标对象提供更复杂的随机数据。并使用更多的条件来控制随机结果。
    /// 如果之用进行按权重的简单随机，可以使用 Fs.Utility.Random.Weight() 系列方法。
    /// 流程：CheckCondition()排除不满足自定义条件的。 -> GetProbability()排除未发生的。 -> GetPriority()排除低优先级的。 -> GetWeight()按权重随机。
    /// 
    /// Random data interface.
    /// Used to provide more complex random data for target objects. And use more conditions to control random results.
    /// If only simple weight-based randomization is needed, you can use Fs.Utility.Random.Weight() series methods.
    /// Process: CheckCondition() excludes those that don't meet custom conditions. -> GetProbability() excludes those that don't occur. -> GetPriority() excludes low priority ones. -> GetWeight() random by weight.
    /// 
    /// ランダムデータインターフェース。
    /// ターゲットオブジェクトにより複雑なランダムデータを提供し、より多くの条件を使用してランダム結果を制御します。
    /// 単純な重みベースのランダム化のみが必要な場合は、Fs.Utility.Random.Weight()シリーズメソッドを使用できます。
    /// プロセス：CheckCondition()でカスタム条件を満たさないものを除外 -> GetProbability()で発生しないものを除外 -> GetPriority()で低優先度のものを除外 -> GetWeight()で重みによりランダム化。
    /// </summary>
    public interface IRandomData
    {
        /// <summary>
        /// 确认自身条件是否符合。
        /// Check if its own conditions are met.
        /// 自身の条件が満たされているかどうかを確認します。
        /// </summary>
        /// <returns></returns>
        public bool CheckCondition(object customData = null)
        {
            // 默认实现为总是符合条件。如果需要更复杂的条件判断，可以重写此方法。
            // The default implementation always meets the conditions. If more complex condition judgment is needed, you can override this method.
            // デフォルトの実装では常に条件を満たします。より複雑な条件判断が必要な場合は、このメソッドをオーバーライドできます。
            return true;
        }

        /// <summary>
        /// 发生概率。范围0-10000000。对应0-100%精确到小数点后7位，最小千万分之一。
        /// Get occurrence probability. Range 0-10000000. Corresponds to 0-100% accurate to 7 decimal places, minimum one in ten million.
        /// 発生確率。範囲0-10000000。0-100%に対応し、小数点以下7桁まで正確、最小で1000万分の1。
        /// </summary>
        /// <returns></returns>
        public int GetProbability()
        {
            // 默认实现为10000000，表示100%概率。如果需要更复杂的概率计算，可以重写此方法。
            // The default implementation is 10000000, indicating a 100% probability. If more complex probability calculations are needed, you can override this method.
            // デフォルトの実装は10000000で、100%の確率を示します。より複雑な確率計算が必要な場合は、このメソッドをオーバーライドできます。
            return 10000000;
        }
        
        /// <summary>
        /// 优先级。
        /// Priority.
        /// 優先度。
        /// </summary>
        /// <returns></returns>
        public int GetPriority()
        {
            // 默认实现为0，表示无优先级。如果需要更复杂的优先级计算，可以重写此方法。
            // The default implementation is 0, indicating no priority. If more complex priority calculations are needed, you can override this method.
            // デフォルトの実装は0で、優先度がないことを示します。より複雑な優先度計算が必要な場合は、このメソッドをオーバーライドできます。
            return 0;
        }
        
        /// <summary>
        /// 权重。
        /// Weight.
        /// 重み。
        /// </summary>
        /// <returns></returns>
        public int GetWeight();
    }
}