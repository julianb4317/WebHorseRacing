namespace HorseRacingGame.Models;

public class Player
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ConnectionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Balance { get; set; } = 100m;
    public List<Card> Cards { get; set; } = new();
    public bool IsHost { get; set; } = false;
    public bool IsConnected { get; set; } = true;
    public List<string> BalanceHistory { get; set; } = new();
}
