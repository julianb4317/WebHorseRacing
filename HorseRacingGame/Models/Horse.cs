namespace HorseRacingGame.Models;

public class Horse
{
    public int Number { get; set; }
    public int Position { get; set; } = 0;
    public int TotalSpaces { get; set; }
    public bool Scratched { get; set; } = false;
    public int? ScratchOrder { get; set; }
    public decimal? ScratchPenalty { get; set; }
    public bool Finished { get; set; } = false;
    public int? FinishPlace { get; set; }
}
