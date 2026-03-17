using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace KillTeam.DataSlate.Infrastructure;

public sealed class SqliteExecutor : ISqlExecutor
{
    private readonly string? _connectionString;
    private readonly SqliteConnection? _sharedConnection;

    public SqliteExecutor(IConfiguration config)
        => _connectionString = $"Data Source={config["DataSlate:DatabasePath"]}";

    public SqliteExecutor(SqliteConnection connection)
        => _sharedConnection = connection;

    private async Task<(SqliteConnection conn, bool owned)> GetConnectionAsync()
    {
        if (_sharedConnection is not null)
        {
            return (_sharedConnection, false);
        }
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return (conn, true);
    }

    private static void BindParameters(SqliteCommand cmd, Dictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var (k, v) in parameters)
        {
            cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        }
    }

    public async Task ExecuteAsync(string sql, Dictionary<string, object?> parameters)
    {
        var (conn, owned) = await GetConnectionAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, parameters);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (owned)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        var (conn, owned) = await GetConnectionAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, parameters);
            using var reader = await cmd.ExecuteReaderAsync();
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
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null)
    {
        var (conn, owned) = await GetConnectionAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, parameters);
            using var reader = await cmd.ExecuteReaderAsync();
            return await reader.ReadAsync() ? map(reader) : default;
        }
        finally
        {
            if (owned)
            {
                await conn.DisposeAsync();
            }
        }
    }

    public async Task<T?> ScalarAsync<T>(string sql,
        Dictionary<string, object?>? parameters = null)
    {
        var (conn, owned) = await GetConnectionAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            BindParameters(cmd, parameters);
            var result = await cmd.ExecuteScalarAsync();
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
                await conn.DisposeAsync();
            }
        }
    }

    public async Task ExecuteTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> work)
    {
        var (conn, owned) = await GetConnectionAsync();
        try
        {
            using var tx = conn.BeginTransaction();
            try
            {
                await work(conn, tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally
        {
            if (owned)
            {
                await conn.DisposeAsync();
            }
        }
    }
}
