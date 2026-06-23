namespace HorseRacingGame.Models;

public class Card
{
    public int Value { get; set; }
    public string Suit { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
