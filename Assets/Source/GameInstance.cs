using Fs.Liquid2D;
using UnityEngine;

namespace Source
{
    public class GameInstance : MonoBehaviour
    {
        public static GameInstance Instance
        {
            get
            {
                if (!_instance)
                {
                    _instance = Object.FindFirstObjectByType<GameInstance>();

                    if (!_instance)
                    {
                        var go = new GameObject("GameInstance");
                        _instance = go.AddComponent<GameInstance>();
                        DontDestroyOnLoad(go);
                    }
                }

                return _instance;
            }
        }
        private static GameInstance _instance;
        
        protected void Awake()
        {
            ProjectSetup();
        }

        private void ProjectSetup()
        {
            // 自定义资源加载方式和销毁方式。
            // initialize custom resource loading and destruction methods.
            // カスタムリソースのロードと破棄方法を初期化します。
            Loader.InitLoaderWithPrefabSynchronous((prefab) =>
            {
                return Pool.Spawn(prefab);
            });
            
            Loader.InitLoaderWithSynchronous((path) =>
            {
                return Pool.ResourcesLoadAndSpawn(path);
            });
            
            // 初始化销毁方式。
            // initialize destruction methods.
            // 破棄方法を初期化します。
            Loader.InitDestroy((go) =>
            {
                Pool.Despawn(go);
            });
        }
    }
}