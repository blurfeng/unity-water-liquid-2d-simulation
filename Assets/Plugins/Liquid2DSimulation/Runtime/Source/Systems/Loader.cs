using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 资源加载器。用于统一管理 GameObject 加载方式和路径。
    /// 项目可以按需求调用 Init 系列方法，注入自定义加载方式。
    /// Assets loader. Used to uniformly manage GameObject loading methods and paths.
    /// The project can call the Init series of methods as needed to inject custom loading methods.
    /// アセットローダー。GameObjectのロード方法とパスを一元管理するために使用されます。
    /// プロジェクトは必要に応じてInitシリーズのメソッドを呼び出して、カスタムロード方法を注入できます。
    /// </summary>
    public static class Loader
    {
        public enum ELoadType
        {
            /// <summary>
            /// 同步加载。 // Synchronous loading. // 同期ロード。
            /// </summary>
            Synchronous,
    
            /// <summary>
            /// 异步加载。 // Asynchronous loading. // 非同期ロード。
            /// </summary>
            Asynchronous 
        }
        
        // 描述：
        // 此类用于统一管理 GameObject 加载方式和路径。
        // 提供了同步和异步加载 GameObject 的方式。项目可以根据需求选择加载方式。
        // 要求返回的 GameObject 是实例化后的 GameObject Prefab 对象，而不是预制体本身。因为项目可能使用池。
        
        // Description:
        // This class is used to uniformly manage GameObject loading methods and paths.
        // It provides both synchronous and asynchronous loading methods for GameObject. Projects can choose the loading method based on their needs.
        // The returned GameObject is required to be an instantiated GameObject Prefab object, not the prefab itself, as the project may use pooling.
        
        // 説明：
        // このクラスは、GameObjectのロード方法とパスを一元管理するために使用されます。
        // 同期および非同期のGameObjectロード方法を提供します。プロジェクトはニーズに応じてロード方法を選択できます。
        // 戻り値のGameObjectは、プールを使用する可能性があるため、プレハブ自体ではなく、インスタンス化されたGameObjectプレハブオブジェクトである必要があります。
        
        /// <summary>
        /// 加载方式。按项目需求设置。 // Loading method. Set according to project needs. // ロード方法。プロジェクトのニーズに応じて設定します。
        /// </summary>
        public static ELoadType LoadType { get; private set; } = ELoadType.Synchronous;
        
        /// <summary>
        /// 加载委托。用于按项目需求注入自定义同步加载方式。设置 LoadType 为 Synchronous 时有效。
        /// 要求返回的 GameObject 是实例化后的 GameObject Prefab 对象。而不是预制体本身。
        /// Load GameObject delegate. Used to inject custom synchronous loading methods according to project needs. Effective when LoadType is set to Synchronous.
        /// The returned GameObject is required to be an instantiated GameObject Prefab object, not the prefab itself.
        /// GameObjectをロードするデリゲート。プロジェクトのニーズに応じてカスタムの同期ロード方法を注入するために使用されます。LoadTypeがSynchronousに設定されている場合に有効です。
        /// 戻り値のGameObjectは、プレハブ自体ではなく、インスタンス化されたGameObjectプレハブオブジェクトである必要があります。
        /// </summary>
        private static Func<string, GameObject> _loadGameObject = DefaultLoad;
        
        /// <summary>
        /// 加载委托。用于按项目需求注入自定义同步加载方式。设置 LoadType 为 Asynchronous 时有效。
        /// 要求返回的 GameObject 是实例化后的 GameObject Prefab 对象。而不是预制体本身。
        /// Load GameObject delegate. Used to inject custom asynchronous loading methods according to project needs. Effective when LoadType is set to Asynchronous.
        /// The returned GameObject is required to be an instantiated GameObject Prefab object, not the prefab itself.
        /// GameObjectをロードするデリゲート。プロジェクトのニーズに応じてカスタムの非同期ロード方法を注入するために使用されます。LoadTypeがAsynchronousに設定されている場合に有効です。
        /// 戻り値のGameObjectは、プレハブ自体ではなく、インスタンス化されたGameObjectプレハブオブジェクトである必要があります。
        /// </summary>
        private static Action<string, Action<GameObject>> _loadGameObjectAsync = DefaultLoadAsync;

        /// <summary>
        /// GameObject预制体同步加载委托。用于直接传入GameObject预制体的同步加载方式。
        /// 要求返回的 GameObject 是实例化后的 GameObject Prefab 对象。
        /// Load GameObject from prefab delegate. Used to directly pass in GameObject prefab for synchronous loading.
        /// The returned GameObject is required to be an instantiated GameObject Prefab object.
        /// プレハブからGameObjectをロードするデリゲート。GameObjectプレハブを直接渡して同期ロードするために使用されます。
        /// 戻り値のGameObjectは、インスタンス化されたGameObjectプレハブオブジェクトである必要があります。
        /// </summary>
        private static Func<GameObject, GameObject> _loadGameObjectFromPrefab = DefaultLoadFromPrefab;
        
        /// <summary>
        /// GameObject预制体异步加载委托。用于直接传入GameObject预制体的异步加载方式。
        /// 要求返回的 GameObject 是实例化后的 GameObject Prefab 对象。
        /// Load GameObject from prefab delegate. Used to directly pass in GameObject prefab for asynchronous loading.
        /// The returned GameObject is required to be an instantiated GameObject Prefab object.
        /// プレハブからGameObjectをロードするデリゲート。GameObjectプレハブを直接渡して非同期ロードするために使用されます。
        /// 戻り値のGameObjectは、インスタンス化されたGameObjectプレハブオブジェクトである必要があります。
        /// </summary>
        private static Action<GameObject, Action<GameObject>> _loadGameObjectFromPrefabAsync = DefaultLoadFromPrefabAsync;
        
        /// <summary>
        /// 销毁 GameObject 委托。用于按项目需求注入自定义销毁方式。
        /// Destroy GameObject delegate. Used to inject custom destruction methods according to project needs.
        /// GameObjectを破棄するデリゲート。プロジェクトのニーズに応じてカスタムの破棄方法を注入するために使用されます。
        /// </summary>
        private static Action<GameObject> _destroyGameObject = DefaultDestroy;

        /// <summary>
        /// 加载 GameObject。
        /// Load GameObject.
        /// GameObject をロードします。
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public static void Load(string path, Action<GameObject> callback)
        {
            if (LoadType == ELoadType.Synchronous)
            {
                GameObject loaded = _loadGameObject(path);
                callback?.Invoke(loaded);
            }
            else
            {
                _loadGameObjectAsync(path, callback);
            }
        }

        /// <summary>
        /// 从预制体加载 GameObject。
        /// Load GameObject from prefab.
        /// プレハブからGameObjectをロードします。
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="callback"></param>
        public static void Load(GameObject prefab, Action<GameObject> callback)
        {
            if (LoadType == ELoadType.Synchronous)
            {
                GameObject loaded = _loadGameObjectFromPrefab(prefab);
                callback?.Invoke(loaded);
            }
            else
            {
                _loadGameObjectFromPrefabAsync(prefab, callback);
            }
        }
        
        #region Init

        /// <summary>
        /// 初始化加载器，使用同步加载方式。
        /// Init loader with synchronous loading method.
        /// ローダーを同期ロード方法で初期化します。
        /// </summary>
        /// <param name="loadFunc"></param>
        public static void InitLoaderWithSynchronous(Func<string, GameObject> loadFunc)
        {
            LoadType = ELoadType.Synchronous;

            _loadGameObject = loadFunc;
        }
        
        /// <summary>
        /// 初始化加载器，使用异步加载方式。
        /// Init loader with asynchronous loading method.
        /// ローダーを非同期ロード方法で初期化します。
        /// </summary>
        /// <param name="loadAsyncFunc"></param>
        public static void InitLoaderWithAsynchronous(Action<string, Action<GameObject>> loadAsyncFunc)
        {
            LoadType = ELoadType.Asynchronous;

            _loadGameObjectAsync = loadAsyncFunc;
        }
        
        /// <summary>
        /// 初始化加载器，使用GameObject预制体同步加载方式。
        /// Init loader with prefab synchronous loading method.
        /// プレハブを使用した同期ロード方法でローダーを初期化します。
        /// </summary>
        /// <param name="loadFromPrefabFunc"></param>
        public static void InitLoaderWithPrefabSynchronous(Func<GameObject, GameObject> loadFromPrefabFunc)
        {
            LoadType = ELoadType.Synchronous;

            _loadGameObjectFromPrefab = loadFromPrefabFunc;
        }
        
        /// <summary>
        /// 初始化加载器，使用GameObject预制体异步加载方式。
        /// Init loader with prefab asynchronous loading method.
        /// プレハブを使用した非同期ロード方法でローダーを初期化します。
        /// </summary>
        /// <param name="loadFromPrefabAsyncFunc"></param>
        public static void InitLoaderWithPrefabAsynchronous(Action<GameObject, Action<GameObject>> loadFromPrefabAsyncFunc)
        {
            LoadType = ELoadType.Asynchronous;

            _loadGameObjectFromPrefabAsync = loadFromPrefabAsyncFunc;
        }
        
        /// <summary>
        /// 默认异步加载方式：实际上调动 DefaultLoad 方法，并不是真正的异步加载。会阻塞调用线程。
        /// Default asynchronous loading method: actually calls the DefaultLoad method, not truly asynchronous. Will block the calling thread.
        /// デフォルトの非同期ロード方法：実際にはDefaultLoadメソッドを呼び出し、真の非同期ではありません。呼び出しスレッドをブロックします。
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        /// <returns></returns>
        private static void DefaultLoadAsync(string path, Action<GameObject> callback)
        {
            // 直接调用同步加载方法，并将结果传递给回调。
            // Directly call the synchronous loading method and pass the result to the callback.
            // 同期ロード方法を直接呼び出し、結果をコールバックに渡します。
            GameObject loaded = DefaultLoad(path);
            callback?.Invoke(loaded);
        }
        
        /// <summary>
        /// 默认加载方式：Resources.Load。
        /// Default loading method: Resources.Load.
        /// デフォルトのロード方法：Resources.Load。
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static GameObject DefaultLoad(string path)
        {
            var viewPrefab = Resources.Load<GameObject>(path);
            return Object.Instantiate(viewPrefab);
        }
        
        /// <summary>
        /// 默认GameObject预制体异步加载方式：实际上调用 DefaultLoadFromPrefab 方法，并不是真正的异步加载。会阻塞调用线程。
        /// Default asynchronous loading method from prefab: actually calls the DefaultLoadFromPrefab method, not truly asynchronous. Will block the calling thread.
        /// プレハブからのデフォルトの非同期ロード方法：実際にはDefaultLoadFromPrefabメソッドを呼び出し、真の非同期ではありません。呼び出しスレッドをブロックします。
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="callback"></param>
        private static void DefaultLoadFromPrefabAsync(GameObject prefab, Action<GameObject> callback)
        {
            // 直接调用同步加载方法，并将结果传递给回调。
            GameObject loaded = DefaultLoadFromPrefab(prefab);
            callback?.Invoke(loaded);
        }
        
        /// <summary>
        /// 默认GameObject预制体加载方式：直接实例化预制体。
        /// Default loading method from prefab: directly instantiate the prefab.
        /// プレハブからのデフォルトのロード方法：プレハブを直接インスタンス化します。
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        private static GameObject DefaultLoadFromPrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogError("Prefab is null, cannot instantiate.");
                return null;
            }
            
            return Object.Instantiate(prefab);
        }

        #endregion

        #region Destroy

        /// <summary>
        /// 销毁 GameObject。
        /// Destroy GameObject.
        /// GameObject を破棄します。
        /// </summary>
        /// <param name="go"></param>
        public static void Destroy(GameObject go)
        {
            _destroyGameObject.Invoke(go);
        }
        
        /// <summary>
        /// 默认销毁方式：Object.Destroy。
        /// Default destruction method: Object.Destroy.
        /// デフォルトの破棄方法：Object.Destroy。
        /// </summary>
        /// <param name="go"></param>
        private static void DefaultDestroy(GameObject go)
        {
            if (!go) return;
            Object.Destroy(go);
        }
        
        /// <summary>
        /// 初始化销毁方式。
        /// Init destruction method.
        /// 破棄方法を初期化します。
        /// </summary>
        /// <param name="destroyFunc"></param>
        public static void InitDestroy(Action<GameObject> destroyFunc)
        {
            _destroyGameObject = destroyFunc ?? DefaultDestroy;
        }

        #endregion
    }
}