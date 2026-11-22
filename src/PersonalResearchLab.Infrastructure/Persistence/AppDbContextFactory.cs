using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PersonalResearchLab.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c>.
/// <para>
/// It exists so migrations can be generated without adding a design-time dependency to the web
/// project. The connection string is only used to build the model; no connection is opened when
/// scaffolding a migration.
/// </para>
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string FallbackConnectionString =
        "Server=localhost;Database=PersonalResearchLab;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("PERSONALRESEARCHLAB_CONNECTION")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
