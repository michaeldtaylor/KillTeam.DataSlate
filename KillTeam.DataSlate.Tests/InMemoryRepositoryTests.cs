using FluentAssertions;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Models;
using Xunit;

namespace KillTeam.DataSlate.Tests;

public class InMemoryRepositoryTests
{
    private static GameOperativeState MakeState(Guid gameId, Guid operativeId, int wounds = 10) =>
        new() { GameId = gameId, OperativeId = operativeId, CurrentWounds = wounds };

    // ── InMemoryGameOperativeStateRepository ──────────────────────────────────

    [Fact]
    public async Task GetByGameAsync_ReturnsSeededStates()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var gameId = Guid.NewGuid();
        var s1 = MakeState(gameId, Guid.NewGuid());
        var s2 = MakeState(gameId, Guid.NewGuid());
        var other = MakeState(Guid.NewGuid(), Guid.NewGuid()); // different game

        repo.Seed([s1, s2, other]);

        var results = (await repo.GetByGameAsync(gameId)).ToList();

        results.Should().HaveCount(2);
        results.Should().Contain(x => x.OperativeId == s1.OperativeId);
        results.Should().Contain(x => x.OperativeId == s2.OperativeId);
    }

    [Fact]
    public async Task UpdateWoundsAsync_UpdatesStateInMemory()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var state = MakeState(Guid.NewGuid(), Guid.NewGuid(), wounds: 10);
        repo.Seed([state]);

        await repo.UpdateWoundsAsync(state.Id, 4);

        state.CurrentWounds.Should().Be(4);
    }

    [Fact]
    public async Task SetIncapacitatedAsync_MarksStateIncapacitated()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var state = MakeState(Guid.NewGuid(), Guid.NewGuid());
        repo.Seed([state]);

        await repo.SetIncapacitatedAsync(state.Id, true);

        state.IsIncapacitated.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateOrderAsync_SetsOrder()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var state = MakeState(Guid.NewGuid(), Guid.NewGuid());
        repo.Seed([state]);

        await repo.UpdateOrderAsync(state.Id, Order.Conceal);

        state.Order.Should().Be(Order.Conceal);
    }

    [Fact]
    public async Task UpdateGuardAsync_SetsGuard()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var state = MakeState(Guid.NewGuid(), Guid.NewGuid());
        repo.Seed([state]);

        await repo.UpdateGuardAsync(state.Id, true);

        state.IsOnGuard.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_AddsNewState()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var gameId = Guid.NewGuid();
        var state = MakeState(gameId, Guid.NewGuid());

        await repo.CreateAsync(state);

        var results = (await repo.GetByGameAsync(gameId)).ToList();
        results.Should().ContainSingle(s => s.Id == state.Id);
    }

    [Fact]
    public void GetAll_ReturnsAllSeededStates()
    {
        var repo = new InMemoryGameOperativeStateRepository();
        var s1 = MakeState(Guid.NewGuid(), Guid.NewGuid());
        var s2 = MakeState(Guid.NewGuid(), Guid.NewGuid());
        repo.Seed([s1, s2]);

        repo.GetAll().Should().HaveCount(2);
    }

    // ── InMemoryActionRepository ──────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ReturnsActionUnchanged()
    {
        var repo = new InMemoryActionRepository();
        var action = new GameAction { Id = Guid.NewGuid(), Type = ActionType.Fight, ApCost = 1 };

        var result = await repo.CreateAsync(action);

        result.Should().BeSameAs(action);
    }

    [Fact]
    public async Task GetByActivationAsync_ReturnsEmpty()
    {
        var repo = new InMemoryActionRepository();
        var results = await repo.GetByActivationAsync(Guid.NewGuid());
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateNarrativeAsync_DoesNotThrow()
    {
        var repo = new InMemoryActionRepository();
        await repo.Invoking(r => r.UpdateNarrativeAsync(Guid.NewGuid(), "test"))
            .Should().NotThrowAsync();
    }

    // ── InMemoryBlastTargetRepository ─────────────────────────────────────────

    [Fact]
    public async Task BlastTargetRepo_CreateAsync_DoesNotThrow()
    {
        var repo = new InMemoryBlastTargetRepository();
        await repo.Invoking(r => r.CreateAsync(new BlastTarget()))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task BlastTargetRepo_GetByActionIdAsync_ReturnsEmpty()
    {
        var repo = new InMemoryBlastTargetRepository();
        var results = await repo.GetByActionIdAsync(Guid.NewGuid());
        results.Should().BeEmpty();
    }
}
