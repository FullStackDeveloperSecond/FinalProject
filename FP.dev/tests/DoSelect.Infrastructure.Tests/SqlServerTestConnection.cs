using Microsoft.Data.SqlClient;

namespace DoSelect.Infrastructure.Tests;

internal static class SqlServerTestConnection
{
    private const string ConnectionStringEnvironmentVariable =
        "DOSELECT_SQLSERVER_TEST_CONNECTION";
    private const string LocalServer = "Server=.\\SQL2025;";

    public static string Build(string databaseName)
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        var builder = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? LocalServer : configured)
        {
            InitialCatalog = databaseName,
            TrustServerCertificate = true,
        };

        if (string.IsNullOrWhiteSpace(configured))
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }
}
