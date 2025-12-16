using Anduin.PhotoRanking.Entities;
using Microsoft.EntityFrameworkCore;


namespace Anduin.PhotoRanking.Sqlite;

public class SqliteContext(DbContextOptions<SqliteContext> options) : TemplateDbContext(options)
{
    public override Task<bool> CanConnectAsync()
    {
        return Task.FromResult(true);
    }
}
