﻿using Impostor.Api.Net;
using Impostor.Api.Net.Messages;

namespace Impostor.Api.Innersloth.Net
{
    public abstract class InnerNetObject : GameObject
    {
        public uint NetId { get; internal set; }

        public int OwnerId { get; internal set; }

        public SpawnFlags SpawnFlags { get; internal set; }

        public abstract void HandleRpc(IClientPlayer sender, byte callId, IMessageReader reader);

        public abstract bool Serialize(IMessageWriter writer, bool initialState);

        public abstract void Deserialize(IClientPlayer sender, IMessageReader reader, bool initialState);
    }
}