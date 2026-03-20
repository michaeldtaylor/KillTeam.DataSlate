# C# Coding Conventions

This is the living conventions document for the KillTeam DataSlate project.
Copilot must follow all rules here when generating or editing C# code.

---

## 1. Always use braces

All control-flow blocks (`if`, `else`, `for`, `foreach`, `while`, `do`, `using`) **must** use braces, even for single-statement bodies.

```csharp
// ✅ Correct
if (operative is null)
{
    return;
}

foreach (var op in operatives)
{
    Process(op);
}

// ❌ Wrong
if (operative is null)
    return;
```

---

## 2. Throw with braces, multi-line

`throw new` must always be inside a braced block. Never inline or braceless.

```csharp
// ✅ Correct
if (string.IsNullOrWhiteSpace(name))
{
    throw new TeamValidationException("Name is required.");
}

// ❌ Wrong — braceless
if (string.IsNullOrWhiteSpace(name))
    throw new TeamValidationException("Name is required.");

// ❌ Wrong — inline (even though single-line)
if (string.IsNullOrWhiteSpace(name)) throw new TeamValidationException("Name is required.");
```

---

## 3. Blank line between using block, namespace, and class

```csharp
// ✅ Correct
using System;
using System.Collections.Generic;

namespace KillTeam.DataSlate.Console.Commands;

public class ImportTeamsCommand
{

// ❌ Wrong — no blank lines
using System;
namespace KillTeam.DataSlate.Console.Commands;
public class ImportTeamsCommand
{
```

---

## 4. Blank line between variable declarations and statements

When a method starts with one or more variable declarations, add a blank line before the first statement.

```csharp
// ✅ Correct
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var folder = config["DataSlate:TeamFolder"] ?? "../teams/";
    var files = Directory.GetFiles(folder, "*.json");

    foreach (var file in files)
    {
        await ImportFileAsync(file);
    }
}

// ❌ Wrong — no blank line
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var folder = config["DataSlate:TeamFolder"] ?? "../teams/";
    var files = Directory.GetFiles(folder, "*.json");
    foreach (var file in files)
    {
        await ImportFileAsync(file);
    }
}
```

---

## 5. Blank line between properties

Each property in a class or record must be separated by a blank line.

```csharp
// ✅ Correct
public class Operative
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Name { get; set; }

    public required string OperativeType { get; set; }

    public int Move { get; set; }
}

// ❌ Wrong — no blank lines between properties
public class Operative
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string OperativeType { get; set; }
    public int Move { get; set; }
}
```

---

## 6. Use `required` on mandatory properties

Properties that must be set at construction time should use the `required` modifier.

```csharp
// ✅ Correct
public class Game
{
    public required string TeamAName { get; set; }

    public required string TeamBName { get; set; }
}
```

**Exception — serialization targets:** When a class is used as a `System.Text.Json` deserialization target (e.g. internal `JsonTeam` DTOs), `required` may be omitted if it would require `[JsonConstructor]` / `SetsRequiredMembers` boilerplate. Add a brief comment:

```csharp
// System.Text.Json deserialization target — required omitted intentionally
public string? Name { get; set; }
```

---

---

## 7. Enum values each on their own line

Each enum member must be on its own line. Never put multiple values on a single line.

```csharp
// ✅ Correct
public enum ActionType
{
    Reposition,
    Dash,
    FallBack,
    Charge,
    Shoot,
    Fight,
    Guard,
    Other,
}

// ❌ Wrong — multiple values per line
public enum ActionType { Reposition, Dash, FallBack, Charge, Shoot, Fight, Guard, Other }
```

---

## 8. Trailing comma on last item

Always include a trailing comma on the last item of:
- Enums
- Multi-line collection initialisers
- Multi-line argument/parameter lists

```csharp
// ✅ Correct — enum
public enum SpecialRuleKind
{
    Devastating,
    AP,
    Lethal,
}

// ✅ Correct — collection initialiser
var weapons = new List<Weapon>
{
    new() { Name = "Bolt Rifle" },
    new() { Name = "Chainsword" },
};

// ❌ Wrong — no trailing comma
public enum SpecialRuleKind
{
    Devastating,
    AP,
    Lethal
}
```

---

## 9. Blank line between all class/interface members

