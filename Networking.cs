using System;
using Sandbox.ModAPI;
using System.Collections.Generic;
using ProtoBuf;
using System.Linq;
using VRage.ModAPI;
using VRage.Game.ModAPI;

namespace Cheetah.Networking
{
    public class AutoSet<T>
    {
        protected T Underlying;
        protected Func<T, bool> Checker = null;
        public string DataID { get; protected set; }
        protected readonly IMyEntity Entity;
        protected readonly long EntityID;
        protected readonly string Header;
        /// <summary>
        /// This is only executed on client (non-host) side, when server sends a new value.
        /// </summary>
        public event Action GotValueFromServer;

        /// <summary>
        /// Before creating, make sure that Networker is initialized.
        /// </summary>
        public AutoSet(IMyEntity Entity, string DataID, T Default = default(T), Func<T, bool> Checker = null)
        {
            this.DataID = DataID;
            this.Checker = Checker;
            EntityID = Entity.EntityId;
            this.Entity = Entity;
			Underlying = Default;
            Header = $"AutoSet|{DataID}|{Default.GetType().ToString()}";
            Register();
        }

        protected void Register()
        {
            Networker.RegisterHandler(Entity, Header, Receive);
        }

        protected void Unregister()
        {
            Networker.UnregisterHandler(Entity, Header, Receive);
        }

        public void Close()
        {
            Unregister();
        }

        public T Get()
        {
            return Underlying;
        }

        public static implicit operator T(AutoSet<T> Object)
        {
            return Object.Get();
        }

        public void Set(T New)
        {
            if (IsValid(New))
            {
                if (Networker.IsServer)
                {
                    Underlying = New;
                    if (MyAPIGateway.Multiplayer.MultiplayerActive) Networker.SendToAll(Header, "Set", Serialize(Underlying), EntityID);
                }
                else
                {
                    Networker.SendToServer(Header, "SetRequest", Serialize(New), EntityID);
                }
            }
            else
            {
                LaserTools.SessionCore.DebugWrite($"AutoSet[{Entity.DisplayName}]", $"Invalid value supplied: {New.ToString()}");
            }
        }

        public void Ask()
        {
            Networker.SendToServer(Header, "Get", null, EntityID);
        }

        protected void Receive(Networker.DataMessage Message)
        {
            if (Networker.IsServer)
            {
                if (Message.DataDesc == "SetRequest")
                {
                    var New = Deserialize(Message.Data);
                    Set(New);
                }
                else if (Message.DataDesc == "Get")
                {
                    Networker.SendTo(Message.SenderClientID, Header, "Set", Serialize(Underlying), EntityID);
                }
            }
            else
            {
                if (Message.DataDesc == "Set" && Message.IsSentFromServer())
                {
                    var New = Deserialize(Message.Data);
                    Underlying = New;
                    GotValueFromServer();
                }
            }
        }

        public bool IsValid(T Object)
        {
            return Checker == null || Checker(Object);
        }

        protected static byte[] Serialize(T Object)
        {
            return MyAPIGateway.Utilities.SerializeToBinary(Object);
        }

        protected static T Deserialize(byte[] Raw)
        {
            return MyAPIGateway.Utilities.SerializeFromBinary<T>(Raw);
        }
    }

    public static class Networker
    {
        const bool Debug = true;
        public const ushort CommChannel = 7254;
        public static bool IsServer => MyAPIGateway.Multiplayer.IsServer;
        public static ulong ServerID => MyAPIGateway.Multiplayer.ServerId;
        public static bool Inited { get; private set; }
        // We won't go beyond 4KKK of workshop creations quickly, right?
        public static uint ModID { get; private set; }

        public static void Init(uint ModWorkshopID)
        {
            if (Inited) return;
            ModID = ModWorkshopID;
            MyAPIGateway.Multiplayer.RegisterMessageHandler(CommChannel, PrimaryRawHandler);
            Inited = true;
        }

        [ProtoContract]
        public struct DataMessage
        {
            [ProtoMember(10)]
            public uint ModID;
            [ProtoMember(12)]
            public ulong SenderClientID;
            /// <summary>
            /// May be used with EntityID. Optional.
            /// </summary>
            [ProtoMember(1)]
            public long ObjectID;
            /// <summary>
            /// Describes the type of sender object. You may put the name of your class here. It is used to filter whom to bother with data.
            /// </summary>
            [ProtoMember(2)]
            public string SenderName;
            [ProtoMember(15)]
            public string DataDesc;
            [ProtoMember(16)]
            public byte[] Data;

