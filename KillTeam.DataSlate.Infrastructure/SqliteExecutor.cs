using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using KillTeam.DataSlate.Domain;

namespace KillTeam.DataSlate.Infrastructure;

public sealed class SqliteExecutor : ISqlExecutor
{
    private readonly string? _connectionString;
    private readonly SqliteConnection? _sharedConnection;

    public SqliteExecutor(IOptions<DataSlateOptions> options)
        => _connectionString = $"Data Source={options.Value.DatabasePath}";

    public SqliteExecutor(SqliteConnection connection)
        => _sharedConnection = connection;

    private async Task<(SqliteConnection connection, bool owned)> GetConnectionAsync()
    {
        if (_sharedConnection is not null)
        {
            return (_sharedConnection, false);
        }

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        return (connection, true);
    }

    private static void BindParameters(SqliteCommand command, Dictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var (parameterName, parameterValue) in parameters)
        {
            command.Parameters.AddWithValue(parameterName, parameterValue ?? DBNull.Value);
        }
    }

    public async Task ExecuteAsync(string sql, Dictionary<string, object?> parameters)
    {
        var (connection, owned) = await GetConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            BindParameters(command, parameters);

            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        var (connection, owned) = await GetConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            BindParameters(command, parameters);
            await using var reader = await command.ExecuteReaderAsync();
            var results = new List<T>();

            while (await reader.ReadAsync())
            {
                results.Add(map(reader));
            }

            return results;
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        var (connection, owned) = await GetConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            BindParameters(command, parameters);
            await using var reader = await command.ExecuteReaderAsync();

            return await reader.ReadAsync() ? map(reader) : default;
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task<T?> ScalarAsync<T>(string sql,
        Dictionary<string, object?>? parameters = null)
    {
        var (connection, owned) = await GetConnectionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            BindParameters(command, parameters);
            var result = await command.ExecuteScalarAsync();

            if (result is null || result == DBNull.Value)
            {
                return default;
            }

            return (T)Convert.ChangeType(result, typeof(T));
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }

    public async Task ExecuteTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> work)
    {
        var (connection, owned) = await GetConnectionAsync();
        try
        {
            await using var transaction = connection.BeginTransaction();
            try
            {
                await work(connection, transaction);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            if (owned)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
