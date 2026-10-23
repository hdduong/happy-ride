using GrabBike.Api.SharedKernel;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace GrabBike.Api.Infrastructure.Persistence;

/// <summary>
/// Serializes ride writes across every service that shares this database.
/// A process-local semaphore cannot stop a cancel in one process from racing an accept in another.
/// </summary>
public sealed class RideWriteLock : IRideWriteLock
{
    private const long Key = 7_264_845_549_321_001;

    private readonly string _connectionString;

    public RideWriteLock(IConfiguration configuration)
        : this(configuration.GetConnectionString("GrabBike")
            ?? throw new InvalidOperationException("ConnectionStrings:GrabBike is required."))
    {
    }

    public RideWriteLock(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:GrabBike is required.");
        _connectionString = connectionString;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", Key);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new Releaser(connection, Key);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Releaser(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
