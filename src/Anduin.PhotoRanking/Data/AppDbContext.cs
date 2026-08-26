using Aiursoft.DbTools;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Data;

public class  AppDbContext(DbContextOptions options) : DbContext(options), ICanMigrate
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<RatingLog> RatingLogs => Set<RatingLog>();
    public DbSet<SystemState> SystemStates => Set<SystemState>();
    public DbSet<PredictionModel> PredictionModels => Set<PredictionModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Photo>().HasIndex(p => p.EstimatedScore);
        modelBuilder.Entity<Photo>().HasIndex(p => p.IndependentScore);
        modelBuilder.Entity<Photo>().HasIndex(p => p.PredictionUncertainty);
        modelBuilder.Entity<Photo>().HasIndex(p => p.PredictionNovelty);
    }

    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        return Database.MigrateAsync(cancellationToken);
    }

    public Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
