using System.Data;
using System.Data.Common;
using DoSelect.Application.Checkout;
using DoSelect.Domain.Orders;
using DoSelect.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DoSelect.Infrastructure.Checkout;

public sealed class SqlOrderNumberGenerator : IOrderNumberGenerator
{
    private const int LockTimeoutMilliseconds = 10_000;
    private readonly DoSelectDbContext _dbContext;

    public SqlOrderNumberGenerator(DoSelectDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Order-number time must be UTC.", nameof(nowUtc));
        }

        var transaction = _dbContext.Database.CurrentTransaction ??
            throw new InvalidOperationException(
                "Order-number allocation requires the caller-owned SQL transaction.");
        var businessDate = DateOnly.FromDateTime(nowUtc.AddHours(8));
        var prefix = OrderNumber.DailyPrefix(businessDate);
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await AcquireLockAsync(connection, transaction, prefix, cancellationToken);
        var latest = await ReadLatestAsync(connection, transaction, prefix, cancellationToken);
        var sequence = latest is null ? 1 : ParseSequence(latest, prefix) + 1;
        if (sequence > OrderNumber.MaximumDailySequence)
        {
            throw new InvalidOperationException(
                $"The daily order-number capacity for {businessDate:yyyy-MM-dd} is exhausted.");
        }

        return OrderNumber.Create(businessDate, sequence);
    }

    private static async Task AcquireLockAsync(
        DbConnection connection,
        IDbContextTransaction transaction,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @lockTimeout;
            SELECT @result;
            """;
        AddParameter(command, "@resource", $"DoSelect:OrderNumber:{prefix}");
        AddParameter(command, "@lockTimeout", LockTimeoutMilliseconds);
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
        {
            throw new TimeoutException("Could not acquire the daily order-number lock.");
        }
    }

    private static async Task<string?> ReadLatestAsync(
        DbConnection connection,
        IDbContextTransaction transaction,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SELECT TOP (1) [OrderNumber]
            FROM [Orders] WITH (UPDLOCK, HOLDLOCK)
            WHERE [OrderNumber] >= @first AND [OrderNumber] <= @last
            ORDER BY [OrderNumber] DESC;
            """;
        AddParameter(command, "@first", $"{prefix}0001");
        AddParameter(command, "@last", $"{prefix}9999");
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static int ParseSequence(string orderNumber, string prefix)
    {
        if (orderNumber.Length != prefix.Length + 4 ||
            !orderNumber.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(orderNumber.AsSpan(prefix.Length), out var sequence) ||
            sequence is < 1 or > OrderNumber.MaximumDailySequence)
        {
            throw new InvalidOperationException(
                $"The stored order number '{orderNumber}' does not match the approved format.");
        }

        return sequence;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