            public bool IsSentFromServer()
            {
                return SenderClientID == MyAPIGateway.Multiplayer.ServerId;
            }
        }

        /// <summary>
        /// Assembles and sends the given data
        /// </summary>
        /// <param name="SenderName">Sender type name. Used as filter on receiving side.</param>
        /// <param name="DataDescription">Describes data. Helpful for parsing.</param>
        /// <param name="Data">The data you want to send.</param>
        /// <param name="ObjectID">ObjectID to which the data belongs. You can use EntityID if your data belongs to an entity.</param>
        public static void SendToAll(string SenderName, string DataDescription, byte[] Data, long ObjectID = 0)
        {
            var Others = MyAPIGateway.Multiplayer.Players.GetClientIDs().Except(new List<ulong> { MyAPIGateway.Multiplayer.MyId });
            var Packets = AssembleMessage(SenderName, ObjectID, DataDescription, Data);
            foreach (ulong ID in Others)
            {
                foreach (var Packet in Packets)
                    MyAPIGateway.Multiplayer.SendMessageToOthers(CommChannel, Packet);
            }
        }

        /// <summary>
        /// Assembles and sends the given data
        /// </summary>
        /// <param name="SenderName">Sender type name. Used as filter on receiving side.</param>
        /// <param name="DataDescription">Describes data. Helpful for parsing.</param>
        /// <param name="Data">The data you want to send.</param>
        /// <param name="ObjectID">ObjectID to which the data belongs. You can use EntityID if your data belongs to an entity.</param>
        public static void SendTo(ulong Recipient, string SenderName, string DataDescription, byte[] Data, long ObjectID = 0)
        {
            var Packets = AssembleMessage(SenderName, ObjectID, DataDescription, Data);
            foreach (var Packet in Packets)
                MyAPIGateway.Multiplayer.SendMessageTo(CommChannel, Packet, Recipient);
        }

        /// <summary>
        /// Assembles and sends the given data
        /// </summary>
        /// <param name="SenderName">Sender type name. Used as filter on receiving side.</param>
        /// <param name="DataDescription">Describes data. Helpful for parsing.</param>
        /// <param name="Data">The data you want to send.</param>
        /// <param name="ObjectID">EntityID to which the data belongs. Leave 0 for session component data!</param>
        public static void SendToServer(string SenderName, string DataDescription, byte[] Data, long ObjectID = 0)
        {
            var Packets = AssembleMessage(SenderName, ObjectID, DataDescription, Data);
            foreach (var Packet in Packets)
                MyAPIGateway.Multiplayer.SendMessageToServer(CommChannel, Packet);
        }

        static List<byte[]> AssembleMessage(string SenderName, long ObjectID, string DataDescription, byte[] Data)
        {
            DataMessage Message = new DataMessage();
            Message.ModID = ModID;
            Message.SenderClientID = MyAPIGateway.Multiplayer.MyId;
            Message.SenderName = SenderName;
            Message.ObjectID = ObjectID;
            Message.DataDesc = DataDescription;
            Message.Data = Data;
            return MessageHelper.Segment(MyAPIGateway.Utilities.SerializeToBinary(Message));
        }

        static void PrimaryRawHandler(byte[] packet)
        {
            var raw = MessageHelper.Desegment(packet);
            if (raw != null)
            {
                try
                {
                    var Message = MyAPIGateway.Utilities.SerializeFromBinary<DataMessage>(raw);
                    SecondaryHandler(Message);
                }
                catch (Exception Scrap)
                {
                    Helper.Report(Scrap, "Networker.PrimaryRawHandler");
                }
            }
        }

        #region General
        static HashSet<ReceivedMessageHandler> GeneralHandlers = new HashSet<ReceivedMessageHandler>();
        /// <summary>
        /// Subscribes a monitor-level handler to ALL received messages.
        /// </summary>
        public static bool RegisterHandler(ReceivedMessageHandler Handler)
        {
            return GeneralHandlers.Add(Handler);
        }

        public static bool UnregisterHandler(ReceivedMessageHandler Handler)
        {
            return GeneralHandlers.Remove(Handler);
        }
        #endregion

