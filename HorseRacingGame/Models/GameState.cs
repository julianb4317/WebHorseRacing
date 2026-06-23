namespace HorseRacingGame.Models;

public class GameState
{
    public List<Player> Players { get; set; } = new();
    public List<Horse> Horses { get; set; } = new();
    public GamePhase Phase { get; set; } = GamePhase.Lobby;
    public int ScratchCount { get; set; } = 0;
    public int CurrentPlayerIndex { get; set; } = 0;
    public decimal CentralPot { get; set; } = 0m;
    public List<PotContribution> PotContributions { get; set; } = new();
    public int? LastDie1 { get; set; }
    public int? LastDie2 { get; set; }
    public int? LastSum { get; set; }
    public string LastRollResult { get; set; } = string.Empty;
    public int RoundNumber { get; set; } = 1;
    public Horse? WinningHorse { get; set; }
    public List<Player> WinningPlayers { get; set; } = new();
    public decimal TotalPotWon { get; set; } = 0m;
    public List<PayoutDetail> PayoutDetails { get; set; } = new();
    public List<string> ChatMessages { get; set; } = new();
    public List<string> KickedDeviceIds { get; set; } = new();
}

public class PotContribution
{
    public string PlayerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class PayoutDetail
{
    public string PlayerName { get; set; } = string.Empty;
    public int CardCount { get; set; }
    public decimal AmountWon { get; set; }
}
