using GameOfDrones.Api.Data;
using GameOfDrones.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameOfDrones.Api.Endpoints;

public static class ApiEndpoints
{
    const int WinsNeeded = 3;

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/moves", async (AppDbContext db) =>
            await db.Moves.Select(m => new MoveDto(m.Id, m.Name)).ToListAsync());

        api.MapPost("/moves", async (NewMoveReq req, AppDbContext db) =>
        {
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return Results.BadRequest("Name is required.");
            if (await db.Moves.AnyAsync(m => m.Name == name)) return Results.Conflict($"Move '{name}' already exists.");

            var move = new Move { Name = name };
            db.Moves.Add(move);
            await db.SaveChangesAsync();
            return Results.Ok(new MoveDto(move.Id, move.Name));
        });

        api.MapGet("/rules", async (AppDbContext db) =>
            await (from r in db.MoveRules
                   join w in db.Moves on r.WinnerMoveId equals w.Id
                   join l in db.Moves on r.LoserMoveId equals l.Id
                   select new RuleDto(w.Id, w.Name, l.Id, l.Name)).ToListAsync());

        api.MapPost("/rules", async (RuleReq req, AppDbContext db) =>
        {
            if (req.WinnerMoveId == req.LoserMoveId)
                return Results.BadRequest("A move cannot beat itself.");
            if (!await db.Moves.AnyAsync(m => m.Id == req.WinnerMoveId) ||
                !await db.Moves.AnyAsync(m => m.Id == req.LoserMoveId))
                return Results.BadRequest("Unknown move.");
            if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.LoserMoveId && r.LoserMoveId == req.WinnerMoveId))
                return Results.Conflict("The opposite rule already exists.");
            if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.WinnerMoveId && r.LoserMoveId == req.LoserMoveId))
                return Results.Conflict("Rule already exists.");

            db.MoveRules.Add(new MoveRule { WinnerMoveId = req.WinnerMoveId, LoserMoveId = req.LoserMoveId });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/rules/{winnerId:int}/{loserId:int}", async (int winnerId, int loserId, AppDbContext db) =>
        {
            var rule = await db.MoveRules.FindAsync(winnerId, loserId);
            if (rule is null) return Results.NotFound();

            db.MoveRules.Remove(rule);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/games", async (NewGameReq req, AppDbContext db) =>
        {
            var n1 = req.Player1Name?.Trim();
            var n2 = req.Player2Name?.Trim();
            if (string.IsNullOrEmpty(n1) || string.IsNullOrEmpty(n2))
                return Results.BadRequest("Both player names are required.");
            if (string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest("Players must have different names.");

            var p1 = await GetOrCreatePlayer(db, n1);
            var p2 = await GetOrCreatePlayer(db, n2);

            var game = new Game { Player1Id = p1.Id, Player2Id = p2.Id };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            return Results.Ok(new GameDto(game.Id, p1.Name, p2.Name));
        });

        api.MapPost("/games/{id:int}/rounds", async (int id, RoundReq req, AppDbContext db) =>
        {
            var game = await db.Games.FindAsync(id);
            if (game is null) return Results.NotFound();
            if (game.WinnerId is not null) return Results.Conflict("Game already finished.");
            if (!await db.Moves.AnyAsync(m => m.Id == req.Move1Id) ||
                !await db.Moves.AnyAsync(m => m.Id == req.Move2Id))
                return Results.BadRequest("Unknown move.");

            int? roundWinnerId = null;
            if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.Move1Id && r.LoserMoveId == req.Move2Id))
                roundWinnerId = game.Player1Id;
            else if (await db.MoveRules.AnyAsync(r => r.WinnerMoveId == req.Move2Id && r.LoserMoveId == req.Move1Id))
                roundWinnerId = game.Player2Id;

            var number = await db.Rounds.CountAsync(r => r.GameId == id) + 1;
            db.Rounds.Add(new Round
            {
                GameId = id, Number = number,
                Move1Id = req.Move1Id, Move2Id = req.Move2Id,
                WinnerId = roundWinnerId
            });
            await db.SaveChangesAsync();

            var wins1 = await db.Rounds.CountAsync(r => r.GameId == id && r.WinnerId == game.Player1Id);
            var wins2 = await db.Rounds.CountAsync(r => r.GameId == id && r.WinnerId == game.Player2Id);

            if (wins1 >= WinsNeeded) game.WinnerId = game.Player1Id;
            else if (wins2 >= WinsNeeded) game.WinnerId = game.Player2Id;
            if (game.WinnerId is not null) await db.SaveChangesAsync();

            var names = await db.Players
                .Where(p => p.Id == game.Player1Id || p.Id == game.Player2Id)
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            return Results.Ok(new RoundResult(
                number,
                roundWinnerId is null ? null : names[roundWinnerId.Value],
                wins1, wins2,
                game.WinnerId is null ? null : names[game.WinnerId.Value]));
        });

        api.MapGet("/stats", async (AppDbContext db) =>
            await db.Players
                .OrderByDescending(p => db.Games.Count(g => g.WinnerId == p.Id))
                .ThenBy(p => p.Name)
                .Select(p => new StatDto(p.Name, db.Games.Count(g => g.WinnerId == p.Id)))
                .ToListAsync());
    }

    static async Task<Player> GetOrCreatePlayer(AppDbContext db, string name)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Name == name);
        if (player is not null) return player;

        player = new Player { Name = name };
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }
}