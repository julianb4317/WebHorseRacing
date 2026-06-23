using HorseRacingGame.Models;

namespace HorseRacingGame.Services;

public class GameService
{
    private readonly object _lock = new();
    private List<Card> _deck = new();

    public GameState State { get; private set; } = new();
    public event Action? OnStateChanged;

    public GameService()
    {
        InitializeGame();
    }

    // ─── DECK MANAGEMENT ────────────────────────────────────────────────────────

    private List<Card> BuildDeck()
    {
        var suits = new[] { "Hearts", "Diamonds", "Clubs", "Spades" };
        var suitSymbols = new Dictionary<string, string>
        {
            ["Hearts"] = "♥",
            ["Diamonds"] = "♦",
            ["Clubs"] = "♣",
            ["Spades"] = "♠"
        };

        var deck = new List<Card>();

        foreach (var suit in suits)
        {
            // Values 2-12 (2-10 face value, Jack=11, Queen=12). No Aces, Kings, Jokers.
            for (int v = 2; v <= 12; v++)
            {
                var rankDisplay = v switch
                {
                    11 => "J",
                    12 => "Q",
                    _ => v.ToString()
                };

                deck.Add(new Card
                {
                    Value = v,
                    Suit = suit,
                    Display = $"{rankDisplay}{suitSymbols[suit]}",
                    IsActive = true
                });
            }
        }

        return deck; // 44 cards: 4 suits × 11 values (2-Q)
    }

    private void ShuffleDeck(List<Card> deck)
    {
        // Fisher-Yates shuffle
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
    }

    // ─── HORSE INITIALIZATION ───────────────────────────────────────────────────

    private List<Horse> BuildHorses()
    {
        var totalSpacesMap = new Dictionary<int, int>
        {
            [2] = 2, [3] = 5, [4] = 7, [5] = 8, [6] = 12,
            [7] = 16, [8] = 12, [9] = 8, [10] = 7, [11] = 5, [12] = 2
        };

        var horses = new List<Horse>();
        for (int i = 2; i <= 12; i++)
        {
            horses.Add(new Horse
            {
                Number = i,
                Position = 0,
                TotalSpaces = totalSpacesMap[i],
                Scratched = false,
                ScratchOrder = null,
                ScratchPenalty = null,
                Finished = false,
                FinishPlace = null
            });
        }

        return horses;
    }

    // ─── PLAYER MANAGEMENT ──────────────────────────────────────────────────────

