using System;
using Lean.Pool;
using UnityEngine;
using Fs.Utility;

namespace Source
{
    /// <summary>
    /// 对象池。需要池化的对象通过此类来生成和销毁。
    /// object pool. Objects that need to be pooled are created and destroyed through this class.
    /// オブジェクトプール。プール化が必要なオブジェクトはこのクラスを通じて生成および破棄される。
    /// </summary>
    public static class Pool
    {
        /// <summary>
        /// 加载并生成GameObject。 // load and spawn GameObject. // GameObjectをロードして生成する。
        /// </summary>
        /// <param name="resourcePath"></param>
        /// <returns></returns>
        public static GameObject ResourcesLoadAndSpawn(string resourcePath)
        {
            return Spawn(ResourcesEx.LoadGameObject(resourcePath));
        }

        /// <summary>
        /// 生成GameObject的实例。 // Spawn an instance of a GameObject. // GameObjectのインスタンスを生成する。
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="parent"></param>
        /// <param name="worldPositionStays"></param>
        /// <param name="resetScale"></param>
        /// <returns></returns>
        public static GameObject Spawn(GameObject prefab, Transform parent = null, bool worldPositionStays = false, bool resetScale = true)
        {
            var instance = LeanPool.Spawn(prefab, parent, worldPositionStays);
            
            // 重置实例化对象的 Transform。 // Reset the Transform of the instantiated object. // インスタンス化されたオブジェクトのTransformをリセットします。
            Transform transform = instance.transform;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (resetScale)
            {
                transform.localScale = prefab.transform.localScale;
            }
            return instance;
        }
    
        /// <summary>
        /// 返还GameObject到对象池。 // Return GameObject to the object pool. // GameObjectをオブジェクトプールに返す。
        /// </summary>
        /// <param name="go"></param>
        public static void Despawn(GameObject go)
        {
            LeanGameObjectPool pool = null;
            if (LeanGameObjectPool.TryFindPoolByClone(go, ref pool))
            {
                LeanPool.Despawn(go);
            }
            else
            {
                // 场景原生对象直接销毁。 // Native scene objects are destroyed directly. // シーンのネイティブオブジェクトは直接破棄されます。
                GameObject.Destroy(go); 
            }
        }
    }
}