All members — including methods — in a class or interface must be separated by a blank line. This extends Rule 5 (which covers properties) to methods, events, and any other member.

```csharp
// ✅ Correct
public interface IPlayerRepository
{
    Task AddAsync(Player player);

    Task<IEnumerable<Player>> GetAllAsync();

    Task DeleteAsync(Guid id);
}

// ❌ Wrong
public interface IPlayerRepository
{
    Task AddAsync(Player player);
    Task<IEnumerable<Player>> GetAllAsync();
    Task DeleteAsync(Guid id);
}
```

---

## 10. Use `init` instead of `set` where possible

Properties that are only assigned at construction time must use `init` instead of `set`. This communicates intent and prevents accidental mutation after creation.

```csharp
// ✅ Correct
public required string Name { get; init; }
public int Move { get; init; }

// ❌ Wrong
public required string Name { get; set; }
public int Move { get; set; }
```

Exception — mutable game state: Properties that are mutated during play (e.g. `GameOperativeState.CurrentWounds`, `Game.CpTeamA`) must keep `set`.

Exception — JSON deserialization targets: Internal DTOs used with `System.Text.Json` must keep `set` (and should have a comment noting why).

---

## 11. Always use `var` for local variables

Always use `var` for local variable declarations when the type can be inferred from the right-hand side. This applies everywhere — method bodies, loop initializers, `foreach`, and nested blocks.

```csharp
// ✅ Correct
var folder = config["DataSlate:TeamFolder"] ?? "../teams/";
var files = Directory.GetFiles(folder, "*.json");

foreach (var file in files) { ... }

for (var i = 0; i < maxRows; i++) { ... }

// ❌ Wrong — explicit type when var is inferrable
string folder = config["DataSlate:TeamFolder"] ?? "../teams/";
string[] files = Directory.GetFiles(folder, "*.json");
foreach (string file in files) { ... }
for (int i = 0; i < maxRows; i++) { ... }
```

Exception: variables initialized with `null` must keep the explicit type:
```csharp
SomeType x = null; // can't infer type from null
```

---

## 12. Blank line before `return` (when not the only statement)

When a `return` statement is not the only statement in a block, add a blank line immediately before it.

```csharp
// ✅ Correct
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var result = await DoWorkAsync();

    return result;
}

if (!await reader.ReadAsync())
{
    AnsiConsole.MarkupLine("[red]Not found.[/]");

    return 1;
}

// ❌ Wrong — no blank line before return
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var result = await DoWorkAsync();
    return result;
}
```

Exception: a `return` that is the sole statement in a block (e.g. a guard clause with no preceding statement) does not need a blank line before it:

```csharp
// ✅ Correct — sole statement, no blank line needed
if (operative is null)
{
    return;
}
```

---

## 13. Using directives in Rider sort order

Using directives must be sorted in Rider's default "Optimise Imports" order:
1. `System.*` namespaces first, sorted alphabetically within the group
2. All other namespaces after, sorted alphabetically
3. No blank line between the two groups

```csharp
// ✅ Correct
using System.ComponentModel;
using System.Text.Json;
using KillTeam.DataSlate.Domain.Models;
using Microsoft.Data.Sqlite;
using Spectre.Console;

// ❌ Wrong — non-System usings before System
using KillTeam.DataSlate.Domain.Models;
using System.ComponentModel;
using Microsoft.Data.Sqlite;
```

---

## 14. `await using` for `IAsyncDisposable` types

Use `await using` (instead of `using`) whenever the type implements `IAsyncDisposable`. This includes `SqliteConnection`, `SqliteCommand`, `SqliteDataReader`, and `SqliteTransaction`.

Prefer the `await using var` declaration form over the `await using ( )` block form.

```csharp
// ✅ Correct
await using var connection = new SqliteConnection(connectionString);
await connection.OpenAsync();

await using var command = connection.CreateCommand();
command.CommandText = sql;

await using var reader = await command.ExecuteReaderAsync();

// ❌ Wrong — plain using for IAsyncDisposable
using var conn = new SqliteConnection(connectionString);
using (var reader = await command.ExecuteReaderAsync()) { ... }
```

---

## 15. Use descriptive variable names — no abbreviations

Variable names must be fully descriptive. Single-letter variables, truncated names, and cryptic abbreviations are not allowed.

