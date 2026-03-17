using KillTeam.DataSlate.Infrastructure;
using Microsoft.Data.Sqlite;

namespace KillTeam.DataSlate.Tests;

public sealed class TestDbBuilder : IDisposable
{
    private readonly SqliteConnection _connection;

    private TestDbBuilder()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var pragmaCommand = _connection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA foreign_keys = ON";
        pragmaCommand.ExecuteNonQuery();

        DatabaseInitialiser.ApplyAllMigrations(_connection);
    }

    public static TestDbBuilder Create() => new();

    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();

    public TestDbBuilder WithPlayer(Guid id, string name)
    {
        Exec("INSERT INTO players (id, name) VALUES (@id, @name)",
            ("@id", id.ToString()), ("@name", name));

        return this;
    }

    public TestDbBuilder WithTeam(string id, string name, string faction)
    {
        Exec("INSERT INTO teams (id, name, faction) VALUES (@id, @name, @faction)",
            ("@id", id), ("@name", name), ("@faction", faction));

        return this;
    }

    public TestDbBuilder WithOperative(Guid id, string teamId, string name,
        int wounds, int save, int apl, int move)
    {
        Exec("""
            INSERT INTO operatives
                (id, team_id, name, operative_type, move, apl, wounds, save)
            VALUES (@id, @teamId, @name, @name, @move, @apl, @wounds, @save)
            """,
            ("@id", id.ToString()), ("@teamId", teamId), ("@name", name),
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

    public TestDbBuilder WithGame(Guid id, string teamAId, string teamAName,
        string teamBId, string teamBName,
        Guid playerAId, Guid playerBId, string status = "InProgress")
    {
        Exec("""
            INSERT INTO games
                (id, played_at, participant1_team_id, participant1_team_name, participant2_team_id, participant2_team_name,
                 participant1_player_id, participant2_player_id, status)
            VALUES (@id, @at, @taId, @taName, @tbId, @tbName, @pa, @pb, @st)
            """,
            ("@id", id.ToString()), ("@at", DateTime.UtcNow.ToString("o")),
            ("@taId", teamAId), ("@taName", teamAName),
            ("@tbId", teamBId), ("@tbName", teamBName),
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
            SELECT @id, @gid, @num, participant1_team_id, @spc FROM games WHERE id = @gid
            """,
            ("@id", id.ToString()), ("@gid", gameId.ToString()), ("@num", number),
            ("@spc", strategyPhaseComplete ? 1 : 0));

        return this;
    }

    public TestDbBuilder WithActivation(Guid id, Guid turningPointId, int seq,
        Guid operativeId, string teamId, string order = "Engage")
    {
        Exec("""
            INSERT INTO activations
                (id, turning_point_id, sequence_number, operative_id, team_id, order_selected)
            VALUES (@id, @tpid, @seq, @opid, @tid, @ord)
            """,
            ("@id", id.ToString()), ("@tpid", turningPointId.ToString()), ("@seq", seq),
            ("@opid", operativeId.ToString()), ("@tid", teamId), ("@ord", order));

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
        using var command = _connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }
}
