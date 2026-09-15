namespace GameOfDrones.Api.Models;

public class Round
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int Number { get; set; }
    public int Move1Id { get; set; }
    public int Move2Id { get; set; }
    public int? WinnerId { get; set; }
}