```csharp
// ✅ Correct
await using var command = connection.CreateCommand();
await using var reader = await command.ExecuteReaderAsync();
await using var transaction = connection.BeginTransaction();

foreach (var (parameterName, parameterValue) in parameters)
{
    command.Parameters.AddWithValue(parameterName, parameterValue ?? DBNull.Value);
}

var playerGames = 0;
var playerWins = 0;
var winPercentage = playerGames > 0 ? $"{playerWins * 100 / playerGames}%" : "—";

// ❌ Wrong — abbreviations and single-letter names
using var cmd = connection.CreateCommand();
using var r = await cmd.ExecuteReaderAsync();
using var tx = connection.BeginTransaction();
var g = 0;
var w = 0;
var pct = g > 0 ? $"{w * 100 / g}%" : "—";
var gCmd = connection.CreateCommand();
```

Common rename mappings:
| Abbreviation | Descriptive name |
|---|---|
| `cmd` | `command` |
| `conn` | `connection` |
| `tx` | `transaction` |
| `r` / `reader` abbrev | `reader` |
| `tp` (variable) | `turningPoint` / `turningPoints` |
| `op` (variable) | `operative` |
| `atk` prefix | `attacker` (e.g. `atkCell` → `attackerCell`, `atkPool` → `attackerPool`) |
| `def` prefix | `defender` (e.g. `defCell` → `defenderCell`, `defPool` → `defenderPool`) |
| `g`, `w` | `playerGames`, `playerWins` |
| `pct` | `winPercentage` |
| `gCmd` | `gameQueryCommand` (or context-specific) |
| `t` (table) | `table` / `statsTable` |

Exception: loop variables in short, unambiguous `foreach` may use conventional short forms only if the type makes the meaning obvious (e.g. `foreach (var file in files)`). Single-letter names are never acceptable.

Exception: LINQ/lambda expression parameters may use short conventional names when the lambda body is compact and the type is obvious from context (e.g. `weapons.Where(w => w.Type == WeaponType.Ranged)`, `rules.Any(r => r.Kind == WeaponRuleKind.Heavy)`, `handlers.All(h => h.IsAvailable(weapon, context))`). This applies only inside the lambda expression itself, not to variables in enclosing scope.

---

## 16. Blank line after `var` / `using var` / `await using var` declaration block

When a method or block begins with one or more `var`, `using var`, or `await using var` declarations, add a blank line after the last declaration before the first non-declaration statement. This extends Rule 4 to cover `using var` and `await using var` patterns. **This rule also applies inside loops and nested blocks — not just at the start of a method.**

```csharp
// ✅ Correct — method level
await using var connection = new SqliteConnection(connectionString);
await using var command = connection.CreateCommand();

command.CommandText = sql;
await command.ExecuteNonQueryAsync();

// ✅ Correct — inside a loop
for (var i = 0; i < maxRows; i++)
{
    var attackerCell = i < attackerDice.Count ? FormatDie(attackerDice[i]) : string.Empty;
    var defenderCell = i < defenderDice.Count ? FormatDie(defenderDice[i]) : string.Empty;

    table.AddRow(attackerCell, defenderCell);
}

// ❌ Wrong — no blank line after declaration block
await using var connection = new SqliteConnection(connectionString);
await using var command = connection.CreateCommand();
command.CommandText = sql;
await command.ExecuteNonQueryAsync();

// ❌ Wrong — no blank line after var block inside loop
for (var i = 0; i < maxRows; i++)
{
    var attackerCell = i < attackerDice.Count ? FormatDie(attackerDice[i]) : string.Empty;
    var defenderCell = i < defenderDice.Count ? FormatDie(defenderDice[i]) : string.Empty;
    table.AddRow(attackerCell, defenderCell);
}
```

---

## 17. Use `string.Empty` instead of `""`

Always use `string.Empty` instead of `""` for empty string literals. This avoids unnecessary string allocation and makes intent explicit.

```csharp
// ✅ Correct
var note = string.Empty;
return string.Empty;
var flagStr = flags.Count > 0 ? $"({string.Join(", ", flags)})" : string.Empty;
command.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(name) ? string.Empty : name);

// ❌ Wrong
var note = "";
return "";
var flagStr = flags.Count > 0 ? $"({string.Join(", ", flags)})" : "";
```

