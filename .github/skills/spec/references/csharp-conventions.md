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
    throw new KillTeamValidationException("Name is required.");
}

// ❌ Wrong — braceless
if (string.IsNullOrWhiteSpace(name))
    throw new KillTeamValidationException("Name is required.");

// ❌ Wrong — inline (even though single-line)
if (string.IsNullOrWhiteSpace(name)) throw new KillTeamValidationException("Name is required.");
```

---

## 3. Blank line between using block, namespace, and class

```csharp
// ✅ Correct
using System;
using System.Collections.Generic;

namespace KillTeam.DataSlate.Console.Commands;

public class ImportKillTeamsCommand
{

// ❌ Wrong — no blank lines
using System;
namespace KillTeam.DataSlate.Console.Commands;
public class ImportKillTeamsCommand
{
```

---

## 4. Blank line between variable declarations and statements

When a method starts with one or more variable declarations, add a blank line before the first statement.

```csharp
// ✅ Correct
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var folder = config["DataSlate:KillTeamFolder"] ?? "../kill-teams/";
    var files = Directory.GetFiles(folder, "*.json");

    foreach (var file in files)
    {
        await ImportFileAsync(file);
    }
}

// ❌ Wrong — no blank line
public async Task<int> ExecuteAsync(CommandContext context, Settings settings)
{
    var folder = config["DataSlate:KillTeamFolder"] ?? "../kill-teams/";
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

**Exception — serialization targets:** When a class is used as a `System.Text.Json` deserialization target (e.g. internal `JsonKillTeam` DTOs), `required` may be omitted if it would require `[JsonConstructor]` / `SetsRequiredMembers` boilerplate. Add a brief comment:

```csharp
// System.Text.Json deserialization target — required omitted intentionally
public string? Name { get; set; }
```

---

*More conventions will be added here as the project evolves.*
