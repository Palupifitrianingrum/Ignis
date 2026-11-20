using Ignis.Api.Support;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Ignis.Tests;

public class DatabaseTargetTests
{
    private static IConfiguration Cfg(params (string, string?)[] kv) =>
        new ConfigurationBuilder().AddInMemoryCollection(kv.Select(x => new KeyValuePair<string, string?>(x.Item1, x.Item2))).Build();

    [Fact]
    public void Uri_IsConvertedWithSsl_AndEncodedPasswordDecoded()
    {
        var cs = new NpgsqlConnectionStringBuilder(DatabaseTarget.NormalizeSupabase(
            "postgresql://postgres.abcd:p%40ss%3Aw0rd@aws-0-ap-southeast-1.pooler.supabase.com:5432/postgres"));
        Assert.Equal("aws-0-ap-southeast-1.pooler.supabase.com", cs.Host);
        Assert.Equal(5432, cs.Port);
        Assert.Equal("postgres.abcd", cs.Username);
        Assert.Equal("p@ss:w0rd", cs.Password);
        Assert.Equal("postgres", cs.Database);
        Assert.Equal(SslMode.Require, cs.SslMode);
    }

    [Fact]
    public void KeyValueString_GetsSsl()
    {
        var cs = new NpgsqlConnectionStringBuilder(DatabaseTarget.NormalizeSupabase("Host=db.x.supabase.co;Username=postgres;Password=abc;Database=postgres"));
        Assert.Equal(SslMode.Require, cs.SslMode);
    }

    [Fact]
    public void BadUri_Throws() =>
        Assert.Throws<InvalidOperationException>(() => DatabaseTarget.NormalizeSupabase("postgresql://"));

    [Fact]
    public void SupabaseFilled_WinsOverLocalPostgres()
    {
        var t = DatabaseTarget.Resolve(Cfg(("Storage:Provider", "Postgres"), ("Supabase:DbUrl", "postgresql://u:p@h.supabase.co:5432/postgres"),
                                           ("ConnectionStrings:Ignis", "Host=localhost")));
        Assert.True(t.IsSupabase);
        Assert.Contains("h.supabase.co", t.ConnectionString);
    }

    [Fact]
    public void SupabaseEmpty_FallsBackToLocalPostgres()
    {
        var t = DatabaseTarget.Resolve(Cfg(("Storage:Provider", "Postgres"), ("Supabase:DbUrl", ""), ("ConnectionStrings:Ignis", "Host=localhost")));
        Assert.False(t.IsSupabase);
        Assert.Equal("Host=localhost", t.ConnectionString);
        Assert.Equal("Postgres", t.Provider);
    }

    [Fact]
    public void ExplicitInMemory_BeatsSupabase()
    {
        var t = DatabaseTarget.Resolve(Cfg(("Storage:Provider", "InMemory"), ("Supabase:DbUrl", "postgresql://u:p@h:5432/postgres")));
        Assert.Equal("InMemory", t.Provider);
        Assert.False(t.IsSupabase);
    }

    [Fact]
    public void NothingSet_IsInMemory() => Assert.Equal("InMemory", DatabaseTarget.Resolve(Cfg()).Provider);
}
