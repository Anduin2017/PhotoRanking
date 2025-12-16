using Aiursoft.DbTools;
using Anduin.PhotoRanking.Models;
using Microsoft.EntityFrameworkCore;

namespace Anduin.PhotoRanking.Data;

public class  AppDbContext(DbContextOptions options) : DbContext(options), ICanMigrate
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<RatingLog> RatingLogs => Set<RatingLog>();

    public Task MigrateAsync(CancellationToken cancellationToken)
    {
        return Database.MigrateAsync(cancellationToken);
    }

    public Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
