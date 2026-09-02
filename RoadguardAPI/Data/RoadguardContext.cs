using Microsoft.EntityFrameworkCore;
using RoadguardAPI.Models;

namespace RoadguardAPI.Data;

public class RoadguardContext : DbContext
{
    public RoadguardContext(DbContextOptions<RoadguardContext> options)
        : base(options)
    {
    }

    public DbSet<Player> Players { get; set; }

    public DbSet<LevelResult> LevelResults { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>()
            .HasMany(p => p.LevelResults)
            .WithOne(r => r.Player)
            .HasForeignKey(r => r.PlayerId);

        base.OnModelCreating(modelBuilder);
    }
}