**Exceptions** — `string.Empty` cannot be used as:
- A default parameter value: `void Foo(string name = "")` — this is a compile error; keep `""`
- An attribute argument: `[Description("")]` — keep `""`

---

## 18. Multi-line formatting for calls and definitions with 5+ arguments

When a constructor call, method call, or type definition (record primary constructor, class constructor, method signature) has **5 or more** arguments or parameters — or would exceed **120 characters** on a single line — format with **each argument/parameter on its own line**, indented 4 spaces from the containing statement. The closing delimiter (`)`, `));`, `);`) follows the last argument on the same line.

```csharp
// ✅ Correct — lambda body on new line, new EventName( indented +4, args indented +4 more
eventStream?.Emit((gameSessionId, seq, ts) =>
    new ShootResolvedEvent(
        gameSessionId,
        seq,
        ts,
        attackerTeamId,
        attacker.Name,
        targetOp.Name,
        result.TotalDamage,
        causedIncap));

// ❌ Wrong — new EventName( on same line as =>
eventStream?.Emit((gameSessionId, seq, ts) => new ShootResolvedEvent(
    gameSessionId,
    seq,
    ts,
    attackerTeamId,
    attacker.Name,
    targetOp.Name,
    result.TotalDamage,
    causedIncap));

// ❌ Wrong — multiple args crammed per line
eventStream?.Emit((gameSessionId, seq, ts) => new ShootResolvedEvent(
    gameSessionId, seq, ts, attackerTeamId,
    attacker.Name, targetOp.Name, result.TotalDamage, causedIncap));
```

// ❌ Wrong — partial multi-line: once broken, every arg must have its own line
return await engine.ProcessAsync(
    actingEnemy, allOperativeStates, allOperatives,
    game, turningPoint, sequenceCounter, eventStream);

// ❌ Wrong — first arg on the same line as the call
DisplaySummary(player1Operative, player2Operative,
    attackerDamage, targetDamage,
    player1Incapacitated: causedIncap1,
    player2Incapacitated: causedIncap2);
```

The same rule applies to record primary constructor definitions:

```csharp
// ✅ Correct
public sealed record ShootResolvedEvent(
    Guid GameSessionId,
    int SequenceNumber,
    DateTime Timestamp,
    string Participant,
    string AttackerName,
    string TargetName,
    int DamageDealt,
    bool CausedIncapacitation)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);

// ❌ Wrong — grouped on shared lines
public sealed record ShootResolvedEvent(
    Guid GameSessionId, int SequenceNumber, DateTime Timestamp, string Participant,
    string AttackerName, string TargetName, int DamageDealt, bool CausedIncapacitation)
    : GameEvent(GameSessionId, SequenceNumber, Timestamp, Participant);
```

**Exceptions:**
- Calls with 4 or fewer arguments may remain on a single line if they fit within 120 characters.
- LINQ lambda bodies (`.Select(w => ...)`, `.Where(r => ...)`) are exempt — use compact inline style.

---

## 19. Blank line after multi-line constructs

A blank line must follow the closing delimiter (`};`, `);`, `])`) of any multi-line
construct — object initialiser, collection initialiser, or multi-line method call —
before the next statement in the same block.

**Exception:** no blank line required when the next line is the enclosing block's
closing `}`.

```csharp
// ✅ Correct
var activation = new Activation
{
    Id = Guid.NewGuid(),
    TurningPointId = turningPoint.Id,
    IsCounteract = false
};

await activationRepository.CreateAsync(activation);

// ✅ Correct — consecutive multi-line initialisers
var player1State = new GameOperativeState
{
    GameId = game.Id,
    CurrentWounds = player1Operative.Wounds
};

var player2State = new GameOperativeState
{
    GameId = game.Id,
    CurrentWounds = player2Operative.Wounds
};

// ❌ Wrong — no blank line after multi-line initialiser
var activation = new Activation
{
    Id = Guid.NewGuid(),
    TurningPointId = turningPoint.Id,
    IsCounteract = false
};
await activationRepository.CreateAsync(activation);

// ❌ Wrong — two consecutive multi-line initialisers with no blank line
var player1State = new GameOperativeState { GameId = game.Id, CurrentWounds = 10 };
var player2State = new GameOperativeState
{
    GameId = game.Id,
    CurrentWounds = player2Operative.Wounds
};
```

---

*More conventions will be added here as the project evolves.*
