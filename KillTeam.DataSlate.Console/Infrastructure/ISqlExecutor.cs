using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Console.Infrastructure;

public interface ISqlExecutor
{
    Task ExecuteAsync(string sql, Dictionary<string, object?> parameters);

    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null);

    Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> map,
        Dictionary<string, object?>? parameters = null);

    Task<T?> ScalarAsync<T>(string sql, Dictionary<string, object?>? parameters = null);

    Task ExecuteTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> work);
}
