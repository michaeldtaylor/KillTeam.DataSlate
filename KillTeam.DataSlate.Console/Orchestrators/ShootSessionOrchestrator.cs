using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public record ShootSessionResult(
    bool CausedIncapacitation,
    int DamageDealt,
    Guid? TargetOperativeId);

public class ShootSessionOrchestrator(
    IAnsiConsole console,
    CombatResolutionService combatResolutionService,
    RerollOrchestrator rerollOrchestrator,
    BlastTorrentSessionOrchestrator blastTorrentOrchestrator,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository)
{
    public async Task<ShootSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation,
        bool hasMovedNonDash = false)
    {
        var isAttackerTeamA = attacker.TeamId == game.Participant1.TeamId;

        // 1. Target selection: enemy operatives
        var enemyStates = allOperativeStates
            .Where(s => !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId != attacker.TeamId)
            .ToList();

        if (enemyStates.Count == 0)
        {
            console.MarkupLine("[yellow]No valid targets available.[/]");
            return new ShootSessionResult(false, 0, null);
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
                    .Title("Select a target to shoot:")
                    .UseConverter(s => allOperatives.TryGetValue(s.OperativeId, out var o)
                        ? $"{Markup.Escape(o.Name)} (Wounds: {s.CurrentWounds}/{o.Wounds})"
                        : s.OperativeId.ToString())
                    .AddChoices(enemyStates));
        }

        if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
        {
            console.MarkupLine("[red]Target operative not found.[/]");
            return new ShootSessionResult(false, 0, null);
        }

        // 2. Weapon selection (ranged only; filter Heavy if moved non-dash)
        var rangedWeapons = attacker.Weapons
            .Where(w => w.Type == WeaponType.Ranged)
            .Where(w => !hasMovedNonDash || !w.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Heavy))
            .ToList();

        if (rangedWeapons.Count == 0)
        {
            console.MarkupLine("[yellow]No ranged weapons available.[/]");
            return new ShootSessionResult(false, 0, null);
        }

        Weapon weapon;
        if (rangedWeapons.Count == 1)
        {
            weapon = rangedWeapons[0];
            var rulesText = weapon.ParsedRules.Count > 0
                ? $" | {string.Join(", ", weapon.ParsedRules.Select(r => r.RawText))}"
                : "";
            console.MarkupLine($"[dim]Auto-selected ranged weapon:[/] {Markup.Escape(weapon.Name)} (Attack: [green]{weapon.Atk}[/] | Hit: [green]{weapon.Hit}+[/] | Normal: [green]{weapon.NormalDmg}[/] | Crit: [green]{weapon.CriticalDmg}[/]{Markup.Escape(rulesText)})");
        }
        else
        {
            weapon = console.Prompt(
                new SelectionPrompt<Weapon>()
                    .Title("Select a ranged weapon:")
                    .UseConverter(w =>
                    {
                        var rulesText = w.ParsedRules.Count > 0
                            ? $" | {string.Join(", ", w.ParsedRules.Select(r => r.RawText))}"
                            : "";
                        var saturate = w.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Saturate)
                            ? " [yellow]⚠ Saturate[/]"
                            : "";
                        return $"{Markup.Escape(w.Name)} (Attack: [green]{w.Atk}[/] | Hit: [green]{w.Hit}+[/] | Normal: [green]{w.NormalDmg}[/] | Crit: [green]{w.CriticalDmg}[/]{Markup.Escape(rulesText)}){saturate}";
                    })
                    .AddChoices(rangedWeapons));
        }

        // Delegate Blast / Torrent
        if (weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Blast || r.Kind == SpecialRuleKind.Torrent))
        {
            var blastResult = await blastTorrentOrchestrator.RunAsync(
                attacker, attackerState,
                targetOp, targetState,
                weapon,
                allOperativeStates,
                allOperatives,
                game, tp, activation);
            return new ShootSessionResult(blastResult.AnyIncapacitation, blastResult.TotalDamage, targetState.OperativeId);
        }

        // 3. Cover check
        var coverChoice = console.Prompt(
            new SelectionPrompt<string>()
                .Title($"Is {Markup.Escape(targetOp.Name)} in cover or obscured?")
                .AddChoices("In cover", "Obscured", "Neither"));
        var inCover = coverChoice == "In cover";
        var isObscured = coverChoice == "Obscured";

        // 4. Fight assist (reduces hit threshold)
        var fightAssist = console.Prompt(
            new TextPrompt<int>("How many non-engaged friendly allies within 6\" of target? (0-2):")
                .DefaultValue(0)
                .Validate(v => v is >= 0 and <= 2));

        // 5. Attacker dice entry
        int[] attackDice = await RollOrEnterDiceAsync(weapon.Atk, $"{Markup.Escape(attacker.Name)} attack dice (Attack: {weapon.Atk})");

        // 6. Weapon re-rolls + attacker CP re-roll
        attackDice = await rerollOrchestrator.ApplyAttackerRerollsAsync(
            attackDice, weapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name);

        // 7. Defence dice entry (player decides how many to roll)
        var defDiceCount = console.Prompt(
            new TextPrompt<int>("How many defence dice to roll? (0 or more):")
                .Validate(v => v >= 0));
        if (inCover)
            console.MarkupLine("[green]+1 cover save will be added automatically.[/]");

        int[] defDice = defDiceCount == 0
            ? []
            : await RollOrEnterDiceAsync(defDiceCount, $"{Markup.Escape(targetOp.Name)} defence dice");

        // 8. Defender CP re-roll
        var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;
        defDice = await rerollOrchestrator.ApplyDefenderRerollAsync(defDice, game.Id, isDefenderTeamA, targetOp.Name);

        // 9. Resolve shoot
        var ctx = new ShootContext(
            AttackDice: attackDice,
            DefenceDice: defDice,
            InCover: inCover,
            IsObscured: isObscured,
            HitThreshold: weapon.Hit,
            SaveThreshold: targetOp.Save,
            NormalDmg: weapon.NormalDmg,
            CritDmg: weapon.CriticalDmg,
            WeaponRules: weapon.ParsedRules.ToList(),
            FightAssistBonus: fightAssist
        );

        var result = combatResolutionService.ResolveShoot(ctx);

        DisplayShootResult(targetOp.Name, result, inCover, isObscured);

        // 10. Apply damage
        var newWounds = Math.Max(0, targetState.CurrentWounds - result.TotalDamage);
        var causedIncap = newWounds <= 0 && !targetState.IsIncapacitated;

        targetState.CurrentWounds = newWounds;
        await stateRepository.UpdateWoundsAsync(targetState.Id, newWounds);

        if (causedIncap)
        {
            targetState.IsIncapacitated = true;
            await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
            await stateRepository.UpdateGuardAsync(targetState.Id, false);
            targetState.IsOnGuard = false;
            console.MarkupLine($"[red]💀 {Markup.Escape(targetOp.Name)} is incapacitated![/]");
        }

        // 11. Stun check
        var stunApplied = result.StunApplied;
        var selfDamage = 0;

        if (stunApplied)
        {
            await stateRepository.SetAplModifierAsync(targetState.Id, -1);
            targetState.AplModifier -= 1;
            console.MarkupLine($"[yellow]⚡ Stun applied to {Markup.Escape(targetOp.Name)} (-1 APL)[/]");
        }

        // 12. Hot check (self-damage)
        if (weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Hot) && result.SelfDamageDealt > 0)
        {
            selfDamage = result.SelfDamageDealt;
            var newAttackerWounds = Math.Max(0, attackerState.CurrentWounds - selfDamage);
            attackerState.CurrentWounds = newAttackerWounds;
            await stateRepository.UpdateWoundsAsync(attackerState.Id, newAttackerWounds);
            console.MarkupLine($"[red]🔥 Hot! {Markup.Escape(attacker.Name)} takes {selfDamage} self-damage! (Wounds: {newAttackerWounds})[/]");
            if (newAttackerWounds <= 0 && !attackerState.IsIncapacitated)
            {
                attackerState.IsIncapacitated = true;
                await stateRepository.SetIncapacitatedAsync(attackerState.Id, true);
                console.MarkupLine($"[red]💀 {Markup.Escape(attacker.Name)} is incapacitated by their own weapon![/]");
            }
        }

        // 13. Persist action
        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = targetState.OperativeId,
            WeaponId = weapon.Id,
            AttackerDice = attackDice,
            DefenderDice = defDice,
            TargetInCover = inCover,
            IsObscured = isObscured,
            NormalHits = result.UnblockedNormals,
            CriticalHits = result.UnblockedCrits,
            NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg,
            CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg,
            CausedIncapacitation = causedIncap,
            SelfDamageDealt = selfDamage,
            StunApplied = stunApplied
        };
        await actionRepository.CreateAsync(action);

        // 14. Narrative note
        var note = console.Prompt(
            new TextPrompt<string>("Narrative note [dim](optional, press enter to skip)[/]:")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new ShootSessionResult(causedIncap, result.TotalDamage, targetState.OperativeId);
    }

    private void DisplayShootResult(string targetName, ShootResult result, bool inCover, bool isObscured)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Result")
            .AddColumn("Value");
        table.AddRow("Unblocked Crits", $"[bold]{result.UnblockedCrits}[/]");
        table.AddRow("Unblocked Normals", $"[bold]{result.UnblockedNormals}[/]");
        table.AddRow("Total Damage", $"[bold red]{result.TotalDamage}[/]");
        if (inCover)
        {
            table.AddRow("Cover Save", "[green]Applied[/]");
        }

        if (isObscured)
        {
            table.AddRow("Obscured", "[green]Crits converted[/]");
        }
        console.Write(table);
    }

    internal async Task<int[]> RollOrEnterDiceAsync(int count, string label)
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
