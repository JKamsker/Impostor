using System;
using System.Collections.Generic;

using Impostor.Api.Extensions;
using Impostor.Api.Net.Messages;

namespace Impostor.Server.GameData.Objects
{
    public partial class InnerGameData
    {
        public class PlayerInfo
        {
            public PlayerInfo()
            {
            }

            public PlayerInfo(byte playerId)
            {
                PlayerId = playerId;
            }

            public byte PlayerId { get; }

            public string PlayerName { get; private set; }

            public byte ColorId { get; private set; }

            public uint HatId { get; private set; }

            public uint PetId { get; private set; }

            public uint SkinId { get; private set; }

            public bool Disconnected { get; private set; }

            public bool IsImpostor { get; private set; }

            public bool IsDead { get; private set; }

            public List<TaskInfo> Tasks { get; private set; }

            public void Serialize(IMessageWriter writer)
            {
                throw new NotImplementedException();
            }

            public void Deserialize(IMessageReader reader)
            {
                PlayerName = reader.ReadString();
                ColorId = reader.ReadByte();
                HatId = reader.ReadPackedUInt32();
                PetId = reader.ReadPackedUInt32();
                SkinId = reader.ReadPackedUInt32();
                var flag = reader.ReadByte();
                Disconnected = (flag & 1) > 0;
                IsImpostor = (flag & 2) > 0;
                IsDead = (flag & 4) > 0;
                var taskCount = reader.ReadByte();
                Tasks = new List<TaskInfo>();
                for (var i = 0; i < taskCount; i++)
                {
                    Tasks.Add(new TaskInfo());
                    Tasks[i].Deserialize(reader);
                }
            }

            public static PlayerInfo Deserialize(ref ReadOnlySpan<byte> reader)
            {
                var info = new PlayerInfo
                {
                    PlayerName = reader.ReadString(),
                    ColorId = reader.ReadByte(),
                    HatId = reader.ReadPackedUInt32(),
                    PetId = reader.ReadPackedUInt32(),
                    SkinId = reader.ReadPackedUInt32(),
                };

                var flag = reader.ReadByte();
                info.Disconnected = (flag & 1) > 0;
                info.IsImpostor = (flag & 2) > 0;
                info.IsDead = (flag & 4) > 0;

                var taskCount = reader.ReadByte();

                info.Tasks = new List<TaskInfo>(taskCount);
                for (var i = 0; i < taskCount; i++)
                {
                    info.Tasks.Add(TaskInfo.Deserialize(ref reader));
                }

                return info;
            }
        }
    }
}