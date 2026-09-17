using HR.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HR.Tests;

/// <summary>Ephemeral in-memory SQLite backing a HrDbContext, shared across contexts.</summary>
internal sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using (var options = OpenContext())
        {
            options.Database.EnsureCreated();
        }
    }

    public HrDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<HrDbContext>().UseSqlite(_connection).Options;
        return new HrDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
