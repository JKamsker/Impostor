﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Innersloth.Data;
using Impostor.Api.Innersloth.GameData;
using Impostor.Api.Innersloth.Net;
using Impostor.Api.Innersloth.Net.Objects;
using Impostor.Api.Net.Messages;
using Impostor.Api.Net.Messages.C2S;
using Impostor.Api.Net.Messages.S2C;
using Impostor.Hazel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    internal partial class Game
    {
        private const int FakeClientId = int.MaxValue - 1;

        /// <summary>
        ///     Used for global object, spawned by the host.
        /// </summary>
        private const int InvalidClient = -2;

        /// <summary>
        ///     Used internally to set the OwnerId to the current ClientId.
        ///     i.e: <code>ownerId = ownerId == -3 ? this.ClientId : ownerId;</code>
        /// </summary>
        private const int CurrentClient = -3;

        private static readonly Type[] SpawnableObjects =
        {
            typeof(InnerShipStatus), // ShipStatus
            typeof(InnerMeetingHud),
            typeof(InnerLobbyBehaviour),
            typeof(InnerGameData),
            typeof(InnerPlayerControl),
            typeof(InnerShipStatus), // HeadQuarters
            typeof(InnerShipStatus), // PlanetMap
            typeof(InnerShipStatus), // AprilShipStatus
        };

        private readonly List<InnerNetObject> _allObjects = new List<InnerNetObject>();
        private readonly Dictionary<uint, InnerNetObject> _allObjectsFast = new Dictionary<uint, InnerNetObject>();

        private int _gamedataInitialized;
        private bool _gamedataFakeReceived;

        private async ValueTask InitGameDataAsync(ClientPlayer player)
        {
            if (Interlocked.Exchange(ref _gamedataInitialized, 1) != 0)
            {
                return;
            }

            /*
             * The Among Us client on 20.9.22i spawns some components on the host side and
             * only spawns these on other clients when someone else connects. This means that we can't
             * parse data until someone connects because we don't know which component belongs to the NetId.
             *
             * We solve this by spawning a fake player and removing the player when the spawn GameData
             * is received in HandleGameDataAsync.
             */
            using (var message = MessageWriter.Get(MessageType.Reliable))
            {
                // Spawn a fake player.
                Message01JoinGameS2C.SerializeJoin(message, false, Code, FakeClientId, HostId);

                message.StartMessage(MessageFlags.GameData);
                message.Write(Code);
                message.StartMessage(GameDataTag.SceneChangeFlag);
                message.WritePacked(FakeClientId);
                message.Write("OnlineGame");
                message.EndMessage();
                message.EndMessage();

                await player.Client.Connection.SendAsync(message);
            }
        }

        public async ValueTask<bool> HandleGameDataAsync(IMessageReader parent, ClientPlayer sender, bool toPlayer)
        {
            // Find target player.
            ClientPlayer target = null;

            if (toPlayer)
            {
                var targetId = parent.ReadPackedInt32();
                if (targetId == FakeClientId && !_gamedataFakeReceived && sender.IsHost)
                {
                    _gamedataFakeReceived = true;

                    // Remove the fake client, we received the data.
                    using (var message = MessageWriter.Get(MessageType.Reliable))
                    {
                        WriteRemovePlayerMessage(message, false, FakeClientId, (byte)DisconnectReason.ExitGame);

                        await sender.Client.Connection.SendAsync(message);
                    }
                }
                else if (!TryGetPlayer(targetId, out target))
                {
                    _logger.LogWarning("Player {0} tried to send GameData to unknown player {1}.", sender.Client.Id, targetId);
                    return false;
                }

                _logger.LogTrace("Received GameData for target {0}.", targetId);
            }

            // Parse GameData messages.
            while (parent.Position < parent.Length)
            {
                var reader = parent.ReadMessage();

                switch (reader.Tag)
                {
                    case GameDataTag.DataFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjectsFast.TryGetValue(netId, out var obj))
                        {
                            obj.Deserialize(sender, reader, false);
                        }
                        else
                        {
                            _logger.LogWarning("Received DataFlag for unregistered NetId {0}.", netId);
                        }

                        break;
                    }

                    case GameDataTag.RpcFlag:
                    {
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjectsFast.TryGetValue(netId, out var obj))
                        {
                            // TODO: Remove try catch.
                            try
                            {
                                obj.HandleRpc(sender, reader.ReadByte(), reader);
                            }
                            catch (NotImplementedException)
                            {
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Received RpcFlag for unregistered NetId {0}.", netId);
                        }

                        break;
                    }

                    case GameDataTag.SpawnFlag:
                    {
                        // Only the host is allowed to despawn objects.
                        if (!sender.IsHost)
                        {
                            _logger.LogWarning("Player {0} ({1}) tried to send SpawnFlag as non-host.", sender.Client.Name, sender.Client.Id);
                            return false;
                        }

                        var objectId = reader.ReadPackedUInt32();
                        if (objectId < SpawnableObjects.Length)
                        {
                            var innerNetObject = (InnerNetObject) ActivatorUtilities.CreateInstance(_serviceProvider, SpawnableObjects[objectId], this);
                            var ownerClientId = reader.ReadPackedInt32();

                            innerNetObject.SpawnFlags = (SpawnFlags) reader.ReadByte();

                            var components = innerNetObject.GetComponentsInChildren<InnerNetObject>();
                            var componentsCount = reader.ReadPackedInt32();

                            if (componentsCount != components.Count)
                            {
                                _logger.LogError(
                                    "Children didn't match for spawnable {0}, name {1} ({2} != {3})",
                                    objectId,
                                    innerNetObject.GetType().Name,
                                    componentsCount,
                                    components.Count);
                                continue;
                            }

                            _logger.LogDebug(
                                "Spawning {0} components, SpawnFlags {1}",
                                innerNetObject.GetType().Name,
                                innerNetObject.SpawnFlags);

                            for (var i = 0; i < componentsCount; i++)
                            {
                                var obj = components[i];

                                obj.NetId = reader.ReadPackedUInt32();
                                obj.OwnerId = ownerClientId;

                                _logger.LogDebug(
                                    "- {0}, NetId {1}, OwnerId {2}",
                                    obj.GetType().Name,
                                    obj.NetId,
                                    obj.OwnerId);

                                if (!AddNetObject(obj))
                                {
                                    _logger.LogTrace("Failed to AddNetObject, it already exists.");

                                    obj.NetId = uint.MaxValue;
                                    break;
                                }

                                var readerSub = reader.ReadMessage();
                                if (readerSub.Length > 0)
                                {
                                    obj.Deserialize(sender, readerSub, true);
                                }

                                GameNet.OnSpawn(obj);
                            }

                            continue;
                        }

                        _logger.LogError("Couldn't find spawnable object {0}.", objectId);
                        break;
                    }

                    case GameDataTag.DespawnFlag:
                    {
                        // Only the host is allowed to despawn objects.
                        var netId = reader.ReadPackedUInt32();
                        if (_allObjectsFast.TryGetValue(netId, out var obj))
                        {
                            if (sender.Client.Id != obj.OwnerId && !sender.IsHost)
                            {
                                _logger.LogWarning(
                                    "Player {0} ({1}) tried to send DespawnFlag for {2} but was denied.",
                                    sender.Client.Name,
                                    sender.Client.Id,
                                    netId);
                                return false;
                            }

                            RemoveNetObject(obj);
                            GameNet.OnDestroy(obj);
                            _logger.LogDebug("Destroyed InnerNetObject {0} ({1}), OwnerId {2}", obj.GetType().Name, netId, obj.OwnerId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Player {0} ({1}) sent DespawnFlag for unregistered NetId {2}.",
                                sender.Client.Name,
                                sender.Client.Id,
                                netId);
                        }

                        break;
                    }

                    case GameDataTag.SceneChangeFlag:
                    {
                        // Sender is only allowed to change his own scene.
                        var clientId = reader.ReadPackedInt32();
                        if (clientId != sender.Client.Id)
                        {
                            _logger.LogWarning(
                                "Player {0} ({1}) tried to send SceneChangeFlag for another player.",
                                sender.Client.Name,
                                sender.Client.Id);
                            return false;
                        }

                        sender.Scene = reader.ReadString();

                        _logger.LogTrace("> Scene {0} to {1}", clientId, sender.Scene);
                        break;
                    }

                    case GameDataTag.ReadyFlag:
                    {
                        var clientId = reader.ReadPackedInt32();
                        _logger.LogTrace("> IsReady {0}", clientId);
                        break;
                    }

                    default:
                    {
                        _logger.LogTrace("Bad GameData tag {0}", reader.Tag);
                        break;
                    }
                }
            }

            return true;
        }

        private bool AddNetObject(InnerNetObject obj)
        {
            if (_allObjectsFast.ContainsKey(obj.NetId))
            {
                return false;
            }

            _allObjects.Add(obj);
            _allObjectsFast.Add(obj.NetId, obj);
            return true;
        }

        private void RemoveNetObject(InnerNetObject obj)
        {
            var index = _allObjects.IndexOf(obj);
            if (index > -1)
            {
                _allObjects.RemoveAt(index);
            }

            _allObjectsFast.Remove(obj.NetId);

            obj.NetId = uint.MaxValue;
        }

        public T FindObjectByNetId<T>(uint netId)
            where T : InnerNetObject
        {
            if (_allObjectsFast.TryGetValue(netId, out var obj))
            {
                return (T) obj;
            }

            return null;
        }
    }
}