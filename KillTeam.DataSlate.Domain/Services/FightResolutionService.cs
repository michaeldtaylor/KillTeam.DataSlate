namespace KillTeam.DataSlate.Domain.Services;

public enum DieResult { Crit, Hit, Miss }
public enum DieOwner { Attacker, Defender }
public enum FightActionType { Strike, Block }

public record FightDie(int Id, int RolledValue, DieResult Result);
public record FightDicePool(DieOwner Owner, IReadOnlyList<FightDie> Remaining);

public record FightAction(
    FightActionType Type,
    FightDie ActiveDie,
    FightDie? TargetDie // null for Strike (no specific target die required)
);

public class FightResolutionService
{
    public DieResult CalculateDie(int roll, int hitThreshold)
    {
        if (roll == 6)
        {
            return DieResult.Crit;
        }

        if (roll >= hitThreshold)
        {
            return DieResult.Hit;
        }

        return DieResult.Miss;
    }

    public FightDicePool CalculateDice(int[] rolls, int hitThreshold, DieOwner owner)
    {
        var dice = new List<FightDie>();
        for (int i = 0; i < rolls.Length; i++)
        {
            var result = CalculateDie(rolls[i], hitThreshold);
            if (result != DieResult.Miss)
            {
                dice.Add(new FightDie(i, rolls[i], result));
            }
        }
        return new FightDicePool(owner, dice);
    }

    public int ApplyStrike(FightDie die, int normalDmg, int critDmg)
        => die.Result == DieResult.Crit ? critDmg : normalDmg;

    public (FightDicePool updatedBlocker, FightDicePool updatedOpponent) ApplySingleBlock(
        FightDie blockingDie, FightDie targetDie,
        FightDicePool blockerPool, FightDicePool opponentPool)
    {
        var newBlocker = blockerPool with
        {
            Remaining = blockerPool.Remaining.Where(d => d.Id != blockingDie.Id).ToList()
        };
        var newOpponent = opponentPool with
        {
            Remaining = opponentPool.Remaining.Where(d => d.Id != targetDie.Id).ToList()
        };
        return (newBlocker, newOpponent);
    }

    /// <summary>
    /// Returns all legal actions for the active pool given the current state.
    /// brutalWeapon = true: opponent's normal dice cannot be used for Block at all.
    /// </summary>
    public IReadOnlyList<FightAction> GetAvailableActions(
        FightDicePool activePool,
        FightDicePool opponentPool,
        bool brutalWeapon = false)
    {
        var actions = new List<FightAction>();

        foreach (var activeDie in activePool.Remaining)
        {
            // Strike: any active die can strike any opponent die
            // (Target die is not needed for strike resolution, but we list available strikes)
            actions.Add(new FightAction(FightActionType.Strike, activeDie, null));

            if (!brutalWeapon || activeDie.Result == DieResult.Crit)
            {
                // Block rules:
                // Crit die: can block any opponent die (crit or normal)
                // Normal die: can only block opponent normals (NOT crits)
                // Brutal: normal dice CANNOT block at all
                foreach (var targetDie in opponentPool.Remaining)
                {
                    var canBlock = activeDie.Result == DieResult.Crit
                        ? true                              // crit blocks anything
                        : targetDie.Result == DieResult.Hit // normal only blocks normals
                            && !brutalWeapon;

                    if (canBlock)
                    {
                        actions.Add(new FightAction(FightActionType.Block, activeDie, targetDie));
                    }
                }
            }
        }

        return actions;
    }
}
