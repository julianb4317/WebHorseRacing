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

    public async Task JoinGame(string playerName)
    {
        var connectionId = Context.ConnectionId;

        // Try reconnect first
        var player = _gameService.ReconnectPlayer(playerName, connectionId);
        if (player == null)
        {
            player = _gameService.AddPlayer(playerName, connectionId);
        }

        // Store mapping
        _connectionPlayerMap[connectionId] = player.Id;

        await Clients.All.SendAsync("PlayerJoined", playerName);
        await Clients.Caller.SendAsync("PlayerJoined", playerName);
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
        var connectedPlayers = _gameService.State.Players.Where(p => p.IsConnected).ToList();
        if (connectedPlayers.Count == 0) return;

        var currentPlayer = connectedPlayers[_gameService.State.CurrentPlayerIndex % connectedPlayers.Count];
        if (currentPlayer.Id != player.Id)
        {
            await Clients.Caller.SendAsync("Error", "It's not your turn.");
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
            if (state.Phase == GamePhase.Racing || (state.Phase == GamePhase.Scratching && horse.Scratched && horse.ScratchOrder == state.ScratchCount))
            {
                // Horse was just scratched
                if (horse.Scratched && horse.ScratchPenalty.HasValue &&
                    rollResult.Contains("scratched!"))
                {
                    await Clients.All.SendAsync("HorseScratched", horse.Number, horse.ScratchPenalty.Value);
                }
            }

            if (!horse.Scratched && state.Phase == GamePhase.Racing)
            {
                await Clients.All.SendAsync("HorseMoved", horse.Number, horse.Position);
            }
        }

        // Check if race was won
        if (state.Phase == GamePhase.Ended && state.WinningHorse != null)
        {
            var winnerNames = state.WinningPlayers.Select(p => p.Name).ToList();
            await Clients.All.SendAsync("RaceWon", state.WinningHorse.Number, winnerNames);

            // Build payout summary
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

        // Validate horse exists and is not scratched
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

        // Record the bet as a pot contribution
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

        // Build payout summary
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

    // ─── HELPERS ────────────────────────────────────────────────────────────────

    private Player? GetCallerPlayer()
    {
        var connectionId = Context.ConnectionId;
        if (!_connectionPlayerMap.TryGetValue(connectionId, out var playerId))
            return null;

        return _gameService.State.Players.FirstOrDefault(p => p.Id == playerId);
    }
}
