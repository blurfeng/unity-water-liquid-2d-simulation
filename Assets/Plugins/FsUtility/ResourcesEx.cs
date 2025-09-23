using System.Collections.Generic;
using UnityEngine;

namespace Fs.Utility
{
    /// <summary>
    /// 自建加载类扩展。
    /// 会将加载过的 Prefab 缓存。
    /// </summary>
    public static class ResourcesEx
    {
        private static Dictionary<string, GameObject> _gameObjectCache = new Dictionary<string, GameObject>();
    
        public static GameObject LoadGameObject(string resourcePath, bool cache = true)
        {
            GameObject prefab = null;
            bool get = false;
            
            if (cache)
            {
                get = _gameObjectCache.TryGetValue(resourcePath, out prefab);
            }
            
            if (!get)
            {
                prefab = Resources.Load<GameObject>(resourcePath);
                if (prefab != null && cache)
                {
                    _gameObjectCache.Add(resourcePath, prefab);
                }
            }
            
            if (prefab == null)
            {
                Debug.LogError($"ResourcesEx:LoadGameObjectAndInstantiate: Path: [ {resourcePath} ] not found!");
                return null;
            }
            
            return prefab;
        }
        
        /// <summary>
        /// 此方法直接调用 Instantiate 来实例化GameObject。
        /// 如果要使用对象池来实例化 GameObject，请使用自己的池相关方法。
        /// </summary>
        /// <param name="resourcePath"></param>
        /// <param name="cache"></param>
        /// <returns></returns>
        public static GameObject LoadGameObjectAndInstantiate(string resourcePath, bool cache = true)
        {
            return Object.Instantiate(LoadGameObject(resourcePath, cache));
        }
    
        public static Sprite LoadSprite(string resourcePath)
        {
            return Resources.Load<Sprite>(resourcePath);;
        }
    }
}