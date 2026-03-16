using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public record FightSessionResult(
    bool AttackerCausedIncapacitation,
    bool DefenderCausedIncapacitation,
    int AttackerDamageDealt,
    int DefenderDamageDealt,
    Guid TargetOperativeId);

public class FightSessionOrchestrator(
    IAnsiConsole console,
    FightResolutionService fightResolutionService,
    RerollOrchestrator rerollOrchestrator,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository)
{
    public async Task<FightSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation)
    {
        var isAttackerTeamA = attacker.TeamId == game.Participant1.TeamId;

        // 1. Target selection
        var enemyStates = allOperativeStates
            .Where(s => !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId != attacker.TeamId)
            .ToList();

        if (enemyStates.Count == 0)
        {
            console.MarkupLine("[yellow]No valid fight targets available.[/]");
            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
        }

        GameOperativeState targetState;
        if (enemyStates.Count == 1)
        {
            targetState = enemyStates[0];
            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                console.MarkupLine($"[dim]Target:[/] {Markup.Escape(autoTarget.Name)} (Wounds: [green]{targetState.CurrentWounds}/{autoTarget.Wounds}[/])");
            }
        }
        else
        {
            targetState = console.Prompt(
                new SelectionPrompt<GameOperativeState>()
                    .Title("Select an enemy to fight (must be within control range):")
                    .UseConverter(s => allOperatives.TryGetValue(s.OperativeId, out var o)
                        ? $"{Markup.Escape(o.Name)} (Wounds: {s.CurrentWounds}/{o.Wounds})"
                        : s.OperativeId.ToString())
                    .AddChoices(enemyStates));
        }

        if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
        {
            console.MarkupLine("[red]Target operative not found.[/]");
            return new FightSessionResult(false, false, 0, 0, Guid.Empty);
        }
        var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;

        // 2. Attacker weapon selection (melee only)
        var atkMeleeWeapons = attacker.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();
        if (atkMeleeWeapons.Count == 0)
        {
            console.MarkupLine($"[red]{Markup.Escape(attacker.Name)} has no melee weapons![/]");
            return new FightSessionResult(false, false, 0, 0, targetState.OperativeId);
        }

        var atkIsInjured = attackerState.CurrentWounds < attacker.Wounds / 2;
        Weapon atkWeapon;
        if (atkMeleeWeapons.Count == 1)
        {
            atkWeapon = atkMeleeWeapons[0];
            var injured = atkIsInjured ? $" [yellow](Injured: effective Hit {atkWeapon.Hit + 1}+)[/]" : "";
            console.MarkupLine($"[dim]Auto-selected melee weapon:[/] {Markup.Escape(atkWeapon.Name)} (Attack: [green]{atkWeapon.Atk}[/] | Hit: [green]{atkWeapon.Hit}+[/] | Normal: [green]{atkWeapon.NormalDmg}[/] | Crit: [green]{atkWeapon.CriticalDmg}[/]){injured}");
        }
        else
        {
            atkWeapon = console.Prompt(
                new SelectionPrompt<Weapon>()
                    .Title("Select attacker's melee weapon:")
                    .UseConverter(w =>
                    {
                        var injured = atkIsInjured ? $" [yellow](Injured: effective Hit {w.Hit + 1}+)[/]" : "";
                        return $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]){injured}";
                    })
                    .AddChoices(atkMeleeWeapons));
        }

        var atkEffectiveHit = atkIsInjured ? atkWeapon.Hit + 1 : atkWeapon.Hit;

        // 3. Defender weapon selection (melee only; 0 ATK if none)
        var defMeleeWeapons = targetOp.Weapons.Where(w => w.Type == WeaponType.Melee).ToList();
        Weapon? defWeapon = null;
        var defEffectiveHit = 3;

        if (defMeleeWeapons.Count == 1)
        {
            defWeapon = defMeleeWeapons[0];
            console.MarkupLine($"[dim]Auto-selected defender weapon:[/] {Markup.Escape(defWeapon.Name)} (Attack: [green]{defWeapon.Atk}[/] | Hit: [green]{defWeapon.Hit}+[/] | Normal: [green]{defWeapon.NormalDmg}[/] | Crit: [green]{defWeapon.CriticalDmg}[/])");
            var defIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;
            defEffectiveHit = defIsInjured ? defWeapon.Hit + 1 : defWeapon.Hit;
        }
        else if (defMeleeWeapons.Count > 1)
        {
            defWeapon = console.Prompt(
                new SelectionPrompt<Weapon>()
                    .Title("Select defender's melee weapon:")
                    .UseConverter(w => $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/])")
                    .AddChoices(defMeleeWeapons));
            var defIsInjured = targetState.CurrentWounds < targetOp.Wounds / 2;
            defEffectiveHit = defIsInjured ? defWeapon.Hit + 1 : defWeapon.Hit;
        }
        else
        {
            console.MarkupLine($"[dim]{Markup.Escape(targetOp.Name)} has no melee weapons — rolls 0 attack dice.[/]");
        }

        // 4. Fight assist
        var fightAssist = console.Prompt(
            new TextPrompt<int>("How many non-engaged friendly allies within 6\" of target? (0-2):")
                .DefaultValue(0)
                .Validate(v => v is >= 0 and <= 2));
        atkEffectiveHit = Math.Max(2, atkEffectiveHit - fightAssist);

        // 5. Attacker dice entry
        int[] atkRolls = await RollOrEnterDiceAsync(atkWeapon.Atk, $"{Markup.Escape(attacker.Name)} attack dice (Attack: {atkWeapon.Atk})");
        atkRolls = await rerollOrchestrator.ApplyAttackerRerollsAsync(
            atkRolls, atkWeapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name);

        // 6. Defender dice entry
        var defAtkCount = defWeapon?.Atk ?? 0;
        int[] defRolls = [];
        if (defAtkCount > 0)
        {
            defRolls = await RollOrEnterDiceAsync(defAtkCount, $"{Markup.Escape(targetOp.Name)} fight-back dice (Attack: {defAtkCount})");
            defRolls = await rerollOrchestrator.ApplyDefenderRerollAsync(defRolls, game.Id, isDefenderTeamA, targetOp.Name);
        }

        // 7. Classify dice
        var atkPool = fightResolutionService.CalculateDice(atkRolls, atkEffectiveHit, DieOwner.Attacker);
        var defPool = defWeapon is not null
            ? fightResolutionService.CalculateDice(defRolls, defEffectiveHit, DieOwner.Defender)
            : new FightDicePool(DieOwner.Defender, []);

        // 8. Apply Shock: if attacker has crits, discard defender's lowest success
        if (atkWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Shock) && atkPool.Remaining.Any(d => d.Result == DieResult.Crit))
        {
            var lowestDefSuccess = defPool.Remaining.OrderBy(d => d.RolledValue).FirstOrDefault(d => d.Result != DieResult.Miss);
            if (lowestDefSuccess is not null)
            {
                defPool = defPool with { Remaining = defPool.Remaining.Where(d => d.Id != lowestDefSuccess.Id).ToList() };
                console.MarkupLine($"[yellow]⚡ Shock: {Markup.Escape(targetOp.Name)} discards die (rolled {lowestDefSuccess.RolledValue})[/]");
            }
        }

        var brutalWeapon = atkWeapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Brutal);

        // Track wounds during fight
        var atkCurrentWounds = attackerState.CurrentWounds;
        var defCurrentWounds = targetState.CurrentWounds;
        var totalAtkDmgDealt = 0;
        var totalDefDmgDealt = 0;

        var currentOwner = DieOwner.Attacker;

        // 9. Alternating fight loop
        while (atkPool.Remaining.Count > 0 || defPool.Remaining.Count > 0)
        {
            // Determine active/opponent pools
            FightDicePool activePool, opponentPool;
            DieOwner activeOwner;

            if (currentOwner == DieOwner.Attacker)
            {
                if (atkPool.Remaining.Count > 0)
                {
                    activePool = atkPool;
                    opponentPool = defPool;
                    activeOwner = DieOwner.Attacker;
                }
                else
                {
                    activePool = defPool;
                    opponentPool = atkPool;
                    activeOwner = DieOwner.Defender;
                }
            }
            else
            {
                if (defPool.Remaining.Count > 0)
                {
                    activePool = defPool;
                    opponentPool = atkPool;
                    activeOwner = DieOwner.Defender;
                }
                else
                {
                    activePool = atkPool;
                    opponentPool = defPool;
                    activeOwner = DieOwner.Attacker;
                }
            }

            Operative activeOp = activeOwner == DieOwner.Attacker ? attacker : targetOp;
            Operative opponentOp = activeOwner == DieOwner.Attacker ? targetOp : attacker;
            Weapon activeWeapon = activeOwner == DieOwner.Attacker ? atkWeapon : (defWeapon ?? atkWeapon);
            var activeCurrentWounds = activeOwner == DieOwner.Attacker ? atkCurrentWounds : defCurrentWounds;
            var opponentCurrentWounds = activeOwner == DieOwner.Attacker ? defCurrentWounds : atkCurrentWounds;

            DisplayFightPools(
                attacker.Name, atkCurrentWounds, attacker.Wounds, atkPool,
                targetOp.Name, defCurrentWounds, targetOp.Wounds, defPool);

            var useBrutal = brutalWeapon && activeOwner == DieOwner.Attacker;
            var actions = fightResolutionService.GetAvailableActions(activePool, opponentPool, useBrutal);

            if (actions.Count == 0)
            {
                break;
            }

            // Show action menu
            var actionChoice = console.Prompt(
                new SelectionPrompt<FightAction>()
                    .Title($"[bold]{Markup.Escape(activeOp.Name)}[/] — select an action:")
                    .UseConverter(a => FormatFightAction(a))
                    .AddChoices(actions));

            if (actionChoice.Type == FightActionType.Strike)
            {
                var dmg = fightResolutionService.ApplyStrike(actionChoice.ActiveDie, activeWeapon.NormalDmg, activeWeapon.CriticalDmg);
                console.MarkupLine($"  ⚔ Strike with die ({actionChoice.ActiveDie.RolledValue}) → {dmg} damage to {Markup.Escape(opponentOp.Name)}");

                if (activeOwner == DieOwner.Attacker)
                {
                    defCurrentWounds = Math.Max(0, defCurrentWounds - dmg);
                    totalAtkDmgDealt += dmg;
                }
                else
                {
                    atkCurrentWounds = Math.Max(0, atkCurrentWounds - dmg);
                    totalDefDmgDealt += dmg;
                }

                // Remove used die
                activePool = activePool with
                {
                    Remaining = activePool.Remaining.Where(d => d.Id != actionChoice.ActiveDie.Id).ToList()
                };
            }
            else // Block
            {
                console.MarkupLine($"  🛡 Block: die ({actionChoice.ActiveDie.RolledValue}) cancels ({actionChoice.TargetDie!.RolledValue})");
                (activePool, opponentPool) = fightResolutionService.ApplySingleBlock(
                    actionChoice.ActiveDie, actionChoice.TargetDie!, activePool, opponentPool);
            }

            // Reassign pools
            if (activeOwner == DieOwner.Attacker)
            {
                atkPool = activePool;
                defPool = opponentPool;
            }
            else
            {
                defPool = activePool;
                atkPool = opponentPool;
            }

            // Switch turns
            var nextOwner = activeOwner == DieOwner.Attacker ? DieOwner.Defender : DieOwner.Attacker;
            var nextHasDice = nextOwner == DieOwner.Attacker ? atkPool.Remaining.Count > 0 : defPool.Remaining.Count > 0;
            if (nextHasDice)
            {
                currentOwner = nextOwner;
            }
        }

        // 10. Apply final wound counts
        var atkCausedIncap = defCurrentWounds <= 0 && !targetState.IsIncapacitated;
        var defCausedIncap = atkCurrentWounds <= 0 && !attackerState.IsIncapacitated;

        attackerState.CurrentWounds = atkCurrentWounds;
        targetState.CurrentWounds = defCurrentWounds;

        await stateRepository.UpdateWoundsAsync(attackerState.Id, atkCurrentWounds);
        await stateRepository.UpdateWoundsAsync(targetState.Id, defCurrentWounds);

        if (atkCausedIncap)
        {
            targetState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
            await stateRepository.UpdateGuardAsync(targetState.Id, false);
            targetState.IsOnGuard = false;
            console.MarkupLine($"[red]💀 {Markup.Escape(targetOp.Name)} is incapacitated![/]");
        }
        if (defCausedIncap)
        {
            attackerState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(attackerState.Id, true);
            await stateRepository.UpdateGuardAsync(attackerState.Id, false);
            attackerState.IsOnGuard = false;
            console.MarkupLine($"[red]💀 {Markup.Escape(attacker.Name)} is incapacitated![/]");
        }

        // 11. Persist action
        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Fight,
            ApCost = 1,
            TargetOperativeId = targetState.OperativeId,
            WeaponId = atkWeapon.Id,
            AttackerDice = atkRolls,
            DefenderDice = defRolls,
            NormalDamageDealt = totalAtkDmgDealt,
            CriticalDamageDealt = 0,
            CausedIncapacitation = atkCausedIncap
        };
        await actionRepository.CreateAsync(action);

        var note = console.Prompt(
            new TextPrompt<string>("Narrative note [dim](optional, press enter to skip)[/]:")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(note))
            await actionRepository.UpdateNarrativeAsync(action.Id, note);

        return new FightSessionResult(atkCausedIncap, defCausedIncap, totalAtkDmgDealt, totalDefDmgDealt, targetState.OperativeId);
    }

    private void DisplayFightPools(
        string atkName, int atkWounds, int atkMaxWounds, FightDicePool atkPool,
        string defName, int defWounds, int defMaxWounds, FightDicePool defPool)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn($"[bold]{Markup.Escape(atkName)}[/] (Atk)")
            .AddColumn($"[bold]{Markup.Escape(defName)}[/] (Def)");

        table.AddRow($"Wounds: {atkWounds}/{atkMaxWounds}", $"Wounds: {defWounds}/{defMaxWounds}");

        var maxRows = Math.Max(atkPool.Remaining.Count, defPool.Remaining.Count);
        for (int i = 0; i < maxRows; i++)
        {
            var atkCell = i < atkPool.Remaining.Count
                ? FormatDie("A", i + 1, atkPool.Remaining[i])
                : "";
            var defCell = i < defPool.Remaining.Count
                ? FormatDie("D", i + 1, defPool.Remaining[i])
                : "";
            table.AddRow(atkCell, defCell);
        }
        console.Write(table);
    }

    private static string FormatDie(string prefix, int num, FightDie die)
    {
        var result = die.Result == DieResult.Crit ? "[bold yellow]CRIT[/]" : "[green]HIT [/]";
        return $"{prefix}{num}: {result} [rolled {die.RolledValue}]";
    }

    private static string FormatFightAction(FightAction a)
    {
        var resultLabel = a.ActiveDie.Result == DieResult.Crit ? "CRIT" : "HIT";
        var dieInfo = $"rolled [green]{a.ActiveDie.RolledValue}[/] ({resultLabel})";
        if (a.Type == FightActionType.Strike)
            return $"⚔ Strike — {dieInfo}";
        var targetLabel = a.TargetDie!.Result == DieResult.Crit ? "CRIT" : "HIT";
        return $"🛡 Block — {dieInfo} cancels rolled [green]{a.TargetDie.RolledValue}[/] ({targetLabel})";
    }

    private async Task<int[]> RollOrEnterDiceAsync(int count, string label)
    {
        if (count == 0)
        {
            return [];
        }

        var choice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"[bold]{Markup.Escape(label)}[/] ({count} dice):")
                .AddChoices("Roll for me", "Enter manually"));

        if (choice == "Roll for me")
        {
            var rolled = Enumerable.Range(0, count).Select(_ => Random.Shared.Next(1, 7)).ToArray();
            console.MarkupLine($"  Rolled: [{string.Join(", ", rolled)}]");
            return rolled;
        }

        while (true)
        {
            var input = console.Prompt(
                new TextPrompt<string>($"Enter {count} dice values (space or comma separated):")
                    .AllowEmpty());
            var parts = input.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
            var values = new List<int>();
            var valid = true;
            foreach (var p in parts)
            {
                if (int.TryParse(p, out int v) && v is >= 1 and <= 6)
                {
                    values.Add(v);
                }
                else
                {
                    valid = false;
                    break;
                }
            }

            if (valid && values.Count > 0)
            {
                return values.ToArray();
            }

            console.MarkupLine("[red]Invalid input. Enter integers 1-6 separated by spaces or commas.[/]");
        }
    }
}
