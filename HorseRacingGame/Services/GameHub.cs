using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using HorseRacingGame.Models;

namespace HorseRacingGame.Services;

public class GameHub : Hub
{
    private readonly GameService _gameService;

    // Connection ID → Player ID mapping
    private static readonly ConcurrentDictionary<string, string> _connectionPlayerMap = new();

    public GameHub(GameService gameService)
    {
        _gameService = gameService;
    }

    // ─── CONNECTION LIFECYCLE ────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;

        if (_connectionPlayerMap.TryRemove(connectionId, out var playerId))
        {
            var player = _gameService.State.Players.FirstOrDefault(p => p.Id == playerId);
            var playerName = player?.Name ?? "Unknown";

            _gameService.RemovePlayer(connectionId);

            await Clients.All.SendAsync("PlayerLeft", playerName);
            await Clients.All.SendAsync("StateUpdated", _gameService.State);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ─── HUB METHODS ────────────────────────────────────────────────────────────

    public async Task JoinGame(string playerName, string deviceId)
    {
        var connectionId = Context.ConnectionId;

        var player = _gameService.AddOrReconnectPlayer(playerName, connectionId, deviceId);

        if (player == null)
        {
            // Player was kicked or lobby is full
            if (_gameService.State.KickedDeviceIds.Contains(deviceId) && _gameService.State.Phase != GamePhase.Lobby)
            {
                await Clients.Caller.SendAsync("Kicked", "You were kicked from this game. Please wait for the next round.");
            }
            else
            {
                await Clients.Caller.SendAsync("Error", "The lobby is full (max 6 players).");
            }
            return;
        }

        // Store mapping (remove old mapping for this device if exists)
        var oldConnection = _connectionPlayerMap.FirstOrDefault(kvp => kvp.Value == player.Id).Key;
        if (oldConnection != null && oldConnection != connectionId)
        {
            _connectionPlayerMap.TryRemove(oldConnection, out _);
        }
        _connectionPlayerMap[connectionId] = player.Id;

        await Clients.All.SendAsync("PlayerJoined", playerName);
        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    public async Task StartGame()
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        if (!player.IsHost)
        {
            await Clients.Caller.SendAsync("Error", "Only the host can start the game.");
            return;
        }

        var success = _gameService.StartGame(player.Id);
        if (!success)
        {
            await Clients.Caller.SendAsync("Error", "Cannot start game in the current phase.");
            return;
        }

        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    public async Task RollDice()
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        // Validate it's this player's turn
        var connectedPlayers = _gameService.State.Players.Where(p => p.IsConnected && !p.IsSpectator).ToList();
        if (connectedPlayers.Count == 0) return;

        var currentPlayer = connectedPlayers[_gameService.State.CurrentPlayerIndex % connectedPlayers.Count];
        if (currentPlayer.Id != player.Id)
        {
            await Clients.Caller.SendAsync("Error", "It's not your turn!");
            return;
        }

        var result = _gameService.RollDice(player.Id);
        if (result == null)
        {
            await Clients.Caller.SendAsync("Error", "Cannot roll dice in the current phase.");
            return;
        }

        var (die1, die2, sum, rollResult) = result.Value;

        // Broadcast dice roll
        await Clients.All.SendAsync("DiceRolled", die1, die2, sum, rollResult);

        // Determine additional broadcasts based on what happened
        var state = _gameService.State;
        var horse = state.Horses.FirstOrDefault(h => h.Number == sum);

        if (horse != null)
        {
            if (horse.Scratched && rollResult.Contains("scratched!"))
            {
                await Clients.All.SendAsync("HorseScratched", horse.Number, horse.ScratchPenalty ?? 0m);
            }
            else if (!horse.Scratched && state.Phase == GamePhase.Racing)
            {
                await Clients.All.SendAsync("HorseMoved", horse.Number, horse.Position);
            }
        }

        // Check if race was won
        if (state.Phase == GamePhase.Ended && state.WinningHorse != null)
        {
            var winnerNames = state.WinningPlayers.Select(p => p.Name).ToList();
            await Clients.All.SendAsync("RaceWon", state.WinningHorse.Number, winnerNames);

            var balanceChanges = state.WinningPlayers.Select(p => new
            {
                PlayerName = p.Name,
                NewBalance = p.Balance,
                LastChange = p.BalanceHistory.LastOrDefault() ?? ""
            }).Cast<object>().ToList();

            await Clients.All.SendAsync("PayoutSummary", balanceChanges);
        }

        await Clients.All.SendAsync("StateUpdated", state);
    }

    public async Task PlaceBet(int horseNumber, decimal amount)
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        var horse = _gameService.State.Horses.FirstOrDefault(h => h.Number == horseNumber);
        if (horse == null)
        {
            await Clients.Caller.SendAsync("Error", $"Horse #{horseNumber} does not exist.");
            return;
        }

        if (horse.Scratched)
        {
            await Clients.Caller.SendAsync("Error", $"Horse #{horseNumber} is scratched.");
            return;
        }

        if (amount <= 0 || amount > player.Balance)
        {
            await Clients.Caller.SendAsync("Error", "Invalid bet amount.");
            return;
        }

        player.Balance -= amount;
        _gameService.State.CentralPot += amount;
        _gameService.State.PotContributions.Add(new PotContribution
        {
            PlayerId = player.Id,
            Amount = amount,
            Reason = $"Bet on Horse #{horseNumber}"
        });
        player.BalanceHistory.Add($"-${amount} (bet on Horse #{horseNumber})");
        _gameService.State.ChatMessages.Add($"{player.Name} bet ${amount} on Horse #{horseNumber}.");

        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    public async Task EndGame()
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        if (!player.IsHost)
        {
            await Clients.Caller.SendAsync("Error", "Only the host can end the game.");
            return;
        }

        var success = _gameService.EndGame(player.Id);
        if (!success)
        {
            await Clients.Caller.SendAsync("Error", "Cannot end the game.");
            return;
        }

        var state = _gameService.State;

        var balanceChanges = state.Players.Select(p => new
        {
            PlayerName = p.Name,
            NewBalance = p.Balance,
            LastChange = p.BalanceHistory.LastOrDefault() ?? ""
        }).Cast<object>().ToList();

        await Clients.All.SendAsync("PayoutSummary", balanceChanges);
        await Clients.All.SendAsync("StateUpdated", state);
    }

