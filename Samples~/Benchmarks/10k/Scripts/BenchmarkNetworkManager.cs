using System;

namespace Mirror.Examples
{
    public class BenchmarkNetworkManager : NetworkManager
    {
        /// <summary>
        /// hook for benchmarking
        /// </summary>
        public Action BeforeLateUpdate;
        /// <summary>
        /// hook for benchmarking
        /// </summary>
        public Action AfterLateUpdate;


        public void LateUpdate()
        {
            BeforeLateUpdate?.Invoke();
            base.server.sceneManager.LateUpdate();
            AfterLateUpdate?.Invoke();
        }
    }
}