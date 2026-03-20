using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

public record FightDie(int Id, int RolledValue, DieResult Result);

public record FightDicePool(IReadOnlyList<FightDie> Remaining);

public record FightAction(
    FightActionType Type,
    FightDie Die,
    FightDie? TargetDie // null for Strike (no specific target die required)
);

public static class FightResolution
{
    public static DieResult CalculateDie(int roll, int hitThreshold)
    {
        if (roll == 6)
        {
            return DieResult.Crit;
        }

        return roll >= hitThreshold ? DieResult.Hit : DieResult.Miss;
    }

    public static FightDicePool CalculateDice(int[] rolls, int hitThreshold)
    {
        var dice = new List<FightDie>();

        for (var i = 0; i < rolls.Length; i++)
        {
            var result = CalculateDie(rolls[i], hitThreshold);

            if (result != DieResult.Miss)
            {
                dice.Add(new FightDie(i, rolls[i], result));
            }
        }

        return new FightDicePool(dice);
    }

    public static int ApplyStrike(FightDie die, int normalDmg, int critDmg)
    {
        return die.Result == DieResult.Crit ? critDmg : normalDmg;
    }

    public static (FightDicePool UpdatedBlocker, FightDicePool UpdatedBlocked) ApplySingleBlock(
        FightDie blockingDie,
        FightDie targetDie,
        FightDicePool blockerPool,
        FightDicePool blockedPool)
    {
        var newBlocker = new FightDicePool(Remaining: blockerPool.Remaining.Where(d => d.Id != blockingDie.Id).ToList());
        var newBlocked = new FightDicePool(Remaining: blockedPool.Remaining.Where(d => d.Id != targetDie.Id).ToList());

        return (newBlocker, newBlocked);
    }

    /// <summary>
    /// Returns all legal actions for the active pool given the current state.
    /// restrictBlocksToCrits = true: only crit dice may be used for Block actions.
    /// </summary>
    public static IReadOnlyList<FightAction> GetAvailableActions(
        FightDicePool pool,
        FightDicePool targetPool,
        bool restrictBlocksToCrits = false)
    {
        var actions = new List<FightAction>();

        foreach (var die in pool.Remaining)
        {
            // Strike: any die can strike
            actions.Add(new FightAction(FightActionType.Strike, die, null));

            if (restrictBlocksToCrits && die.Result != DieResult.Crit)
            {
                continue;
            }

            // Block rules:
            // Crit die: can block any target die (crit or normal)
            // Normal die: can only block target normals (NOT crits)
            // restrictBlocksToCrits: normal dice CANNOT block at all
            foreach (var targetDie in targetPool.Remaining)
            {
                // normal only blocks normals
                var canBlock = die.Result == DieResult.Crit || targetDie.Result == DieResult.Hit && !restrictBlocksToCrits;

                if (canBlock)
                {
                    actions.Add(new FightAction(FightActionType.Block, die, targetDie));
                }
            }
        }

        return actions;
    }
}