    public async Task ResetGame()
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        if (!player.IsHost)
        {
            await Clients.Caller.SendAsync("Error", "Only the host can reset the game.");
            return;
        }

        var success = _gameService.ResetGame(player.Id);
        if (!success)
        {
            await Clients.Caller.SendAsync("Error", "Cannot reset the game.");
            return;
        }

        await Clients.All.SendAsync("GameReset");
        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    public async Task SendChat(string message)
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        if (string.IsNullOrWhiteSpace(message)) return;

        var formattedMessage = $"{player.Name}: {message}";
        _gameService.State.ChatMessages.Add(formattedMessage);

        await Clients.All.SendAsync("ChatMessage", player.Name, message);
        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    public async Task KickPlayer(string targetPlayerId)
    {
        var player = GetCallerPlayer();
        if (player == null)
        {
            await Clients.Caller.SendAsync("Error", "You are not in the game.");
            return;
        }

        if (!player.IsHost)
        {
            await Clients.Caller.SendAsync("Error", "Only the host can kick players.");
            return;
        }

        var target = _gameService.State.Players.FirstOrDefault(p => p.Id == targetPlayerId);
        var targetName = target?.Name ?? "Unknown";

        var success = _gameService.KickPlayer(player.Id, targetPlayerId);
        if (!success)
        {
            await Clients.Caller.SendAsync("Error", "Cannot kick that player.");
            return;
        }

        // Notify the kicked player's connection if we can find it
        var kickedConnection = _connectionPlayerMap.FirstOrDefault(kvp => kvp.Value == targetPlayerId).Key;
        if (kickedConnection != null)
        {
            await Clients.Client(kickedConnection).SendAsync("Kicked", "You have been kicked from the game.");
            _connectionPlayerMap.TryRemove(kickedConnection, out _);
        }

        await Clients.All.SendAsync("PlayerLeft", targetName);
        await Clients.All.SendAsync("StateUpdated", _gameService.State);
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────────

    private Player? GetCallerPlayer()
    {
        var connectionId = Context.ConnectionId;
        if (!_connectionPlayerMap.TryGetValue(connectionId, out var playerId))
            return null;

        return _gameService.State.Players.FirstOrDefault(p => p.Id == playerId);
    }
}
