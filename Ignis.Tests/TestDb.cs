using Ignis.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ignis.Tests;

/// <summary>Database EF InMemory yang terisolasi per tes (nama unik), opsional berisi data demo.</summary>
public static class TestDb
{
    public static AppDbContext Create(bool seedDemo = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("tes-" + Guid.NewGuid()).Options;
        var db = new AppDbContext(options);
        if (seedDemo)
        {
            db.Users.AddRange(DemoData.Users());
            db.Hotspots.AddRange(DemoData.Hotspots());
            db.Predictions.AddRange(DemoData.Predictions());
            db.SaveChanges();
        }
        return db;
    }
}
