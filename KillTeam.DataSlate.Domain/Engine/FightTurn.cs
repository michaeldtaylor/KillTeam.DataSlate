using KillTeam.DataSlate.Domain.Models;

namespace KillTeam.DataSlate.Domain.Engine;

internal record FightTurn(
    FightDicePool ActingPool,
    FightDicePool OpposingPool,
    DieOwner CurrentTurn,
    Operative ActingOperative,
    Operative OpposingOperative,
    Weapon ActingWeapon,
    bool RestrictBlocksToCrits)
{
    public static FightTurn Resolve(
        DieOwner turnOrder,
        FightDicePool attackerPool,
        FightDicePool targetPool,
        Operative attacker,
        Operative target,
        Weapon attackerWeapon,
        Weapon? targetWeapon,
        bool blockRestrictedToCrits)
    {
        var attackerShouldAct = turnOrder == DieOwner.Attacker
            ? attackerPool.Remaining.Count > 0
            : targetPool.Remaining.Count == 0;

        var currentTurn = attackerShouldAct ? DieOwner.Attacker : DieOwner.Target;

        return attackerShouldAct
            ? new FightTurn(attackerPool, targetPool, currentTurn, attacker, target, attackerWeapon, blockRestrictedToCrits && currentTurn == DieOwner.Attacker)
            // targetWeapon is never null here — if it were, targetPool would be empty and attackerShouldAct would always be true
            : new FightTurn(targetPool, attackerPool, currentTurn, target, attacker, targetWeapon!, blockRestrictedToCrits && currentTurn == DieOwner.Attacker);
    }

    public (FightDicePool AttackerPool, FightDicePool TargetPool) Reintegrate(FightDicePool actingPool, FightDicePool opposingPool)
    {
        return CurrentTurn == DieOwner.Attacker
            ? (actingPool, opposingPool)
            : (opposingPool, actingPool);
    }
}
