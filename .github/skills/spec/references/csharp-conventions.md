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

Always use `var` for local variable declarations when the type can be inferred from the right-hand side.

```csharp
// ✅ Correct
var folder = config["DataSlate:TeamFolder"] ?? "../teams/";
var files = Directory.GetFiles(folder, "*.json");
foreach (var file in files) { ... }

// ❌ Wrong
string folder = config["DataSlate:TeamFolder"] ?? "../teams/";
string[] files = Directory.GetFiles(folder, "*.json");
foreach (string file in files) { ... }
```

Exception: variables initialized with `null` must keep the explicit type:
```csharp
SomeType x = null; // can't infer type from null
```

---

*More conventions will be added here as the project evolves.*
