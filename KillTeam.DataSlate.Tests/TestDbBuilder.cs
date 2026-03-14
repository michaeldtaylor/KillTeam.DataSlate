using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Console.Infrastructure;

namespace KillTeam.DataSlate.Tests;

public sealed class TestDbBuilder : IDisposable
{
    private readonly SqliteConnection _conn;

    private TestDbBuilder()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();

        DatabaseInitialiser.ApplyAllMigrations(_conn);
    }

    public static TestDbBuilder Create() => new();

    public SqliteConnection Connection => _conn;

    public void Dispose() => _conn.Dispose();

    public TestDbBuilder WithPlayer(Guid id, string name)
    {
        Exec("INSERT INTO players (id, name) VALUES (@id, @name)",
            ("@id", id.ToString()), ("@name", name));
        return this;
    }

    public TestDbBuilder WithKillTeam(Guid id, string name, string faction)
    {
        Exec("INSERT INTO kill_teams (id, name, faction) VALUES (@id, @name, @faction)",
            ("@id", id.ToString()), ("@name", name), ("@faction", faction));
        return this;
    }

    public TestDbBuilder WithOperative(Guid id, Guid teamId, string name,
        int wounds, int save, int apl, int move)
    {
        Exec("""
            INSERT INTO operatives
                (id, kill_team_id, name, operative_type, move, apl, wounds, save)
            VALUES (@id, @teamId, @name, @name, @move, @apl, @wounds, @save)
            """,
            ("@id", id.ToString()), ("@teamId", teamId.ToString()), ("@name", name),
            ("@move", move), ("@apl", apl), ("@wounds", wounds), ("@save", save));
        return this;
    }

    public TestDbBuilder WithWeapon(Guid id, Guid operativeId, string name,
        string type, int atk, int hit, int normalDmg, int critDmg, string specialRules = "")
    {
        Exec("""
            INSERT INTO weapons
                (id, operative_id, name, type, atk, hit, normal_dmg, critical_dmg, special_rules)
            VALUES (@id, @opId, @name, @type, @atk, @hit, @nd, @cd, @sr)
            """,
            ("@id", id.ToString()), ("@opId", operativeId.ToString()), ("@name", name),
            ("@type", type), ("@atk", atk), ("@hit", hit), ("@nd", normalDmg),
            ("@cd", critDmg), ("@sr", specialRules));
        return this;
    }

    public TestDbBuilder WithGame(Guid id, Guid teamAId, Guid teamBId,
        Guid playerAId, Guid playerBId, string status = "InProgress")
    {
        Exec("""
            INSERT INTO games
                (id, played_at, team_a_id, team_b_id, player_a_id, player_b_id, status)
            VALUES (@id, @at, @ta, @tb, @pa, @pb, @st)
            """,
            ("@id", id.ToString()), ("@at", DateTime.UtcNow.ToString("o")),
            ("@ta", teamAId.ToString()), ("@tb", teamBId.ToString()),
            ("@pa", playerAId.ToString()), ("@pb", playerBId.ToString()),
            ("@st", status));
        return this;
    }

    public TestDbBuilder WithTurningPoint(Guid id, Guid gameId, int number,
        bool strategyPhaseComplete = false)
    {
        Exec("""
            INSERT INTO turning_points
                (id, game_id, number, team_with_initiative_id, is_strategy_phase_complete)
            SELECT @id, @gid, @num, team_a_id, @spc FROM games WHERE id = @gid
            """,
            ("@id", id.ToString()), ("@gid", gameId.ToString()), ("@num", number),
            ("@spc", strategyPhaseComplete ? 1 : 0));
        return this;
    }

    public TestDbBuilder WithActivation(Guid id, Guid turningPointId, int seq,
        Guid operativeId, Guid teamId, string order = "Engage")
    {
        Exec("""
            INSERT INTO activations
                (id, turning_point_id, sequence_number, operative_id, team_id, order_selected)
            VALUES (@id, @tpid, @seq, @opid, @tid, @ord)
            """,
            ("@id", id.ToString()), ("@tpid", turningPointId.ToString()), ("@seq", seq),
            ("@opid", operativeId.ToString()), ("@tid", teamId.ToString()), ("@ord", order));
        return this;
    }

    public TestDbBuilder WithGameOperativeState(Guid id, Guid gameId,
        Guid operativeId, int currentWounds, string order = "Engage")
    {
        Exec("""
            INSERT INTO game_operative_states
                (id, game_id, operative_id, current_wounds, "order")
            VALUES (@id, @gid, @opid, @cw, @ord)
            """,
            ("@id", id.ToString()), ("@gid", gameId.ToString()),
            ("@opid", operativeId.ToString()), ("@cw", currentWounds), ("@ord", order));
        return this;
    }

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}
