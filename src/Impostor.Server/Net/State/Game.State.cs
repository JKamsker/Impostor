﻿using System.Linq;
using System.Threading.Tasks;
using Impostor.Api;
using Impostor.Api.Events;
using Impostor.Api.Innersloth.Data;
using Impostor.Api.Net;
using Impostor.Api.Net.Messages;
using Impostor.Hazel;
using Microsoft.Extensions.Logging;

namespace Impostor.Server.Net.State
{
    internal partial class Game
    {
        private async ValueTask PlayerAdd(ClientPlayer player)
        {
            // Store player.
            if (!_players.TryAdd(player.Client.Id, player))
            {
                throw new ImpostorException("Failed to add player to game.");
            }

            // Assign hostId if none is set.
            if (HostId == -1)
            {
                HostId = player.Client.Id;
                await InitGameDataAsync(player);
            }

            await _eventManager.CallAsync(new PlayerJoinedGameEvent(this, player));
        }

        private async ValueTask<bool> PlayerRemove(int playerId, bool isBan = false)
        {
            if (!_players.TryRemove(playerId, out var player))
            {
                return false;
            }

            _logger.LogInformation("{0} - Player {1} ({2}) has left.", Code, player.Client.Name, playerId);

            player.Client.Player = null;

            // Game is empty, remove it.
            if (_players.IsEmpty)
            {
                GameState = GameStates.Destroyed;

                // Remove instance reference.
                await _gameManager.RemoveAsync(Code);
                return true;
            }

            // Host migration.
            if (HostId == playerId)
            {
                await MigrateHost();
            }

            if (isBan && player.Client.Connection != null)
            {
                _bannedIps.Add(player.Client.Connection.EndPoint.Address);
            }

            await _eventManager.CallAsync(new PlayerLeftGameEvent(this, player, isBan));

            return true;
        }

        private async ValueTask MigrateHost()
        {
            // Pick the first player as new host.
            var host = _players
                .Select(p => p.Value)
                .FirstOrDefault();

            if (host == null)
            {
                await EndAsync();
                return;
            }

            HostId = host.Client.Id;
            _logger.LogInformation("{0} - Assigned {1} ({2}) as new host.", Code, host.Client.Name, host.Client.Id);

            // Check our current game state.
            if (GameState == GameStates.Ended && host.Limbo == LimboStates.WaitingForHost)
            {
                GameState = GameStates.NotStarted;

                // Spawn the host.
                await HandleJoinGameNew(host, false);

                // Pull players out of limbo.
                await CheckLimboPlayers();
            }
        }

        private async ValueTask CheckLimboPlayers()
        {
            using var message = MessageWriter.Get(MessageType.Reliable);

            foreach (var (_, player) in _players.Where(x => x.Value.Limbo == LimboStates.WaitingForHost))
            {
                WriteJoinedGameMessage(message, true, player);
                WriteAlterGameMessage(message, false, IsPublic);

                player.Limbo = LimboStates.NotLimbo;

                await SendToAsync(message, player.Client.Id);
            }
        }
    }
}