        #region Session
        static Dictionary<string, HashSet<ReceivedMessageHandler>> SessionHandlers = new Dictionary<string, HashSet<ReceivedMessageHandler>>();
        public delegate void ReceivedMessageHandler(DataMessage Message);
        /// <summary>
        /// Subscribes a session-level handler to the message received event from given sender.
        /// </summary>
        /// <param name="SenderFilter">A DataMessage.SenderName on which the handler will be invoked.</param>
        public static bool RegisterHandler(string SenderFilter, ReceivedMessageHandler Handler)
        {
            if (!SessionHandlers.ContainsKey(SenderFilter)) SessionHandlers.Add(SenderFilter, new HashSet<ReceivedMessageHandler>());
            var Handlers = SessionHandlers[SenderFilter];
            return Handlers.Add(Handler);
        }

        public static bool UnregisterHandler(string SenderFilter, ReceivedMessageHandler Handler)
        {
            if (!SessionHandlers.ContainsKey(SenderFilter)) return false;
            return SessionHandlers[SenderFilter].Remove(Handler);
        }
        #endregion

        #region Entity
        static Dictionary<long, Dictionary<string, HashSet<ReceivedMessageHandler>>> EntityHandlersCollection = new Dictionary<long, Dictionary<string, HashSet<ReceivedMessageHandler>>>();

        /// <summary>
        /// Subscribes an entity-level handler to the message received event from given sender about the given entity.
        /// <para />
        /// Useful for entity gamelogic components. Absolutely not useful for entity components.
        /// </summary>
        /// <param name="SenderName">A DataMessage.SenderName on which the handler will be invoked.</param>
        public static bool RegisterHandler(VRage.ModAPI.IMyEntity Entity, string SenderName, ReceivedMessageHandler Handler)
        {
            long EntityID = Entity.EntityId;
            if (!EntityHandlersCollection.ContainsKey(EntityID)) EntityHandlersCollection.Add(EntityID, new Dictionary<string, HashSet<ReceivedMessageHandler>>());
            var EntityHandlers = EntityHandlersCollection[EntityID];
            if (!EntityHandlers.ContainsKey(SenderName)) EntityHandlers.Add(SenderName, new HashSet<ReceivedMessageHandler>());
            return EntityHandlers[SenderName].Add(Handler);
        }

        public static bool UnregisterHandler(VRage.ModAPI.IMyEntity Entity, string SenderFilter, ReceivedMessageHandler Handler)
        {
            long EntityID = Entity.EntityId;
            if (!EntityHandlersCollection.ContainsKey(EntityID)) return false;
            if (!EntityHandlersCollection[EntityID].ContainsKey(SenderFilter)) return false;
            return EntityHandlersCollection[EntityID][SenderFilter].Remove(Handler);
        }
        #endregion


        static void SecondaryHandler(DataMessage Message)
        {
            if (Message.ModID != ModID) return;
            if (Message.SenderClientID == MyAPIGateway.Multiplayer.MyId) return;
            foreach (var Handler in GeneralHandlers)
            {
                try
                {
                    Handler(Message);
                }
                catch (Exception Scrap)
                {
                    Report(Scrap, "InvokeGeneralHandler");
                }
            }

            if (Message.ObjectID == 0)
            {
                if (SessionHandlers.ContainsKey(Message.SenderName))
                {
                    foreach (var Handler in SessionHandlers[Message.SenderName])
                    {
                        try
                        {
                            Handler(Message);
                        }
                        catch (Exception Scrap)
                        {
                            Report(Scrap, "InvokeHandler");
                        }
                    }
                }
                else
                {
                    DebugWrite("SecondaryHandler.SessionHandlers", $"A message with tag '{Message.SenderName}' was received and no handler is subscribed to it.");
                }
            }
            else
            {
                if (EntityHandlersCollection.ContainsKey(Message.ObjectID))
                {
                    var EntityHandlers = EntityHandlersCollection[Message.ObjectID];
                    if (EntityHandlers.ContainsKey(Message.SenderName))
                    {
                        foreach (var Handler in EntityHandlers[Message.SenderName])
                        {
                            try
                            {
                                Handler(Message);
                            }
                            catch (Exception Scrap)
                            {
                                Report(Scrap, "InvokeHandler");
                            }
                        }
                    }
                    else
                    {
                        DebugWrite("SecondaryHandler.EntityHandlers", $"A message with tag '{Message.SenderName}' was received and no handler is subscribed to it.");
                    }
                }
                else
                {
                    DebugWrite("SecondaryHandler", $"A message with entityid '{Message.ObjectID}' was received and no handler is subscribed to this entity.");
                }
            }
        }

