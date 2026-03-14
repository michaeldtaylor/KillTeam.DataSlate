using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public record RollableDie(int Index, int Value, bool HasBeenRerolled = false);

public class RerollOrchestrator(IGameRepository gameRepository)
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
        string ownerLabel)
    {
        var pool = dice.Select((v, i) => new RollableDie(i, v)).ToList();

        // ─── 1. Balanced: pick exactly 1 die to re-roll ─────────────────────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Balanced))
        {
            pool = await ApplyBalancedAsync(pool, ownerLabel);
        }

        // ─── 2. Ceaseless: choose a face value; all matching dice re-roll ────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Ceaseless))
        {
            pool = ApplyCeaseless(pool, ownerLabel);
        }

        // ─── 3. Relentless: choose any/all dice to re-roll ───────────────────
        if (rules.Any(r => r.Kind == SpecialRuleKind.Relentless))
        {
            pool = await ApplyRelentlessAsync(pool, ownerLabel);
        }

        // ─── 4. CP re-roll (attacker) ────────────────────────────────────────
        pool = await ApplyCpRerollAsync(pool, gameId, isTeamA, ownerLabel);

        return pool.Select(d => d.Value).ToArray();
    }

    /// <summary>
    /// Offers CP re-roll to the defender (once per target for Blast/Torrent).
    /// </summary>
    public async Task<int[]> ApplyDefenderRerollAsync(
        int[] dice,
        Guid gameId,
        bool isTeamA,
        string ownerLabel)
    {
        var pool = dice.Select((v, i) => new RollableDie(i, v)).ToList();
        pool = await ApplyCpRerollAsync(pool, gameId, isTeamA, ownerLabel);
        return pool.Select(d => d.Value).ToArray();
    }

    // ─── Weapon re-roll implementations ──────────────────────────────────────

    private static async Task<List<RollableDie>> ApplyBalancedAsync(
        List<RollableDie> pool, string label)
    {
        if (pool.Count == 0) return pool;

        var choice = await Task.FromResult(AnsiConsole.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title($"[yellow]{label}[/] [dim](Balanced)[/] Pick 1 die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(pool)));

        var newVal = RollD6();
        AnsiConsole.MarkupLine($"  Re-rolled die {choice.Index + 1}: {choice.Value} → [bold]{newVal}[/]");

        return pool.Select(d => d.Index == choice.Index
            ? d with { Value = newVal, HasBeenRerolled = true }
            : d).ToList();
    }

    private static List<RollableDie> ApplyCeaseless(List<RollableDie> pool, string label)
    {
        if (pool.Count == 0) return pool;

        var face = AnsiConsole.Prompt(
            new TextPrompt<int>($"[yellow]{label}[/] [dim](Ceaseless)[/] Re-roll all dice showing which value? (1-6):")
                .Validate(v => v is >= 1 and <= 6));

        return pool.Select(d =>
        {
            if (d.Value != face || d.HasBeenRerolled) return d;
            var newVal = RollD6();
            AnsiConsole.MarkupLine($"  Ceaseless re-roll die {d.Index + 1}: {d.Value} → [bold]{newVal}[/]");
            return d with { Value = newVal, HasBeenRerolled = true };
        }).ToList();
    }

    private static async Task<List<RollableDie>> ApplyRelentlessAsync(
        List<RollableDie> pool, string label)
    {
        if (pool.Count == 0) return pool;

        var eligible = pool.Where(d => !d.HasBeenRerolled).ToList();
        if (eligible.Count == 0) return pool;

        var chosen = await Task.FromResult(AnsiConsole.Prompt(
            new MultiSelectionPrompt<RollableDie>()
                .Title($"[yellow]{label}[/] [dim](Relentless)[/] Select dice to re-roll (space to toggle):")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(eligible)
                .NotRequired()));

        var updated = pool.ToList();
        foreach (var d in chosen)
        {
            var newVal = RollD6();
            AnsiConsole.MarkupLine($"  Relentless re-roll die {d.Index + 1}: {d.Value} → [bold]{newVal}[/]");
            var idx = updated.FindIndex(x => x.Index == d.Index);
            if (idx >= 0) updated[idx] = d with { Value = newVal, HasBeenRerolled = true };
        }
        return updated;
    }

    private async Task<List<RollableDie>> ApplyCpRerollAsync(
        List<RollableDie> pool, Guid gameId, bool isTeamA, string label)
    {
        if (pool.Count == 0) return pool;

        var game = await gameRepository.GetByIdAsync(gameId);
        if (game is null) return pool;

        var cp = isTeamA ? game.CpTeamA : game.CpTeamB;
        if (cp <= 0) return pool;

        var eligible = pool.Where(d => !d.HasBeenRerolled).ToList();
        if (eligible.Count == 0) return pool;

        if (!AnsiConsole.Confirm($"[yellow]{label}[/] Spend 1CP (have {cp}CP) to re-roll one die?", defaultValue: false))
            return pool;

        var choice = await Task.FromResult(AnsiConsole.Prompt(
            new SelectionPrompt<RollableDie>()
                .Title("Select die to re-roll:")
                .UseConverter(d => $"Die {d.Index + 1}: [bold]{d.Value}[/]")
                .AddChoices(eligible)));

        var newVal = RollD6();
        AnsiConsole.MarkupLine($"  CP re-roll die {choice.Index + 1}: {choice.Value} → [bold]{newVal}[/]");

        var newCpA = isTeamA ? game.CpTeamA - 1 : game.CpTeamA;
        var newCpB = isTeamA ? game.CpTeamB : game.CpTeamB - 1;
        await gameRepository.UpdateCpAsync(gameId, newCpA, newCpB);

        return pool.Select(d => d.Index == choice.Index
            ? d with { Value = newVal, HasBeenRerolled = true }
            : d).ToList();
    }

    private static int RollD6() => Random.Shared.Next(1, 7);
}
