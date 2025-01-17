using System;
using Mirage.Tests;
using NSubstitute;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Mirage.SocketLayer.Tests.PeerTests
{
    [Category("SocketLayer"), Description("tests for Peer that apply to both server and client")]
    public class PeerTest : PeerTestBase
    {
        [Test]
        public void ThrowIfSocketIsNull()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new Peer(null, Substitute.For<IDataHandler>(), new Config(), Substitute.For<ILogger>());
            });
            var expected = new ArgumentNullException("socket");
            Assert.That(exception, Has.Message.EqualTo(expected.Message));
        }
        [Test]
        public void ThrowIfDataHandlerIsNull()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
            {
                _ = new Peer(Substitute.For<ISocket>(), null, new Config(), Substitute.For<ILogger>());
            });
            var expected = new ArgumentNullException("dataHandler");
            Assert.That(exception, Has.Message.EqualTo(expected.Message));
        }
        [Test]
        public void DoesNotThrowIfConfigIsNull()
        {
            Assert.DoesNotThrow(() =>
            {
                _ = new Peer(Substitute.For<ISocket>(), Substitute.For<IDataHandler>(), null, Substitute.For<ILogger>());
            });
        }
        [Test]
        public void DoesNotThrowIfLoggerIsNull()
        {
            Assert.DoesNotThrow(() =>
            {
                _ = new Peer(Substitute.For<ISocket>(), Substitute.For<IDataHandler>(), new Config(), null);
            });
        }

        [Test]
        public void CloseShouldThrowIfNoActive()
        {
            LogAssert.Expect(LogType.Warning, "Peer is not active");
            peer.Close();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CloseShouldCallSocketClose()
        {
            // activate peer
            peer.Bind(default);
            // close peer
            peer.Close();
            socket.Received(1).Close();
        }

        [Test]
        public void IgnoresMessageThatIsTooShort()
        {
            peer.Bind(TestEndPoint.CreateSubstitute());

            Action<IConnection> connectAction = Substitute.For<Action<IConnection>>();
            peer.OnConnected += connectAction;

            socket.SetupReceiveCall(new byte[1] {
                (byte)UnityEngine.Random.Range(0, 255),
            });

            peer.Update();

            // server does nothing for invalid
            socket.DidNotReceiveWithAnyArgs().Send(default, default, default);
            connectAction.DidNotReceiveWithAnyArgs().Invoke(default);
        }

        [Test]
        public void ThrowsIfSocketGivesLengthThatIsTooHigh()
        {
            peer.Bind(TestEndPoint.CreateSubstitute());

            Action<IConnection> connectAction = Substitute.For<Action<IConnection>>();
            peer.OnConnected += connectAction;

            const int aboveMTU = 5000;
            socket.SetupReceiveCall(new byte[1000], length: aboveMTU);

            IndexOutOfRangeException exception = Assert.Throws<IndexOutOfRangeException>(() =>
            {
                peer.Update();
            });

            Assert.That(exception, Has.Message.EqualTo($"Socket returned length above MTU. MaxPacketSize:{config.MaxPacketSize} length:{aboveMTU}"));
        }

        [Test]
        [Repeat(10)]
        public void IgnoresRandomData()
        {
            peer.Bind(TestEndPoint.CreateSubstitute());

            Action<IConnection> connectAction = Substitute.For<Action<IConnection>>();
            peer.OnConnected += connectAction;

            IEndPoint endPoint = TestEndPoint.CreateSubstitute();

            // 2 is min length of a message
            byte[] randomData = new byte[UnityEngine.Random.Range(2, 20)];
            for (int i = 0; i < randomData.Length; i++)
            {
                randomData[i] = (byte)UnityEngine.Random.Range(0, 255);
            }
            socket.SetupReceiveCall(randomData);

            peer.Update();

            // server does nothing for invalid
            socket.DidNotReceiveWithAnyArgs().Send(default, default, default);
            connectAction.DidNotReceiveWithAnyArgs().Invoke(default);
        }

        [Test]
        public void StopsReceiveLoopIfClosedByMessageHandler()
        {
            // todo implement
            Assert.Ignore("Not Implemented");

            // if a receive handler calls close while in receive loop we should stop the loop before calling poll again
            // if we dont we will get object disposed errors
        }
    }
}
