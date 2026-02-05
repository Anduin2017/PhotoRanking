
using Anduin.PhotoRanking.Services;

namespace Anduin.PhotoRanking;

public static class ProgramExtends
{
    public static void StartSeed(this IHost host)
    {
        _ = Task.Run(async () =>
        {
            using var scope = host.Services.CreateScope();
            
            // Init Vector Storage First
            var vectorStorage = scope.ServiceProvider.GetRequiredService<VectorStorageService>();
            await vectorStorage.InitializeAsync();
            
            var seeder = scope.ServiceProvider.GetRequiredService<SeederService>();
            await seeder.SeedAsync();
            return host;
        });
    }
}
