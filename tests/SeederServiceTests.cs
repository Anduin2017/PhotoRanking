using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Aiursoft.Canon;

namespace Anduin.PhotoRanking.Tests;

[TestClass]
public class SeederServiceTests
{
    private AppDbContext _context = null!;
    private string _tempPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "PhotoRankingTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPath);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        _context = new AppDbContext(options);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [TestMethod]
    public async Task SeedAsync_ShouldAddAndThenRemovePhotos()
    {
        // 1. Setup initial files
        var album1Path = Path.Combine(_tempPath, "Album1");
        Directory.CreateDirectory(album1Path);
        var photo1Path = Path.Combine(album1Path, "photo1.jpg");
        var photo2Path = Path.Combine(album1Path, "photo2.jpg");
        await File.WriteAllBytesAsync(photo1Path, [0x01]);
        await File.WriteAllBytesAsync(photo2Path, [0x01]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoRootPath"] = _tempPath
            })
            .Build();

        var loggerMock = new Mock<ILogger<SeederService>>();
        var analysisService = new ImageAnalysisService();
        var canonPool = new CanonPool(new Mock<ILogger<CanonPool>>().Object);
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(x => x.GetService(typeof(AppDbContext))).Returns(_context);
        scopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var seeder = new SeederService(_context, configuration, loggerMock.Object, analysisService, scopeFactoryMock.Object, canonPool);

        // 2. Initial seed
        await seeder.SeedAsync();

        Assert.AreEqual(1, await _context.Albums.CountAsync(), "Should have 1 album");
        Assert.AreEqual(2, await _context.Photos.CountAsync(), "Should have 2 photos");

        // 3. Delete one photo from disk
        File.Delete(photo1Path);

        // 4. Seed again
        await seeder.SeedAsync();

        Assert.AreEqual(1, await _context.Albums.CountAsync(), "Should still have 1 album");
        Assert.AreEqual(1, await _context.Photos.CountAsync(), "Should have 1 photo after deletion");
        Assert.IsFalse(await _context.Photos.AnyAsync(p => p.FilePath == "Album1/photo1.jpg"), "photo1.jpg should be removed");
        Assert.IsTrue(await _context.Photos.AnyAsync(p => p.FilePath == "Album1/photo2.jpg"), "photo2.jpg should still exist");
    }

    [TestMethod]
    public async Task SeedAsync_ShouldRemoveAlbumWhenAllPhotosDeleted()
    {
        // 1. Setup initial files
        var album1Path = Path.Combine(_tempPath, "Album1");
        Directory.CreateDirectory(album1Path);
        var photo1Path = Path.Combine(album1Path, "photo1.jpg");
        await File.WriteAllBytesAsync(photo1Path, [0x01]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoRootPath"] = _tempPath
            })
            .Build();

        var loggerMock = new Mock<ILogger<SeederService>>();
        var analysisService = new ImageAnalysisService();
        var canonPool = new CanonPool(new Mock<ILogger<CanonPool>>().Object);
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(x => x.GetService(typeof(AppDbContext))).Returns(_context);
        scopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var seeder = new SeederService(_context, configuration, loggerMock.Object, analysisService, scopeFactoryMock.Object, canonPool);

        // 2. Initial seed
        await seeder.SeedAsync();

        Assert.AreEqual(1, await _context.Albums.CountAsync());
        Assert.AreEqual(1, await _context.Photos.CountAsync());

        // 3. Delete the photo (and thus the album becomes empty of photos)
        File.Delete(photo1Path);

        // 4. Seed again
        await seeder.SeedAsync();

        Assert.AreEqual(0, await _context.Photos.CountAsync(), "Photo should be removed");
        Assert.AreEqual(0, await _context.Albums.CountAsync(), "Album should be removed because it has no photos anymore");
    }

    [TestMethod]
    public async Task SeedAsync_ShouldRemoveAlbumWhenDirectoryDeleted()
    {
         // 1. Setup initial files
        var album1Path = Path.Combine(_tempPath, "Album1");
        Directory.CreateDirectory(album1Path);
        var photo1Path = Path.Combine(album1Path, "photo1.jpg");
        await File.WriteAllBytesAsync(photo1Path, [0x01]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoRootPath"] = _tempPath
            })
            .Build();

        var loggerMock = new Mock<ILogger<SeederService>>();
        var analysisService = new ImageAnalysisService();
        var canonPool = new CanonPool(new Mock<ILogger<CanonPool>>().Object);
        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var serviceProviderMock = new Mock<IServiceProvider>();

        serviceProviderMock.Setup(x => x.GetService(typeof(AppDbContext))).Returns(_context);
        scopeMock.Setup(x => x.ServiceProvider).Returns(serviceProviderMock.Object);
        scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

        var seeder = new SeederService(_context, configuration, loggerMock.Object, analysisService, scopeFactoryMock.Object, canonPool);

        // 2. Initial seed
        await seeder.SeedAsync();

        Assert.AreEqual(1, await _context.Albums.CountAsync());
        Assert.AreEqual(1, await _context.Photos.CountAsync());

        // 3. Delete the album directory
        Directory.Delete(album1Path, true);

        // 4. Seed again
        await seeder.SeedAsync();

        Assert.AreEqual(0, await _context.Photos.CountAsync());
        Assert.AreEqual(0, await _context.Albums.CountAsync());
    }
}