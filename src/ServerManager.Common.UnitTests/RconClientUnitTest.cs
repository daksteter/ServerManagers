using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ServerManagerTool.Common.Lib;

namespace ServerManager.Common.Tests
{
    /// <summary>
    /// <see cref="RconClient"/> against a fake RCON server on the loopback address that answers like an ASA server
    /// (one reply per command, auth reply ID -1 and a closed connection for a wrong password).
    /// </summary>
    [TestClass]
    public class RconClientUnitTest
    {
        private const string Password = "secret";

        [TestMethod]
        public void Connect_RightPassword_SendsCommandsAndReturnsReplies()
        {
            using var server = new FakeRconServer();
            using var rcon = RconClient.Connect(server.EndPoint, Password, 5000);

            Assert.IsTrue(rcon.IsConnected);
            Assert.AreEqual("No Players Connected \n", rcon.SendCommand("listplayers"));
            Assert.AreEqual("Server received, But no response!! \n", rcon.SendCommand("getchat"));
            Assert.AreEqual("No Players Connected \n", rcon.SendCommand("listplayers"));
        }

        [TestMethod]
        public void Connect_WrongPassword_ThrowsRconAuthenticationException()
        {
            using var server = new FakeRconServer();

            Assert.ThrowsExactly<RconAuthenticationException>(() => RconClient.Connect(server.EndPoint, "wrong", 5000));
        }

        [TestMethod]
        public void SendCommand_ReplyLargerThanFourKilobytes_ReturnsAllOfIt()
        {
            using var server = new FakeRconServer();
            using var rcon = RconClient.Connect(server.EndPoint, Password, 5000);

            Assert.AreEqual(FakeRconServer.BigReply, rcon.SendCommand("big"));
        }

        [TestMethod]
        public void SendCommand_ReplyArrivesInFragments_ReturnsAllOfIt()
        {
            using var server = new FakeRconServer();
            using var rcon = RconClient.Connect(server.EndPoint, Password, 5000);

            Assert.AreEqual(FakeRconServer.BigReply, rcon.SendCommand("fragmented"));
            Assert.AreEqual("No Players Connected \n", rcon.SendCommand("listplayers"));
        }

        [TestMethod]
        public void SendCommand_ReplySplitOverTwoPackets_JoinsThem()
        {
            using var server = new FakeRconServer();
            using var rcon = RconClient.Connect(server.EndPoint, Password, 5000);

            Assert.AreEqual("first part, second part", rcon.SendCommand("split"));
        }

        [TestMethod]
        public void SendCommand_NoReply_TimesOutAndClosesTheConnection()
        {
            using var server = new FakeRconServer();
            using var rcon = RconClient.Connect(server.EndPoint, Password, 500);

            Assert.Throws<IOException>(() => rcon.SendCommand("silent"));
            Assert.IsFalse(rcon.IsConnected);
            Assert.ThrowsExactly<ObjectDisposedException>(() => rcon.SendCommand("listplayers"));
        }

        [TestMethod]
        public void SendCommand_FromSeveralThreads_SendsOneCommandAtATime()
        {
            using var server = new FakeRconServer { ReplyDelay = 20 };
            using var rcon = RconClient.Connect(server.EndPoint, Password, 5000);

            var commands = Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? "listplayers" : "getchat").ToArray();
            var replies = commands.AsParallel().WithDegreeOfParallelism(5).Select(c => (c, reply: rcon.SendCommand(c))).ToArray();

            foreach (var (command, reply) in replies)
                Assert.AreEqual(command == "listplayers" ? "No Players Connected \n" : "Server received, But no response!! \n", reply);
            Assert.IsFalse(server.PipeliningSeen, "A command was sent before the previous one was answered.");
        }

        /// <summary>
        /// Answers one client like an ASA server.
        /// </summary>
        private sealed class FakeRconServer : IDisposable
        {
            public static readonly string BigReply = string.Concat(Enumerable.Range(0, 400).Select(i => $"{i}. Player {i}, 0002{i:x28}\n"));

            private readonly TcpListener _listener;
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();
            private readonly Task _task;

            public FakeRconServer()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                EndPoint = (IPEndPoint)_listener.LocalEndpoint;
                _task = Task.Run(RunAsync);
            }

            public IPEndPoint EndPoint { get; }

            public int ReplyDelay { get; init; }

            /// <summary>
            /// A packet arrived before the previous one was answered.
            /// </summary>
            public bool PipeliningSeen { get; private set; }

            public void Dispose()
            {
                _stop.Cancel();
                _listener.Stop();
                try
                {
                    _task.Wait(2000);
                }
                catch (AggregateException)
                {
                    // the listener was stopped
                }
            }

            private async Task RunAsync()
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();

                while (!_stop.IsCancellationRequested)
                {
                    var (id, type, body) = await ReadPacketAsync(stream);
                    if (client.Available > 0)
                        PipeliningSeen = true;

                    if (ReplyDelay > 0)
                        await Task.Delay(ReplyDelay);

                    if (type == 3)
                    {
                        if (body != Password)
                        {
                            await WriteAsync(stream, Packet(-1, 2, string.Empty));
                            return;
                        }
                        await WriteAsync(stream, Packet(id, 2, string.Empty));
                        continue;
                    }

                    switch (body)
                    {
                        case "listplayers":
                            await WriteAsync(stream, Packet(id, 0, "No Players Connected \n "));
                            break;
                        case "big":
                            await WriteAsync(stream, Packet(id, 0, BigReply));
                            break;
                        case "fragmented":
                            foreach (var chunk in Packet(id, 0, BigReply).Chunk(1000))
                            {
                                await WriteAsync(stream, chunk);
                                await Task.Delay(10);
                            }
                            break;
                        case "split":
                            await WriteAsync(stream, Packet(id, 0, "first part, ").Concat(Packet(id, 0, "second part")).ToArray());
                            break;
                        case "silent":
                            break;
                        default:
                            await WriteAsync(stream, Packet(id, 0, "Server received, But no response!! \n "));
                            break;
                    }
                }
            }

            private static async Task<(int Id, int Type, string Body)> ReadPacketAsync(NetworkStream stream)
            {
                var sizeBytes = new byte[4];
                await stream.ReadExactlyAsync(sizeBytes);
                var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(sizeBytes)];
                await stream.ReadExactlyAsync(data);
                return (BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)), Encoding.UTF8.GetString(data, 8, data.Length - 10));
            }

            private static byte[] Packet(int id, int type, string body)
            {
                var bodyBytes = Encoding.UTF8.GetBytes(body);
                var packet = new byte[14 + bodyBytes.Length];
                BinaryPrimitives.WriteInt32LittleEndian(packet, 10 + bodyBytes.Length);
                BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
                BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
                bodyBytes.CopyTo(packet, 12);
                return packet;
            }

            private static Task WriteAsync(NetworkStream stream, byte[] bytes) => stream.WriteAsync(bytes, 0, bytes.Length);
        }
    }
}
