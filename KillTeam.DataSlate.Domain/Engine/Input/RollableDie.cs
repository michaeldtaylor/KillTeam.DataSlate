namespace KillTeam.DataSlate.Domain.Engine.Input;

public record RollableDie(int Index, int Value, bool HasBeenRerolled = false);
