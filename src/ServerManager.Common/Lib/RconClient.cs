using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ServerManagerTool.Common.Lib
{
    /// <summary>
    /// The RCON password was not accepted.
    /// </summary>
    public class RconAuthenticationException : Exception
    {
        public RconAuthenticationException(IPEndPoint endPoint)
            : base($"The RCON password was not accepted by {endPoint}.")
        {
        }
    }

    /// <summary>
    /// A Source RCON client, the protocol ARK: Survival Ascended's RCON speaks: one TCP connection, logged in once,
    /// commands sent one at a time.
    /// </summary>
    /// <remarks>
    /// Checked against an ASA server: every command gets exactly one reply (a command without output replies
    /// "Server received, But no response!!"), and a wrong password gets an auth reply with ID -1, after which the server
    /// closes the connection. The server stops answering on a connection that receives two packets in one write, or the
    /// empty "end of reply" packet some Source RCON clients send after a command, so every packet is written on its own
    /// (no Nagle buffering) and a command waits for its reply before the next one is sent.
    /// </remarks>
    public sealed class RconClient : IDisposable
    {
        public const int DefaultTimeout = 10000;

        private const int SERVERDATA_RESPONSE_VALUE = 0;
        private const int SERVERDATA_AUTH_RESPONSE = 2;
        private const int SERVERDATA_EXECCOMMAND = 2;
        private const int SERVERDATA_AUTH = 3;

        // id + type + two terminating nulls
        private const int MinPacketSize = 10;
        // anything larger means the stream is out of step
        private const int MaxPacketSize = 4 * 1024 * 1024;

        private readonly object _lock = new object();
        private TcpClient _client;
        private NetworkStream _stream;
        private int _lastId;

        private RconClient(TcpClient client, IPEndPoint endPoint)
        {
            _client = client;
            _stream = client.GetStream();
            EndPoint = endPoint;
        }

        public IPEndPoint EndPoint { get; }

        /// <summary>
        /// False once the connection has failed or been closed; connect again to continue.
        /// </summary>
        public bool IsConnected => _client?.Connected ?? false;

        /// <summary>
        /// Opens a connection and logs in.
        /// </summary>
        /// <exception cref="RconAuthenticationException">The password was not accepted.</exception>
        /// <exception cref="TimeoutException">The server didn't accept the connection in time.</exception>
        /// <exception cref="IOException">The connection failed or the server didn't answer in time.</exception>
        /// <exception cref="SocketException">The connection was refused.</exception>
        public static RconClient Connect(IPEndPoint endPoint, string password, int timeoutMilliseconds = DefaultTimeout)
        {
            ArgumentNullException.ThrowIfNull(endPoint);

            var client = new TcpClient(endPoint.AddressFamily)
            {
                NoDelay = true,
                SendTimeout = timeoutMilliseconds,
                ReceiveTimeout = timeoutMilliseconds,
            };

            try
            {
                using (var cancellation = new CancellationTokenSource(timeoutMilliseconds))
                {
                    try
                    {
                        client.ConnectAsync(endPoint, cancellation.Token).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException($"No RCON connection to {endPoint} within {timeoutMilliseconds} ms.");
                    }
                }

                var rcon = new RconClient(client, endPoint);
                rcon.Authenticate(password);
                return rcon;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Sends a command and returns the server's reply. If the connection fails (or the reply doesn't come in time),
        /// the connection is closed and the exception passed on.
        /// </summary>
        public string SendCommand(string command)
        {
            lock (_lock)
            {
                if (_stream == null)
                    throw new ObjectDisposedException(nameof(RconClient), $"The RCON connection to {EndPoint} is closed.");

                try
                {
                    var id = NextId();
                    WritePacket(id, SERVERDATA_EXECCOMMAND, command ?? string.Empty);

                    var reply = new StringBuilder();
                    var packet = ReadPacket();
                    while (packet.Id != id)
                    {
                        // not the reply to this command (none is expected, but don't let it be taken for one)
                        packet = ReadPacket();
                    }
                    reply.Append(packet.Body);

                    // a reply split over several packets: the rest has already arrived
                    while (_client.Available > 0)
                    {
                        packet = ReadPacket();
                        if (packet.Id == id && packet.Type == SERVERDATA_RESPONSE_VALUE)
                            reply.Append(packet.Body);
                    }

                    return reply.ToString().TrimEnd(' ');
                }
                catch
                {
                    Close();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Close();
            }
        }

        private void Authenticate(string password)
        {
            var id = NextId();
            WritePacket(id, SERVERDATA_AUTH, password ?? string.Empty);

            while (true)
            {
                // Source servers may send an empty response packet before the auth reply (ASA doesn't); skip it
                var packet = ReadPacket();
                if (packet.Type != SERVERDATA_AUTH_RESPONSE)
                    continue;

                if (packet.Id == -1)
                    throw new RconAuthenticationException(EndPoint);
                if (packet.Id == id)
                    return;
            }
        }

        private int NextId()
        {
            // positive, and never -1 (the failed-login ID)
            _lastId = _lastId == int.MaxValue ? 1 : _lastId + 1;
            return _lastId;
        }

        private void Close()
        {
            _stream?.Dispose();
            _client?.Dispose();
            _stream = null;
            _client = null;
        }

        private void WritePacket(int id, int type, string body)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var packet = new byte[4 + MinPacketSize + bodyBytes.Length];

            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0), MinPacketSize + bodyBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
            bodyBytes.CopyTo(packet, 12);
            // the body's terminating null and the empty second string's null are already zero

            // one write per packet
            _stream.Write(packet, 0, packet.Length);
        }

        private (int Id, int Type, string Body) ReadPacket()
        {
            var sizeBytes = new byte[4];
            _stream.ReadExactly(sizeBytes);

            var size = BinaryPrimitives.ReadInt32LittleEndian(sizeBytes);
            if (size < MinPacketSize || size > MaxPacketSize)
                throw new IOException($"Invalid RCON packet size ({size}) from {EndPoint}.");

            var data = new byte[size];
            _stream.ReadExactly(data);

            var id = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0));
            var type = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            // the body ends in two nulls (its own and the empty second string's); trailing spaces are trimmed once the
            // whole reply is in (ASA ends replies with " \n "), so a space at a packet boundary is kept
            var body = Encoding.UTF8.GetString(data, 8, size - 8).TrimEnd('\0');
            return (id, type, body);
        }
    }
}
