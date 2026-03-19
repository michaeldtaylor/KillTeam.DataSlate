using KillTeam.DataSlate.Domain.Models;
using KillTeam.DataSlate.Domain.Services;

namespace KillTeam.DataSlate.Domain.Engine;

internal record FightTurnContext(
    FightDicePool ActivePool,
    FightDicePool OpponentPool,
    DieOwner CurrentTurn,
    Operative ActiveOperative,
    Operative OpponentOperative,
    Weapon ActiveWeapon)
{
    public static FightTurnContext Resolve(
        DieOwner turnOrder,
        FightDicePool attackerPool,
        FightDicePool targetPool,
        Operative attacker,
        Operative target,
        Weapon attackerWeapon,
        Weapon? targetWeapon)
    {
        var attackerShouldAct = turnOrder == DieOwner.Attacker
            ? attackerPool.Remaining.Count > 0
            : targetPool.Remaining.Count == 0;

        return attackerShouldAct
            ? new FightTurnContext(attackerPool, targetPool, DieOwner.Attacker, attacker, target, attackerWeapon)
            : new FightTurnContext(targetPool, attackerPool, DieOwner.Target, target, attacker, targetWeapon ?? attackerWeapon);
    }

    public (FightDicePool AttackerPool, FightDicePool TargetPool) Reintegrate(
        FightDicePool activePool,
        FightDicePool opponentPool)
    {
        return CurrentTurn == DieOwner.Attacker
            ? (activePool, opponentPool)
            : (opponentPool, activePool);
    }
}
