namespace KillTeam.DataSlate.Domain.Models;

public class GameOperativeState
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid GameId { get; set; }

    public Guid OperativeId { get; set; }

    public int CurrentWounds { get; set; }

    public Order Order { get; set; } = Order.Conceal;

    public bool IsReady { get; set; } = true;

    public bool IsOnGuard { get; set; }

    public bool IsIncapacitated { get; set; }

    public bool HasUsedCounteractThisTurningPoint { get; set; }

    public int AplModifier { get; set; }
}
