
using Anduin.PhotoRanking.Services;

namespace Anduin.PhotoRanking;

public static class ProgramExtends
{
    public static void StartSeed(this IHost host)
    {
        _ = Task.Run(async () =>
        {
            using var scope = host.Services.CreateScope();
            var seeder = scope.ServiceProvider.GetRequiredService<SeederService>();
            await seeder.SeedAsync();
            return host;
        });
    }
}
