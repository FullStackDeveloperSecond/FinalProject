using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DoSelect.Infrastructure.Persistence;

public sealed class DoSelectDbContextFactory
    : IDesignTimeDbContextFactory<DoSelectDbContext>
{
    private const string DefaultLocalConnectionString =
        "Server=.\\SQL2025;Database=DoSelectDb;Trusted_Connection=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True;";

    public DoSelectDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultLocalConnectionString;
        }

        var options = new DbContextOptionsBuilder<DoSelectDbContext>()
            .UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(DoSelectDbContext).Assembly.FullName))
            .Options;

        return new DoSelectDbContext(options);
    }
}
