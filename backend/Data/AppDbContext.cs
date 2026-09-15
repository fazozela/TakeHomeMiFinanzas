using GameOfDrones.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GameOfDrones.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Move> Moves => Set<Move>();
    public DbSet<MoveRule> MoveRules => Set<MoveRule>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<Round> Rounds => Set<Round>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Move>().HasIndex(m => m.Name).IsUnique();
        b.Entity<Player>().HasIndex(p => p.Name).IsUnique();

        b.Entity<MoveRule>(e =>
        {
            e.HasKey(r => new { r.WinnerMoveId, r.LoserMoveId });
            e.HasOne<Move>().WithMany().HasForeignKey(r => r.WinnerMoveId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Move>().WithMany().HasForeignKey(r => r.LoserMoveId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Game>(e =>
        {
            e.HasOne<Player>().WithMany().HasForeignKey(g => g.Player1Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(g => g.Player2Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Player>().WithMany().HasForeignKey(g => g.WinnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Round>(e =>
        {
            e.HasOne<Move>().WithMany().HasForeignKey(r => r.Move1Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Move>().WithMany().HasForeignKey(r => r.Move2Id).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Game>().WithMany().HasForeignKey(r => r.GameId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Player>().WithMany().HasForeignKey(r => r.WinnerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Move>().HasData(
            new Move { Id = 1, Name = "Rock" },
            new Move { Id = 2, Name = "Paper" },
            new Move { Id = 3, Name = "Scissors" });

        b.Entity<MoveRule>().HasData(
            new MoveRule { WinnerMoveId = 2, LoserMoveId = 1 },
            new MoveRule { WinnerMoveId = 1, LoserMoveId = 3 },
            new MoveRule { WinnerMoveId = 3, LoserMoveId = 2 });
    }
}