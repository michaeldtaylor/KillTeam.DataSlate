using Microsoft.Data.Sqlite;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;

namespace KillTeam.DataSlate.Console.Infrastructure.Repositories;

public class SqliteGameOperativeStateRepository : IGameOperativeStateRepository
{
    private readonly ISqlExecutor _db;

    public SqliteGameOperativeStateRepository(ISqlExecutor db) => _db = db;

    public SqliteGameOperativeStateRepository(SqliteConnection connection)
        : this(new SqliteExecutor(connection)) { }

    public async Task CreateAsync(GameOperativeState state)
    {
        await _db.ExecuteAsync(
            """
            INSERT INTO game_operative_states
            (id, game_id, operative_id, current_wounds, "order", is_ready,
             is_on_guard, is_incapacitated, has_used_counteract_this_turning_point, apl_modifier)
            VALUES
            (@id, @gameId, @operativeId, @currentWounds, @order, @isReady,
             @isOnGuard, @isIncapacitated, @hasUsedCounteract, @aplModifier)
            """,
            new()
            {
                ["@id"] = state.Id.ToString(),
                ["@gameId"] = state.GameId.ToString(),
                ["@operativeId"] = state.OperativeId.ToString(),
                ["@currentWounds"] = state.CurrentWounds,
                ["@order"] = state.Order.ToString(),
                ["@isReady"] = state.IsReady ? 1 : 0,
                ["@isOnGuard"] = state.IsOnGuard ? 1 : 0,
                ["@isIncapacitated"] = state.IsIncapacitated ? 1 : 0,
                ["@hasUsedCounteract"] = state.HasUsedCounteractThisTurningPoint ? 1 : 0,
                ["@aplModifier"] = state.AplModifier
            });
    }

    public async Task<IEnumerable<GameOperativeState>> GetByGameAsync(Guid gameId)
    {
        return await _db.QueryAsync(
            """
            SELECT id, game_id, operative_id, current_wounds, "order", is_ready,
                   is_on_guard, is_incapacitated, has_used_counteract_this_turning_point, apl_modifier
            FROM game_operative_states
            WHERE game_id = @gameId
            """,
            r => new GameOperativeState
            {
                Id = Guid.Parse(r.GetString(0)),
                GameId = Guid.Parse(r.GetString(1)),
                OperativeId = Guid.Parse(r.GetString(2)),
                CurrentWounds = r.GetInt32(3),
                Order = Enum.Parse<Order>(r.GetString(4)),
                IsReady = r.GetInt32(5) != 0,
                IsOnGuard = r.GetInt32(6) != 0,
                IsIncapacitated = r.GetInt32(7) != 0,
                HasUsedCounteractThisTurningPoint = r.GetInt32(8) != 0,
                AplModifier = r.GetInt32(9)
            },
            new() { ["@gameId"] = gameId.ToString() });
    }

    public async Task UpdateWoundsAsync(Guid id, int currentWounds)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET current_wounds = @wounds WHERE id = @id",
            new() { ["@wounds"] = currentWounds, ["@id"] = id.ToString() });
    }

    public async Task UpdateOrderAsync(Guid id, Order order)
    {
        await _db.ExecuteAsync(
            @"UPDATE game_operative_states SET ""order"" = @order WHERE id = @id",
            new() { ["@order"] = order.ToString(), ["@id"] = id.ToString() });
    }

    public async Task UpdateGuardAsync(Guid id, bool isOnGuard)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET is_on_guard = @isOnGuard WHERE id = @id",
            new() { ["@isOnGuard"] = isOnGuard ? 1 : 0, ["@id"] = id.ToString() });
    }

    public async Task SetAplModifierAsync(Guid id, int aplModifier)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET apl_modifier = @val WHERE id = @id",
            new() { ["@val"] = aplModifier, ["@id"] = id.ToString() });
    }

    public async Task SetReadyAsync(Guid id, bool isReady)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET is_ready = @ready WHERE id = @id",
            new() { ["@ready"] = isReady ? 1 : 0, ["@id"] = id.ToString() });
    }

    public async Task SetIncapacitatedAsync(Guid id, bool isIncapacitated)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET is_incapacitated = @val WHERE id = @id",
            new() { ["@val"] = isIncapacitated ? 1 : 0, ["@id"] = id.ToString() });
    }

    public async Task SetCounteractUsedAsync(Guid id, bool used)
    {
        await _db.ExecuteAsync(
            "UPDATE game_operative_states SET has_used_counteract_this_turning_point = @val WHERE id = @id",
            new() { ["@val"] = used ? 1 : 0, ["@id"] = id.ToString() });
    }
}
