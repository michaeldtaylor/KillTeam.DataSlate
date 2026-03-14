using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Repositories;
using KillTeam.DataSlate.Domain.Services;
using Spectre.Console;

namespace KillTeam.DataSlate.Console.Orchestrators;

public record BlastSessionResult(bool AnyIncapacitation, int TotalDamage);

public class BlastTorrentSessionOrchestrator(
    IAnsiConsole console,
    CombatResolutionService combatResolutionService,
    RerollOrchestrator rerollOrchestrator,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository,
    IBlastTargetRepository blastTargetRepository)
{
    public async Task<BlastSessionResult> RunAsync(
        Operative attacker,
        GameOperativeState attackerState,
        Operative primaryTarget,
        GameOperativeState primaryTargetState,
        Weapon weapon,
        IReadOnlyList<GameOperativeState> allOperativeStates,
        IReadOnlyDictionary<Guid, Operative> allOperatives,
        Game game,
        TurningPoint tp,
        Activation activation)
    {
        var isAttackerTeamA = attacker.TeamName == game.TeamAName;
        var weaponType = weapon.ParsedRules.Any(r => r.Kind == SpecialRuleKind.Torrent) ? "Torrent" : "Blast";

        console.MarkupLine($"[bold yellow]⚠ [MULTI-TARGET][/] This weapon hits multiple targets. [{weaponType}]");

        // Additional target selection (excluding primary, must not be incapacitated)
        var additionalCandidates = allOperativeStates
            .Where(s => s.OperativeId != primaryTarget.Id && !s.IsIncapacitated && allOperatives.ContainsKey(s.OperativeId))
            .ToList();

        var additionalTargetStates = new List<GameOperativeState>();
        if (additionalCandidates.Count > 0)
        {
            additionalTargetStates = console.Prompt(
                new MultiSelectionPrompt<GameOperativeState>()
                    .Title("Select additional targets (space to toggle, enter to confirm):")
                    .UseConverter(s =>
                    {
                        if (!allOperatives.TryGetValue(s.OperativeId, out var o))
                        {
                            return s.OperativeId.ToString();
                        }
                        var isFriendly = o.TeamName == attacker.TeamName;
                        var friendly = isFriendly ? " [red][FRIENDLY FIRE!][/]" : "";
                        return $"{Markup.Escape(o.Name)} (W:{s.CurrentWounds}/{o.Wounds}){friendly}";
                    })
                    .AddChoices(additionalCandidates)
                    .NotRequired());
        }

        var allTargetStates = new List<GameOperativeState> { primaryTargetState }.Concat(additionalTargetStates).ToList();

        // Friendly fire confirmation
        var friendlyCount = allTargetStates.Count(s =>
            allOperatives.TryGetValue(s.OperativeId, out var o) && o.TeamName == attacker.TeamName);
        if (friendlyCount > 0)
        {
            console.MarkupLine($"[red]⚠ This will affect {friendlyCount} friendly operative(s).[/]");
            if (!console.Confirm("Confirm?", defaultValue: false))
            {
                return new BlastSessionResult(false, 0);
            }
        }

        // Shared attack dice
        int[] attackDice = await RollOrEnterDiceAsync(weapon.Atk, $"{Markup.Escape(attacker.Name)} attack dice (ATK {weapon.Atk})");
        attackDice = await rerollOrchestrator.ApplyAttackerRerollsAsync(
            attackDice, weapon.ParsedRules.ToList(), game.Id, isAttackerTeamA, attacker.Name);

        // Count shared raw crits (for PiercingCrits)
        var effectiveHit = weapon.Hit;
        var rawCrits = attackDice.Count(d => d == 6);

        // Primary action record
        var action = new GameAction
        {
            Id = Guid.NewGuid(),
            ActivationId = activation.Id,
            Type = ActionType.Shoot,
            ApCost = 1,
            TargetOperativeId = primaryTarget.Id,
            WeaponId = weapon.Id,
            AttackerDice = attackDice
        };

        var anyIncapacitation = false;
        var totalDamage = 0;
        var primaryActionPersisted = false;

        // Process each target
        for (int i = 0; i < allTargetStates.Count; i++)
        {
            var targetState = allTargetStates[i];

            if (!allOperatives.TryGetValue(targetState.OperativeId, out var targetOp))
            {
                continue;
            }

            console.Write(new Rule($"[bold]{Markup.Escape(targetOp.Name)}[/] (W: {targetState.CurrentWounds}/{targetOp.Wounds})"));

            var coverOptions = new[] { "In cover", "Obscured", "Neither" };
            var coverChoice = console.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Is {Markup.Escape(targetOp.Name)} in cover or obscured?")
                    .AddChoices(coverOptions));
            var inCover = coverChoice == "In cover";
            var isObscured = coverChoice == "Obscured";

            var defDiceCount = console.Prompt(
                new TextPrompt<int>($"  How many defence dice for {Markup.Escape(targetOp.Name)}? (0 or more):")
                    .Validate(v => v >= 0));

            int[] defDice = defDiceCount == 0
                ? []
                : await RollOrEnterDiceAsync(defDiceCount, $"{Markup.Escape(targetOp.Name)} defence dice");

            var isDefenderTeamA = targetOp.TeamName == game.TeamAName;
            defDice = await rerollOrchestrator.ApplyDefenderRerollAsync(defDice, game.Id, isDefenderTeamA, targetOp.Name);

            var ctx = new ShootContext(
                AttackDice: attackDice,
                DefenceDice: defDice,
                InCover: inCover,
                IsObscured: isObscured,
                HitThreshold: effectiveHit,
                SaveThreshold: targetOp.Save,
                NormalDmg: weapon.NormalDmg,
                CritDmg: weapon.CriticalDmg,
                WeaponRules: weapon.ParsedRules.ToList()
            );

            var result = combatResolutionService.ResolveShoot(ctx);
            var dmg = result.TotalDamage;

            var newWounds = Math.Max(0, targetState.CurrentWounds - dmg);
            var causedIncap = newWounds <= 0 && !targetState.IsIncapacitated;

            targetState.CurrentWounds = newWounds;
            await stateRepository.UpdateWoundsAsync(targetState.Id, newWounds);

            if (causedIncap)
            {
                targetState.IsIncapacitated = true;
                await stateRepository.SetIncapacitatedAsync(targetState.Id, true);
                await stateRepository.UpdateGuardAsync(targetState.Id, false);
                targetState.IsOnGuard = false;
                anyIncapacitation = true;
                console.MarkupLine($"[red]💀 {Markup.Escape(targetOp.Name)} is incapacitated![/]");
            }

            totalDamage += dmg;

            DisplayShootResult(targetOp.Name, result, inCover, isObscured);

            // Persist primary action on first target
            if (!primaryActionPersisted)
            {
                action.DefenderDice = defDice;
                action.TargetInCover = inCover;
                action.IsObscured = isObscured;
                action.NormalHits = result.UnblockedNormals;
                action.CriticalHits = result.UnblockedCrits;
                action.NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg;
                action.CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg;
                action.CausedIncapacitation = causedIncap;
                await actionRepository.CreateAsync(action);
                primaryActionPersisted = true;
            }
            else
            {
                var blastTarget = new BlastTarget
                {
                    Id = Guid.NewGuid(),
                    ActionId = action.Id,
                    TargetOperativeId = targetState.OperativeId,
                    OperativeName = targetOp.Name,
                    DefenderDice = defDice,
                    NormalHits = result.UnblockedNormals,
                    CriticalHits = result.UnblockedCrits,
                    NormalDamageDealt = result.UnblockedNormals * weapon.NormalDmg,
                    CriticalDamageDealt = result.UnblockedCrits * weapon.CriticalDmg,
                    CausedIncapacitation = causedIncap
                };
                await blastTargetRepository.CreateAsync(blastTarget);
            }
        }

        if (!primaryActionPersisted)
        {
            await actionRepository.CreateAsync(action);
        }

        var note = console.Prompt(
            new TextPrompt<string>("Narrative note [dim](optional, press enter to skip)[/]:")
                .AllowEmpty());
        if (!string.IsNullOrWhiteSpace(note))
        {
            await actionRepository.UpdateNarrativeAsync(action.Id, note);
        }

        return new BlastSessionResult(anyIncapacitation, totalDamage);
    }

    private void DisplayShootResult(string targetName, ShootResult result, bool inCover, bool isObscured)
    {
        var table = new Table().AddColumn("Stat").AddColumn("Value");
        table.AddRow("Unblocked Crits", result.UnblockedCrits.ToString());
        table.AddRow("Unblocked Normals", result.UnblockedNormals.ToString());
        table.AddRow("Total Damage", $"[bold]{result.TotalDamage}[/]");
        if (inCover)
        {
            table.AddRow("Cover", "[green]In Cover[/]");
        }

        if (isObscured)
        {
            table.AddRow("Obscured", "[green]Obscured[/]");
        }
        console.Write(table);
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
