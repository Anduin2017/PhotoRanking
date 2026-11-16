using System.Globalization;
using Anduin.PhotoRanking.Controllers;
using Anduin.PhotoRanking.Data;
using Anduin.PhotoRanking.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Anduin.PhotoRanking.Tests.Services;

[TestClass]
public class AlbumOrderingTests
{
    [TestMethod]
    public async Task AlbumOrderingAndSortNamesAreIndependentOfTheServerCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite("DataSource=:memory:").Options);
                await db.Database.OpenConnectionAsync();
                await db.Database.EnsureCreatedAsync();
                db.Albums.Add(new Album { AlbumId = "test", Name = "Test" });
                db.Photos.AddRange(
                    new Photo { AlbumId = "test", FilePath = "a.jpg", EstimatedScore = 6 },
                    new Photo { AlbumId = "test", FilePath = "B.jpg", EstimatedScore = 3 });
                await db.SaveChangesAsync();
                var controller = new AlbumsController(db);
                var filename = await controller.GetAlbum("test");
                var body = JObject.FromObject(((OkObjectResult)filename.Result!).Value!);
                CollectionAssert.AreEqual(new[] { "B.jpg", "a.jpg" },
                    body["Photos"]!.Select(p => p["FilePath"]!.Value<string>()).ToArray());
                var predicted = await controller.GetAlbum("test", "ESTIMATEDSCORE");
                body = JObject.FromObject(((OkObjectResult)predicted.Result!).Value!);
                CollectionAssert.AreEqual(new[] { "a.jpg", "B.jpg" },
                    body["Photos"]!.Select(p => p["FilePath"]!.Value<string>()).ToArray());
                var paged = await controller.GetAlbumPhotos("test", sortBy: "ESTIMATEDSCORE");
                var photos = (IEnumerable<Photo>)((OkObjectResult)paged.Result!).Value!;
                CollectionAssert.AreEqual(new[] { "a.jpg", "B.jpg" }, photos.Select(p => p.FilePath).ToArray());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
