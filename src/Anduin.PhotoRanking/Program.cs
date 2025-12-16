using Aiursoft.DbTools;
using Anduin.PhotoRanking.Entities;
using static Aiursoft.WebTools.Extends;

namespace Anduin.PhotoRanking;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        var app = await AppAsync<Startup>(args);
        await app.UpdateDbAsync<TemplateDbContext>();
        await app.SeedAsync();
        await app.CopyAvatarFileAsync();
        await app.RunAsync();
    }
}