        public static void Close()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(CommChannel, PrimaryRawHandler);
            GeneralHandlers = null;
            SessionHandlers = null;
            EntityHandlersCollection = null;
        }

        static void DebugWrite(string Source, string Message, string Prefix = "Networker.")
        {
            LaserTools.SessionCore.DebugWrite(Source, Message, DebugPrefix: Prefix);
        }

        static void Report(Exception Scrap, string Source, string Prefix = "Networker.")
        {
            LaserTools.SessionCore.LogError(Source, Scrap, DebugPrefix: Prefix);
        }
    }

    /// <summary>
    /// Courtesy of Jimmacle. Segments and reassembles data packets, thus allowing to go over 4 KB limit. 
    /// </summary>
    public static class MessageHelper
    {
        private static readonly Dictionary<int, PartialMessage> Messages = new Dictionary<int, PartialMessage>();
        private const int PACKET_SIZE = 4096;
        private const int HEADER_SIZE = sizeof(int) * 2;
        private const int DATA_LENGTH = PACKET_SIZE - HEADER_SIZE;

        /// <summary>
        /// Segments a byte array.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static List<byte[]> Segment(byte[] message)
        {
            var hash = BitConverter.GetBytes(message.GetHashCode());
            var packets = new List<byte[]>();
            var msgIndex = 0;

            var packetId = message.Length / DATA_LENGTH;

            while (packetId >= 0)
            {
                var id = BitConverter.GetBytes(packetId);
                byte[] segment;

                if (message.Length - msgIndex > DATA_LENGTH)
                    segment = new byte[PACKET_SIZE];
                else
                    segment = new byte[HEADER_SIZE + message.Length - msgIndex];

                //Copy packet header data.
                Array.Copy(hash, segment, hash.Length);
                Array.Copy(id, 0, segment, hash.Length, id.Length);

                //Copy segment of original message.
                Array.Copy(message, msgIndex, segment, HEADER_SIZE, segment.Length - HEADER_SIZE);

                packets.Add(segment);
                msgIndex += DATA_LENGTH;
                packetId--;
            }

            return packets;
        }

        /// <summary>
        /// Reassembles a segmented byte array.
        /// </summary>
        /// <param name="packet">Array segment.</param>
        /// <returns>Message fully desegmented, "message" is assigned.</returns>
        public static byte[] Desegment(byte[] packet)
        {
            var hash = BitConverter.ToInt32(packet, 0);
            var packetId = BitConverter.ToInt32(packet, sizeof(int));
            var dataBytes = new byte[packet.Length - HEADER_SIZE];
            Array.Copy(packet, HEADER_SIZE, dataBytes, 0, packet.Length - HEADER_SIZE);

            if (!Messages.ContainsKey(hash))
                if (packetId == 0)
                    return dataBytes;
                else
                    Messages.Add(hash, new PartialMessage(packetId));

            var message = Messages[hash];
            message.WritePart(packetId, dataBytes);

            if (!message.IsComplete)
                return null;

            Messages.Remove(hash);
            return message.Data;
        }

        private class PartialMessage
        {
            public byte[] Data;
            private readonly HashSet<int> _receivedPackets = new HashSet<int>();
            private readonly int _maxId;
            public bool IsComplete => _receivedPackets.Count == _maxId + 1;

            public PartialMessage(int startId)
            {
                _maxId = startId;
                Data = new byte[DATA_LENGTH * startId];
            }

            public void WritePart(int id, byte[] data)
            {
                var index = _maxId - id;
                var requiredLength = index * DATA_LENGTH + data.Length;

                if (Data.Length < requiredLength)
                    Array.Resize(ref Data, requiredLength);

                Array.Copy(data, 0, Data, index * DATA_LENGTH, data.Length);
                _receivedPackets.Add(id);
            }
        }
    }

    public static class Helper
    {
        public static void Report(this Exception Scrap, string Source)
        {
            MyAPIGateway.Utilities.ShowMessage("Syncer|" + Source, $"Exception caught: {Scrap.Message}");
        }
		
		public static List<ulong> GetClientIDs(this IMyPlayerCollection Collection)
        {
			List<IMyPlayer> players = new List<IMyPlayer>();
            Collection.GetPlayers(players);
            return players.Select(x => x.SteamUserId).ToList();
        }
    }
}