    public Player AddOrReconnectPlayer(string name, string connectionId, string deviceId)
    {
        lock (_lock)
        {
            // First: check if this device already has a player in the game
            var existingByDevice = State.Players.FirstOrDefault(p => p.DeviceId == deviceId);
            if (existingByDevice != null)
            {
                // Same device reconnecting (possibly with different connection)
                existingByDevice.ConnectionId = connectionId;
                existingByDevice.IsConnected = true;
                existingByDevice.Name = name; // allow name update
                EnsureHost();
                NotifyStateChanged();
                return existingByDevice;
            }

            // Second: check if there's a disconnected player with this name
            var existingByName = State.Players.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !p.IsConnected);
            if (existingByName != null)
            {
                existingByName.ConnectionId = connectionId;
                existingByName.DeviceId = deviceId;
                existingByName.IsConnected = true;
                State.ChatMessages.Add($"{existingByName.Name} reconnected.");
                EnsureHost();
                NotifyStateChanged();
                return existingByName;
            }

            // Third: check if there's already a connected player with this name
            var existingConnected = State.Players.FirstOrDefault(p =>
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.IsConnected);
            if (existingConnected != null)
            {
                // Same name already connected — update connection (new tab / page nav)
                existingConnected.ConnectionId = connectionId;
                existingConnected.DeviceId = deviceId;
                EnsureHost();
                NotifyStateChanged();
                return existingConnected;
            }

            // New player
            var player = new Player
            {
                Name = name,
                ConnectionId = connectionId,
                DeviceId = deviceId,
                IsHost = State.Players.Count(p => p.IsConnected) == 0,
                IsConnected = true
            };

            State.Players.Add(player);
            State.ChatMessages.Add($"{name} joined the game.");
            EnsureHost();
            NotifyStateChanged();
            return player;
        }
    }

    public void RemovePlayer(string connectionId)
    {
        lock (_lock)
        {
            var player = State.Players.FirstOrDefault(p => p.ConnectionId == connectionId);
            if (player == null) return;

            // Mark as disconnected (they may reconnect from Game page)
            player.IsConnected = false;
            State.ChatMessages.Add($"{player.Name} disconnected.");

            var connectedPlayers = State.Players.Where(p => p.IsConnected).ToList();

            // If fewer than 2 connected players remain during an active game, end it
            if (connectedPlayers.Count < 2 &&
                (State.Phase == GamePhase.Scratching || State.Phase == GamePhase.Racing))
            {
                State.Phase = GamePhase.Ended;
                State.ChatMessages.Add("Not enough players remaining. Game ended.");
                NotifyStateChanged();
                return;
            }

            // Advance turn index if needed
            if (connectedPlayers.Count > 0 &&
                (State.Phase == GamePhase.Scratching || State.Phase == GamePhase.Racing))
            {
                if (State.CurrentPlayerIndex >= connectedPlayers.Count)
                {
                    State.CurrentPlayerIndex = 0;
                }
            }
            else
            {
                State.CurrentPlayerIndex = 0;
            }

            // Handle host transfer
            if (player.IsHost)
            {
                player.IsHost = false;
                EnsureHost();
            }

            NotifyStateChanged();
        }
    }

    private void EnsureHost()
    {
        var connectedPlayers = State.Players.Where(p => p.IsConnected).ToList();
        if (connectedPlayers.Count == 0) return;

        // Check if any connected player is already host
        var currentHost = connectedPlayers.FirstOrDefault(p => p.IsHost);
        if (currentHost != null) return;

        // Assign host to the first connected player (by index order)
        var newHost = connectedPlayers.First();
        newHost.IsHost = true;
        State.ChatMessages.Add($"{newHost.Name} is now the host.");
    }

    // ─── GAME FLOW METHODS ──────────────────────────────────────────────────────

    public void InitializeGame()
    {
        lock (_lock)
        {
            State.Horses = BuildHorses();
            _deck = BuildDeck();
            ShuffleDeck(_deck);
            State.Phase = GamePhase.Lobby;
            State.ScratchCount = 0;
            State.CurrentPlayerIndex = 0;
            State.CentralPot = 0m;
            State.PotContributions.Clear();
            State.LastDie1 = null;
            State.LastDie2 = null;
            State.LastSum = null;
            State.LastRollResult = string.Empty;
            State.RoundNumber = 1;
            State.WinningHorse = null;
            State.WinningPlayers.Clear();

            foreach (var player in State.Players)
            {
                player.Cards.Clear();
            }

            NotifyStateChanged();
        }
    }

    public bool StartGame(string playerId)
    {
        lock (_lock)
        {
            var player = State.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost) return false;
            if (State.Phase != GamePhase.Lobby) return false;

            // Reset horses and deck
            State.Horses = BuildHorses();
            _deck = BuildDeck();
            ShuffleDeck(_deck);

            State.Phase = GamePhase.Dealing;
            State.ChatMessages.Add("Game started! Dealing cards...");
            NotifyStateChanged();

            DealCards();

            State.Phase = GamePhase.Scratching;
            State.CurrentPlayerIndex = 0;
            State.ChatMessages.Add("Scratching phase — roll to scratch horses!");
            NotifyStateChanged();
            return true;
        }
    }

    private void DealCards()
    {
        var connectedPlayers = State.Players.Where(p => p.IsConnected).ToList();
        if (connectedPlayers.Count == 0) return;

        // Clear existing cards
        foreach (var player in connectedPlayers)
        {
            player.Cards.Clear();
        }

        // Deal 44 cards round-robin
        int playerIndex = 0;
        foreach (var card in _deck)
        {
            connectedPlayers[playerIndex].Cards.Add(card);
            playerIndex = (playerIndex + 1) % connectedPlayers.Count;
        }
    }

    public (int die1, int die2, int sum, string result)? RollDice(string playerId)
    {
        lock (_lock)
        {
            // Validate phase
            if (State.Phase != GamePhase.Scratching && State.Phase != GamePhase.Racing)
                return null;

            // Validate it's this player's turn
            var connectedPlayers = State.Players.Where(p => p.IsConnected).ToList();
            if (connectedPlayers.Count == 0) return null;

            var currentPlayer = connectedPlayers[State.CurrentPlayerIndex % connectedPlayers.Count];
            if (currentPlayer.Id != playerId) return null;

            // Roll two dice
            int die1 = Random.Shared.Next(1, 7);
            int die2 = Random.Shared.Next(1, 7);
            int sum = die1 + die2;

            State.LastDie1 = die1;
            State.LastDie2 = die2;
            State.LastSum = sum;

            string result;

            if (State.Phase == GamePhase.Scratching)
            {
                result = ProcessScratchPhaseRoll(sum);
            }
            else
            {
                result = ProcessRacingPhaseRoll(playerId, sum);
            }

            State.LastRollResult = result;

            // Advance to next player
            AdvanceCurrentPlayer();

            NotifyStateChanged();
            return (die1, die2, sum, result);
        }
    }

    private string ProcessScratchPhaseRoll(int sum)
    {
        var horse = State.Horses.FirstOrDefault(h => h.Number == sum);
        if (horse == null) return $"No horse #{sum} exists.";

        if (horse.Scratched)
        {
            return $"Horse #{sum} is already scratched. Roll again next turn.";
        }

        // Scratch the horse
        State.ScratchCount++;
        horse.Scratched = true;
        horse.ScratchOrder = State.ScratchCount;

        // Assign penalty based on scratch order
        horse.ScratchPenalty = State.ScratchCount switch
        {
            1 => 1m,
            2 => 2m,
            3 => 3m,
            4 => 4m,
            _ => 0m
        };

        // Remove matching cards from all players and charge penalty
        foreach (var player in State.Players.Where(p => p.IsConnected))
        {
            var matchingCards = player.Cards.Where(c => c.Value == sum).ToList();
            if (matchingCards.Count > 0)
            {
                decimal totalPenalty = matchingCards.Count * horse.ScratchPenalty.Value;
                player.Balance -= totalPenalty;
                State.CentralPot += totalPenalty;

                State.PotContributions.Add(new PotContribution
                {
                    PlayerId = player.Id,
                    Amount = totalPenalty,
                    Reason = $"Scratch penalty: {matchingCards.Count}× Horse #{sum} @ ${horse.ScratchPenalty.Value}"
                });

                // Mark cards as inactive
                foreach (var card in matchingCards)
                {
                    card.IsActive = false;
                }

                player.BalanceHistory.Add($"-${totalPenalty} (Horse #{sum} scratched)");
            }
        }

        var result = $"Horse #{sum} scratched! (Penalty: ${horse.ScratchPenalty}/card)";
        State.ChatMessages.Add(result);

        // Check if 4 scratches reached — transition to Racing
        if (State.ScratchCount >= 4)
        {
            State.Phase = GamePhase.Racing;
            State.CurrentPlayerIndex = 0;
            State.ChatMessages.Add("4 horses scratched! Racing phase begins!");
        }

        return result;
    }

    private string ProcessRacingPhaseRoll(string playerId, int sum)
    {
        var horse = State.Horses.FirstOrDefault(h => h.Number == sum);
        if (horse == null) return $"No horse #{sum} exists.";

        if (horse.Scratched)
        {
            // Charge penalty to current player
            var currentPlayer = State.Players.FirstOrDefault(p => p.Id == playerId);
            if (currentPlayer != null && horse.ScratchPenalty.HasValue)
            {
                decimal penalty = horse.ScratchPenalty.Value;
                currentPlayer.Balance -= penalty;
                State.CentralPot += penalty;

                State.PotContributions.Add(new PotContribution
                {
                    PlayerId = currentPlayer.Id,
                    Amount = penalty,
                    Reason = $"Rolled scratched Horse #{sum}"
                });

                currentPlayer.BalanceHistory.Add($"-${penalty} (rolled scratched Horse #{sum})");
            }

            var penaltyResult = $"Horse #{sum} is scratched! Penalty charged.";
            State.ChatMessages.Add(penaltyResult);
            return penaltyResult;
        }

        // Advance the horse
        horse.Position++;

        var result = $"Horse #{sum} advances to position {horse.Position}/{horse.TotalSpaces}";
        State.ChatMessages.Add(result);

        // Check win condition
        if (CheckWinCondition())
        {
            State.Phase = GamePhase.Payout;
            CalculatePayout();
            State.Phase = GamePhase.Ended;
        }
        else
        {
            State.RoundNumber++;
        }

        return result;
    }

    private bool CheckWinCondition()
    {
        var winner = State.Horses.FirstOrDefault(h => !h.Scratched && h.Position >= h.TotalSpaces);
        if (winner != null)
        {
            winner.Finished = true;
            winner.FinishPlace = 1;
            State.WinningHorse = winner;
            State.ChatMessages.Add($"🏆 Horse #{winner.Number} wins the race!");
            return true;
        }
        return false;
    }

    private void CalculatePayout()
    {
        if (State.WinningHorse == null) return;

        int winningNumber = State.WinningHorse.Number;

        // Find players with active cards matching the winning horse
        var winners = State.Players
            .Where(p => p.Cards.Any(c => c.Value == winningNumber && c.IsActive))
            .ToList();

        State.WinningPlayers = winners;

        if (winners.Count == 0)
        {
            State.ChatMessages.Add("No players hold cards for the winning horse! Pot carries over.");
            return;
        }

        // Count total winning cards
        int totalWinningCards = winners.Sum(p => p.Cards.Count(c => c.Value == winningNumber && c.IsActive));

        if (totalWinningCards == 0) return;

        // Split pot per card
        decimal perCard = State.CentralPot / totalWinningCards;

        foreach (var player in winners)
        {
            int cardCount = player.Cards.Count(c => c.Value == winningNumber && c.IsActive);
            decimal winnings = perCard * cardCount;
            player.Balance += winnings;
            player.BalanceHistory.Add($"+${winnings:F2} (won with {cardCount}× Horse #{winningNumber} cards)");
        }

        State.ChatMessages.Add($"Pot of ${State.CentralPot:F2} split among {winners.Count} winner(s) ({totalWinningCards} cards).");
        State.CentralPot = 0m;
    }

    public bool ResetGame(string playerId)
    {
        lock (_lock)
        {
            var player = State.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost) return false;

            // Keep players, reset everything else
            State.Horses = BuildHorses();
            _deck = BuildDeck();
            ShuffleDeck(_deck);
            State.Phase = GamePhase.Lobby;
            State.ScratchCount = 0;
            State.CurrentPlayerIndex = 0;
            State.CentralPot = 0m;
            State.PotContributions.Clear();
            State.LastDie1 = null;
            State.LastDie2 = null;
            State.LastSum = null;
            State.LastRollResult = string.Empty;
            State.RoundNumber = 1;
            State.WinningHorse = null;
            State.WinningPlayers.Clear();
            State.ChatMessages.Clear();

            foreach (var p in State.Players)
            {
                p.Cards.Clear();
            }

            State.ChatMessages.Add("Game reset. Waiting in lobby...");
            NotifyStateChanged();
            return true;
        }
    }

    public bool EndGame(string playerId)
    {
        lock (_lock)
        {
            var player = State.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || !player.IsHost) return false;

            // If mid-race, trigger payout for the leading horse
            if (State.Phase == GamePhase.Racing)
            {
                // Find the horse closest to finishing (by % of track completed)
                var leadingHorse = State.Horses
                    .Where(h => !h.Scratched)
                    .OrderByDescending(h => (double)h.Position / h.TotalSpaces)
                    .FirstOrDefault();

                if (leadingHorse != null)
                {
                    leadingHorse.Finished = true;
                    leadingHorse.FinishPlace = 1;
                    State.WinningHorse = leadingHorse;
                    State.ChatMessages.Add($"Game ended early. Horse #{leadingHorse.Number} declared winner (furthest ahead).");
                    CalculatePayout();
                }
            }

            State.Phase = GamePhase.Ended;
            State.ChatMessages.Add("Game ended.");
            NotifyStateChanged();
            return true;
        }
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────────

    private void AdvanceCurrentPlayer()
    {
        var connectedPlayers = State.Players.Where(p => p.IsConnected).ToList();
        if (connectedPlayers.Count == 0) return;

        State.CurrentPlayerIndex = (State.CurrentPlayerIndex + 1) % connectedPlayers.Count;
    }

    private void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }
}
