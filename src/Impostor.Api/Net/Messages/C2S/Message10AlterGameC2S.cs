﻿using Impostor.Api.Extensions;
using Impostor.Api.Innersloth.Data;

namespace Impostor.Api.Net.Messages.C2S
{
    public class Message10AlterGameC2S
    {
        public static void Serialize(IMessageWriter writer)
        {
            throw new System.NotImplementedException();
        }

        public static void Deserialize(IMessageReader reader, out AlterGameTags gameTag, out bool isPublic)
        {
            var slice = reader.ReadBytes(sizeof(byte) + sizeof(byte)).Span;

            gameTag = (AlterGameTags)slice.ReadByte();
            isPublic = slice.ReadBoolean();
        }
    }
}