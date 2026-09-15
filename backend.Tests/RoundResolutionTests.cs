using GameOfDrones.Api.Data;
using GameOfDrones.Api.Endpoints;
using GameOfDrones.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameOfDrones.Tests;

public class RoundResolutionTests
{
    const int Rock = 1, Paper = 2, Scissors = 3, Dog = 4;
    const int Player1 = 10, Player2 = 20;

    static AppDbContext ContextWith(params MoveRule[] rules)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);
        db.MoveRules.AddRange(rules);
        db.SaveChanges();
        return db;
    }

    static AppDbContext ClassicRules() => ContextWith(
        new MoveRule { WinnerMoveId = Paper, LoserMoveId = Rock },
        new MoveRule { WinnerMoveId = Rock, LoserMoveId = Scissors },
        new MoveRule { WinnerMoveId = Scissors, LoserMoveId = Paper });

    [Fact]
    public async Task Player1_wins_when_their_move_beats_the_other()
    {
        using var db = ClassicRules();

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Rock, Scissors, Player1, Player2);

        Assert.Equal(Player1, winner);
    }

    [Fact]
    public async Task Player2_wins_when_their_move_beats_the_other()
    {
        using var db = ClassicRules();

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Scissors, Rock, Player1, Player2);

        Assert.Equal(Player2, winner);
    }

    [Fact]
    public async Task Same_move_is_a_tie()
    {
        using var db = ClassicRules();

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Rock, Rock, Player1, Player2);

        Assert.Null(winner);
    }

    [Fact]
    public async Task Unrelated_moves_are_a_tie()
    {
        // "Dog beats Paper" is the only rule involving Dog: Dog vs Rock relates to nothing.
        using var db = ContextWith(new MoveRule { WinnerMoveId = Dog, LoserMoveId = Paper });

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Dog, Rock, Player1, Player2);

        Assert.Null(winner);
    }

    [Fact]
    public async Task A_rule_added_at_runtime_decides_the_round()
    {
        using var db = ContextWith(new MoveRule { WinnerMoveId = Dog, LoserMoveId = Paper });

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Paper, Dog, Player1, Player2);

        Assert.Equal(Player2, winner);
    }

    [Fact]
    public async Task Without_rules_every_round_is_a_tie()
    {
        using var db = ContextWith();

        var winner = await ApiEndpoints.ResolveWinnerAsync(db, Rock, Scissors, Player1, Player2);

        Assert.Null(winner);
    }
}
