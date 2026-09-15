public record MoveDto(int Id, string Name);
public record RuleDto(int WinnerMoveId, string WinnerName, int LoserMoveId, string LoserName);
public record GameDto(int Id, string Player1Name, string Player2Name);
public record StatDto(string Name, int GamesWon);
public record RoundResult(int Number, string? RoundWinner, int Player1Wins, int Player2Wins, string? GameWinner);

public record NewMoveReq(string? Name);
public record RuleReq(int WinnerMoveId, int LoserMoveId);
public record NewGameReq(string? Player1Name, string? Player2Name);
public record RoundReq(int Move1Id, int Move2Id);