using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Mirage.Tests.Runtime.Host
{
    [TestFixture]
    public class NetworkManagerHudTestSetup : HostSetup<MockComponent>
    {
        GameObject gameObject;
        protected NetworkManagerHud networkManagerHud;
        public override void ExtraSetup()
        {
            gameObject = new GameObject("NetworkManagerHud", typeof(NetworkManagerHud));
            networkManagerHud = gameObject.GetComponent<NetworkManagerHud>();
            networkManagerHud.NetworkManager = manager;
            networkManagerHud.OfflineGO = new GameObject();
            networkManagerHud.OnlineGO = new GameObject();

            //Initial state in the prefab
            networkManagerHud.OfflineGO.SetActive(true);
            networkManagerHud.OnlineGO.SetActive(false);
        }

        public override void ExtraTearDown()
        {
            Object.DestroyImmediate(networkManagerHud.OfflineGO);
            Object.DestroyImmediate(networkManagerHud.OnlineGO);
            Object.DestroyImmediate(gameObject);
        }
    }

    [TestFixture]
    public class NetworkManagerHudTest : NetworkManagerHudTestSetup
    {
        [Test]
        public void OnlineSetActiveTest()
        {
            networkManagerHud.OnlineSetActive();
            Assert.That(networkManagerHud.OfflineGO.activeSelf, Is.False);
            Assert.That(networkManagerHud.OnlineGO.activeSelf, Is.True);
        }

        [Test]
        public void OfflineSetActiveTest()
        {
            networkManagerHud.OfflineSetActive();
            Assert.That(networkManagerHud.OfflineGO.activeSelf, Is.True);
            Assert.That(networkManagerHud.OnlineGO.activeSelf, Is.False);
        }



        [UnityTest]
        public IEnumerator StopButtonTest() => UniTask.ToCoroutine(async () =>
        {
            networkManagerHud.StopButtonHandler();
            Assert.That(networkManagerHud.OfflineGO.activeSelf, Is.True);
            Assert.That(networkManagerHud.OnlineGO.activeSelf, Is.False);

            await AsyncUtil.WaitUntilWithTimeout(() => !manager.IsNetworkActive);

            Assert.That(manager.IsNetworkActive, Is.False);
        });
    }

    [TestFixture]
    public class NetworkManagerHudTestNoAutoStart : NetworkManagerHudTestSetup
    {
        protected override bool AutoStartServer => false;

        [Test]
        public void StartServerOnlyButtonTest()
        {
            networkManagerHud.StartServerOnlyButtonHandler();
            Assert.That(networkManagerHud.OfflineGO.activeSelf, Is.False);
            Assert.That(networkManagerHud.OnlineGO.activeSelf, Is.True);

            Assert.That(manager.Server.Active, Is.True);
        }
    }
}
