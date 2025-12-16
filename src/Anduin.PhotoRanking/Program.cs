using Aiursoft.DbTools;
using Anduin.PhotoRanking.Data;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await AppAsync<Startup>(args);
        await app.UpdateDbAsync<AppDbContext>();
        app.StartSeed();
        await app.RunAsync();
    }
}
