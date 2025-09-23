using System;
using UnityEngine;

namespace Fs.Liquid2D.Localization
{
    /// <summary>
    /// 本地化工具提示特性，支持中、英、日三种语言的工具提示。
    /// 根据系统语言自动选择合适的语言显示。
    /// Localization tooltip attribute that supports Chinese, English, and Japanese tooltips.
    /// Automatically selects the appropriate language based on the system language.
    /// ローカル化ツールチップ属性は、中国語、英語、日本語のツールチップをサポートしています。
    /// システム言語に基づいて適切な言語を自動的に選択します。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public class LocalizationTooltipAttribute : TooltipAttribute
    {
        public string Chinese { get; private set; }
        public string English { get; private set; }
        public string Japanese { get; private set; }
        
        public LocalizationTooltipAttribute(string chinese, string english = "", string japanese = "") 
            : base(GetLocalizedTooltipStatic(chinese, english, japanese))
        {
            Chinese = chinese ?? "";
            English = english ?? "";
            Japanese = japanese ?? "";
        }
        
        /// <summary>
        /// 获取当前语言对应的工具提示文本。
        /// Get the tooltip text corresponding to the current language.
        /// 現在の言語に対応するツールチップテキストを取得します。
        /// </summary>
        private static string GetLocalizedTooltipStatic(string chinese, string english, string japanese)
        {
          SystemLanguage systemLanguage = Application.systemLanguage;

          switch (systemLanguage)
          {
            case SystemLanguage.Chinese:
            case SystemLanguage.ChineseSimplified:
            case SystemLanguage.ChineseTraditional:
              return !string.IsNullOrEmpty(chinese) ? chinese : GetFallbackTooltipStatic(chinese, english, japanese);

            case SystemLanguage.Japanese:
              return !string.IsNullOrEmpty(japanese) ? japanese : GetFallbackTooltipStatic(chinese, english, japanese);

            default:
              return !string.IsNullOrEmpty(english) ? english : GetFallbackTooltipStatic(chinese, english, japanese);
          }
        }
        
        private static string GetFallbackTooltipStatic(string chinese, string english, string japanese)
        {
            if (!string.IsNullOrEmpty(english)) return english;
            if (!string.IsNullOrEmpty(chinese)) return chinese;
            if (!string.IsNullOrEmpty(japanese)) return japanese;
            return "";
        }
    }
}