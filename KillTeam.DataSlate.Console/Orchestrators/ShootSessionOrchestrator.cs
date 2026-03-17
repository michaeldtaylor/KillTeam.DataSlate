using KillTeam.DataSlate.Domain.Events;
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
        bool hasMovedNonDash = false,
        GameEventStream? eventStream = null)
    {
        var isAttackerTeamA = attacker.TeamId == game.Participant1.TeamId;
        var isAttackerTeamId = attacker.TeamId;

        // 1. Target selection: enemy operatives
        var enemyStates = allOperativeStates
            .Where(s => !s.IsIncapacitated
                && allOperatives.TryGetValue(s.OperativeId, out var o)
                && o.TeamId != attacker.TeamId)
            .ToList();

        if (enemyStates.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoValidTargets, "No valid targets available."));
            return new ShootSessionResult(false, 0, null);
        }

        GameOperativeState targetState;
        if (enemyStates.Count == 1)
        {
            targetState = enemyStates[0];
            if (allOperatives.TryGetValue(targetState.OperativeId, out var autoTarget))
            {
                eventStream?.Emit((seq, ts) => new ShootTargetSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, autoTarget.Name, targetState.CurrentWounds, autoTarget.Wounds, true));
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
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.TargetNotFound, "Target operative not found."));
            return new ShootSessionResult(false, 0, null);
        }
        var defenderTeamId = targetOp.TeamId;

        // 2. Weapon selection (ranged only; filter Heavy if moved non-dash)
        var rangedWeapons = attacker.Weapons
            .Where(w => w.Type == WeaponType.Ranged)
            .Where(w => !hasMovedNonDash || !w.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Heavy))
            .ToList();

        if (rangedWeapons.Count == 0)
        {
            eventStream?.Emit((seq, ts) => new CombatWarningEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, CombatWarningKind.NoWeaponsAvailable, "No ranged weapons available."));
            return new ShootSessionResult(false, 0, null);
        }

        Weapon weapon;
        if (rangedWeapons.Count == 1)
        {
            weapon = rangedWeapons[0];
            eventStream?.Emit((seq, ts) => new WeaponSelectedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, weapon.Name, weapon.Atk, weapon.Hit, weapon.NormalDmg, weapon.CriticalDmg, "Attacker", true, false, weapon.Hit));
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
                            ? " [yellow]Saturate[/]"
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
        int[] attackDice = await RollOrEnterDiceAsync(weapon.Atk, $"{Markup.Escape(attacker.Name)} attack dice (Attack: {weapon.Atk})", attacker.Name, "Attacker", "Shoot", isAttackerTeamId, eventStream);

        // 6. Weapon re-rolls + attacker CP re-roll
        attackDice = await rerollOrchestrator.ApplyAttackerRerollsAsync(
            attackDice, weapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name, isAttackerTeamId, eventStream);

        // 7. Defence dice entry (player decides how many to roll)
        var defDiceCount = console.Prompt(
            new TextPrompt<int>("How many defence dice to roll? (0 or more):")
                .Validate(v => v >= 0));
        if (inCover)
            eventStream?.Emit((seq, ts) => new CoverSaveNotifiedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name));

        int[] defDice = defDiceCount == 0
            ? []
            : await RollOrEnterDiceAsync(defDiceCount, $"{Markup.Escape(targetOp.Name)} defence dice", targetOp.Name, "Defender", "Shoot", defenderTeamId, eventStream);

        // 8. Defender CP re-roll
        var isDefenderTeamA = targetOp.TeamId == game.Participant1.TeamId;
        defDice = await rerollOrchestrator.ApplyDefenderRerollAsync(defDice, game.Id, isDefenderTeamA, targetOp.Name, defenderTeamId, eventStream);

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

        if (eventStream is not null)
            eventStream.Emit((seq, ts) => new ShootResultDisplayedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, result.UnblockedCrits, result.UnblockedNormals, result.TotalDamage, inCover, isObscured));
        else
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
            eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, "Shoot"));
        }

        // 11. Stun check
        var stunApplied = result.StunApplied;
        var selfDamage = 0;

        if (stunApplied)
        {
            await stateRepository.SetAplModifierAsync(targetState.Id, -1);
            targetState.AplModifier -= 1;
            eventStream?.Emit((seq, ts) => new StunAppliedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, targetOp.Name, 1));
        }

        // 12. Hot check (self-damage)
        if (weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Hot) && result.SelfDamageDealt > 0)
        {
            selfDamage = result.SelfDamageDealt;
            var newAttackerWounds = Math.Max(0, attackerState.CurrentWounds - selfDamage);
            attackerState.CurrentWounds = newAttackerWounds;
            await stateRepository.UpdateWoundsAsync(attackerState.Id, newAttackerWounds);
            eventStream?.Emit((seq, ts) => new SelfDamageDealtEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, selfDamage, newAttackerWounds));
            if (newAttackerWounds <= 0 && !attackerState.IsIncapacitated)
            {
                attackerState.IsIncapacitated = true;
                await stateRepository.SetIncapacitatedAsync(attackerState.Id, true);
                eventStream?.Emit((seq, ts) => new IncapacitationEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, "SelfDamage"));
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
        eventStream?.Emit((seq, ts) => new ShootResolvedEvent(eventStream.GameSessionId, seq, ts, isAttackerTeamId, attacker.Name, targetOp.Name, result.TotalDamage, causedIncap));

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

    internal async Task<int[]> RollOrEnterDiceAsync(int count, string label, string operativeName = "", string role = "", string phase = "", string participant = "", GameEventStream? eventStream = null)
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
            eventStream?.Emit((seq, ts) => new DiceRolledEvent(eventStream.GameSessionId, seq, ts, participant, operativeName, role, phase, rolled));
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
