using KillTeam.DataSlate.Domain.Events;
using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public record RollableDie(int Index, int Value, bool HasBeenRerolled = false);

public class RerollOrchestrator(IAnsiConsole console, IGameRepository gameRepository)
{
    /// <summary>
    /// Applies all weapon-based re-rolls (Balanced, Ceaseless, Relentless) in order,
    /// then offers CP re-roll to the attacker.
    /// Returns updated dice array.
    /// </summary>
    public async Task<int[]> ApplyAttackerRerollsAsync(
        int[] dice,
        List<WeaponSpecialRule> rules,
        Guid gameId,
        bool isTeamA,
        string ownerLabel,
        string participant = "",
        GameEventStream? eventStream = null)
    {
        var pool = dice.Select((v, i) => new RollableDie(i, v)).ToList();

        // ─── 1. Balanced: pick exactly 1 die to re-roll ─────────────────────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Balanced))
        {
            pool = await ApplyBalancedAsync(pool, ownerLabel, participant, eventStream);
        }

        // ─── 2. Ceaseless: choose a face value; all matching dice re-roll ────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Ceaseless))
        {
            pool = ApplyCeaseless(pool, ownerLabel, participant, eventStream);
        }

        // ─── 3. Relentless: choose any/all dice to re-roll ───────────────────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Relentless))
        {
            pool = await ApplyRelentlessAsync(pool, ownerLabel, participant, eventStream);
        }

        // ─── 4. CP re-roll (attacker) ────────────────────────────────────────
        pool = await ApplyCpRerollAsync(pool, gameId, isTeamA, ownerLabel, participant, eventStream);

        return pool.Select(d => d.Value).ToArray();
    }

    /// <summary>
    /// Offers CP re-roll to the defender (once per target for Blast/Torrent).
    /// </summary>
    public async Task<int[]> ApplyDefenderRerollAsync(
        int[] dice,
        Guid gameId,
        bool isTeamA,
        string ownerLabel,
        string participant = "",
        GameEventStream? eventStream = null)
    {
        var pool = dice.Select((v, i) => new RollableDie(i, v)).ToList();
        pool = await ApplyCpRerollAsync(pool, gameId, isTeamA, ownerLabel, participant, eventStream);
        return pool.Select(d => d.Value).ToArray();
    }

    // ─── Weapon re-roll implementations ──────────────────────────────────────

    private async Task<List<RollableDie>> ApplyBalancedAsync(
        List<RollableDie> pool, string label, string participant, GameEventStream? eventStream)
    {
        if (pool.Count == 0)
        {
            return pool;
        }

        var choice = await Task.FromResult(console.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title($"[yellow]{label}[/] [dim](Balanced)[/] Pick 1 die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(pool)));

        var newVal = RollD6();
        eventStream?.Emit((seq, ts) => new BalancedRerollAppliedEvent(
            eventStream.GameSessionId,
            seq, ts, participant, label, choice.Index, choice.Value, newVal));

        return pool.Select(d => d.Index == choice.Index
            ? d with { Value = newVal, HasBeenRerolled = true }
            : d).ToList();
    }

    private List<RollableDie> ApplyCeaseless(List<RollableDie> pool, string label, string participant, GameEventStream? eventStream)
    {
        if (pool.Count == 0)
        {
            return pool;
        }

        var face = console.Prompt(
            new TextPrompt<int>($"[yellow]{label}[/] [dim](Ceaseless)[/] Re-roll all dice showing which value? (1-6):")
                .Validate(v => v is >= 1 and <= 6));

        return pool.Select(d =>
        {
            if (d.Value != face || d.HasBeenRerolled)
            {
                return d;
            }
            var newVal = RollD6();
            eventStream?.Emit((seq, ts) => new CeaselessRerollAppliedEvent(
                eventStream.GameSessionId,
                seq, ts, participant, label, d.Index, d.Value, newVal));
            return d with { Value = newVal, HasBeenRerolled = true };
        }).ToList();
    }

    private async Task<List<RollableDie>> ApplyRelentlessAsync(
        List<RollableDie> pool, string label, string participant, GameEventStream? eventStream)
    {
        if (pool.Count == 0)
        {
            return pool;
        }

        var eligible = pool.Where(d => !d.HasBeenRerolled).ToList();
        if (eligible.Count == 0)
        {
            return pool;
        }

        var chosen = await Task.FromResult(console.Prompt(
            new MultiSelectionPrompt<RollableDie>()
                .Title($"[yellow]{label}[/] [dim](Relentless)[/] Select dice to re-roll (space to toggle):")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(eligible)
                .NotRequired()));

        var updated = pool.ToList();
        foreach (var d in chosen)
        {
            var newVal = RollD6();
            eventStream?.Emit((seq, ts) => new RelentlessRerollAppliedEvent(
                eventStream.GameSessionId,
                seq, ts, participant, label, d.Index, d.Value, newVal));
            var idx = updated.FindIndex(x => x.Index == d.Index);
            if (idx >= 0)
            {
                updated[idx] = d with { Value = newVal, HasBeenRerolled = true };
            }
        }
        return updated;
    }

    private async Task<List<RollableDie>> ApplyCpRerollAsync(
        List<RollableDie> pool, Guid gameId, bool isTeamA, string label, string participant, GameEventStream? eventStream)
    {
        if (pool.Count == 0)
        {
            return pool;
        }

        var game = await gameRepository.GetByIdAsync(gameId);
        if (game is null)
        {
            return pool;
        }

        var cp = isTeamA ? game.Participant1.CommandPoints : game.Participant2.CommandPoints;
        if (cp <= 0)
        {
            return pool;
        }

        var eligible = pool.Where(d => !d.HasBeenRerolled).ToList();
        if (eligible.Count == 0)
        {
            return pool;
        }

        if (!console.Confirm($"[yellow]{label}[/] Spend 1CP (have {cp}CP) to re-roll one die?", defaultValue: false))
        {
            return pool;
        }

        var choice = await Task.FromResult(console.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title("Select die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(eligible)));

        var newVal = RollD6();
        var newCpA = isTeamA ? game.Participant1.CommandPoints - 1 : game.Participant1.CommandPoints;
        var newCpB = isTeamA ? game.Participant2.CommandPoints : game.Participant2.CommandPoints - 1;
        var remainingCp = isTeamA ? newCpA : newCpB;

        eventStream?.Emit((seq, ts) => new CpRerollAppliedEvent(
            eventStream.GameSessionId,
            seq, ts, participant, label, choice.Index, choice.Value, newVal, remainingCp));

        await gameRepository.UpdateCpAsync(gameId, newCpA, newCpB);

        return pool.Select(d => d.Index == choice.Index
            ? d with { Value = newVal, HasBeenRerolled = true }
            : d).ToList();
    }

    private static int RollD6() => Random.Shared.Next(1, 7);
}

