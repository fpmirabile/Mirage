using System.Collections;
using UnityEngine;

namespace Mirage.Examples.Light
{
    public class Health : NetworkBehaviour
    {
        [SyncVar] public int health = 10;
        private void Awake()
        {
            NetIdentity.OnStartServer.AddListener(OnStartServer);
            NetIdentity.OnStopServer.AddListener(OnStopServer);
        }

        public void OnStartServer()
        {
            StartCoroutine(UpdateHealth());
        }

        public void OnStopServer()
        {
            StopAllCoroutines();
        }

        internal IEnumerator UpdateHealth()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(0f, 5f));
                health = (health + 1) % 10;
            }
        }
    }
}
