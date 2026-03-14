# Spike: US-009 — AI Combat Advisor

**Author:** Spike research  
**Date:** 2026-03-14  
**Status:** Complete — ready for implementation planning  
**Related:** spec.md US-009, spike-simulate-command.md (US-008), CombatResolutionService.cs

---

## 1. Overview

This spike investigates the design of an AI advisor layer that uses Claude (via Anthropic's C# SDK) to explain dice results and suggest optimal plays during simulation. The advisor is accessible from within the `simulate` command (US-008) and optionally from a live game (`play` command / US-001).

The advisor is built on `IChatClient` from `Microsoft.Extensions.AI.Abstractions` so it is not hard-coupled to Anthropic — any compliant implementation (Azure OpenAI, Ollama, etc.) could be swapped in via DI configuration.

---

## 2. NuGet Package Decisions

| Package | Version | Reason |
|---|---|---|
| `Anthropic` | Latest stable | Official C# SDK; implements `IChatClient` via `.AsIChatClient(modelId)` |
| `Microsoft.Extensions.AI.Abstractions` | Latest stable | Provides `IChatClient`, `ChatMessage`, `ChatRole` — the abstraction layer |

**Installation:**
```xml
<!-- KillTeam.DataSlate.Console.csproj -->
<PackageReference Include="Anthropic" Version="0.*" />
<PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="9.*" />
```

**Configuration:**
```json
// appsettings.json
{
  "DataSlate": {
    "DatabasePath": "./data/killteam.db",
    "RosterFolder": "./rosters/"
  },
  "Anthropic": {
    "ApiKey": ""
  }
}
```

API key resolution order (first non-empty value wins):
1. `ANTHROPIC_API_KEY` environment variable
2. `Anthropic:ApiKey` in `appsettings.json`

---

## 3. Decision Log

### Decision 1 — Trigger Model

**Question:** Should the AI advisor speak automatically after each dice resolution, or be user-triggered?

**Decision:** **User-triggered** — a `"? Ask AI Advisor"` option injected into the existing `SelectionPrompt` at key decision points.

**Rationale:**
- Automatic advice after every dice pool would be noisy and slow. In a fast simulate loop (10 dice pools) the user would be waiting for 10 API calls they didn't ask for.
- Players who are learning will use the advisor heavily; experienced players will ignore it entirely. User-triggered respects both audiences.
- A spinner / progress indicator is shown while waiting for the API response — the user explicitly accepted the latency by choosing "?".
- In `FightSessionOrchestrator`, the advisor option appears in the action menu between rounds: `"⚔ Strike with die(6,CRIT)"`, `"🛡 Block die..."`, **`"? Ask AI Advisor"`**. This keeps the advisor at the same level as game decisions.
- In `ShootSessionOrchestrator`, the advisor option appears after the result table is displayed, as a follow-up prompt: `"Understand this result? [Y/N]"` where Y triggers the advisor.

**Alternative considered — automatic mode:** Could be added as a `--ai-auto` flag in a future story. The advisor architecture supports this: `ExplainShootResultAsync(...)` can be called either in response to user input or automatically.

---

### Decision 2 — Context Provided to the AI

**Question:** What context does the AI advisor receive? Full fight history vs. current exchange only?

**Decision:** **Current exchange context only**, plus both operatives' full stats and the weapon being used.

**Full context bundle per call:**
- Attacker: name, wounds (current/max), save threshold, APL, weapon name, weapon stats (ATK, Hit+, DMG), all special rules
- Defender: same
- Current dice pools (for fight) or resolved shoot result (for shoot)
- In-cover / obscured state (for shoot)
- Active special rules on the weapon (e.g. "Brutal is active — defender cannot Block with normal dice")
- For fight: available actions at the current decision point
- The question type: "explain this result" vs. "suggest next action"

**What is NOT sent:**
- Full game history (previous activations, previous turning points)
- Other operatives not involved in this exchange
- VP scores, CP totals, ploy history

**Rationale:**
- Full history would bloat token usage significantly. The advisor's value is in the current mechanical decision — not strategic game memory.
- Both operatives' stats are always sent: the advisor needs to know save thresholds, wound totals, and weapon damage to give meaningful advice about whether to Strike or Block.
- "Available actions" context is critical for fight advisor: the advisor should know whether a Block is even possible before suggesting one.
- Context is serialised to a structured text block, not JSON — natural language is more reliable for Claude to reason about.

---

### Decision 3 — Tool Calling vs Post-Hoc Explanation

**Question:** Should the AI have function/tool access to call `CombatResolutionService` to simulate alternative plays, or only receive state and explain it?

**Decision:** **Post-hoc explanation only** — no tool calling in the initial implementation.

**Rationale:**
- Tool calling adds significant implementation complexity: requires defining tool schemas, handling tool call / tool result turn cycles, and ensuring the AI invokes tools correctly.
- The primary use case is players learning the game. "Explain what just happened and why" is more valuable for learners than "simulate 5 alternative dice configurations."
- `CombatResolutionService` is a pure function (no side effects, no DB) — it could theoretically be wrapped as a tool. This is an excellent **future enhancement** once the basic advisor is proven.
- Post-hoc explanation covers 90% of the value proposition: "Why did my 3 attack dice only deal 2 damage? Why was it better to Block than Strike there?"

**Future enhancement:** Add a `--tools` flag to `simulate` that enables tool-calling mode, allowing the advisor to call `ResolveShoot(...)` internally to compare "what if I had used Rending instead?"

---

### Decision 4 — Output Format

**Question:** Plain text, Spectre.Console markup, or a Panel?

**Decision:** **Spectre.Console `Panel`** with a distinct `[bold cyan]🤖 AI Advisor[/]` header and word-wrapped body.

**Rationale:**
- A `Panel` visually separates advisor output from game flow, making it clear this is optional supplementary content rather than a game state change.
- The `[bold cyan]` colour scheme distinguishes the advisor from game output (which uses `[red]`, `[yellow]`, `[green]`) while remaining accessible.
- Word-wrapping is important: Claude's responses may be 2-5 sentences. Spectre.Console's Panel handles this automatically.
- Spectre.Console markup in the AI response text should be escaped (`Markup.Escape(advisorText)`) since Claude's output is untrusted.

**Visual example:**
```
╭─────────────────────────────────╮
│ 🤖 AI Advisor                   │
│                                  │
│ Your Bolt rifle rolled 3 crits   │
│ but the defender's 3+ save       │
│ absorbed 2 of them. Blocking     │
│ with normal dice against crits   │
│ costs 2 normals per crit — here  │
│ it was the defender's only        │
│ option. Consider Rending or      │
│ Piercing Crits to punish high-   │
│ save targets.                    │
╰─────────────────────────────────╯
```

---

### Decision 5 — Fallback When No API Key

**Question:** Graceful disable or error?

**Decision:** **Graceful disable** — the `"? Ask AI Advisor"` option is simply not shown when `IAiAdvisor.IsAvailable` is false. No error, no noise.

**Rationale:**
- The advisor is optional. The game (both simulate and live play) must work perfectly without it.
- Showing an error every time the "?" option would have appeared would be annoying for players who haven't set up an API key.
- A single informational message at startup ("AI Advisor not configured — set ANTHROPIC_API_KEY to enable") is the extent of the notification. This fires once on `dataslate simulate` launch if `IAiAdvisor.IsAvailable == false`.
- `NullAiAdvisor` (null-object pattern) implements `IAiAdvisor` with all methods returning `null` and `IsAvailable = false`. The orchestrators check `advisor.IsAvailable` before injecting the "?" menu option.

---

## 4. `IAiAdvisor` Interface

```csharp
namespace KillTeam.DataSlate.Domain.Services;

/// <summary>
/// Provides AI-powered tactical advice and explanation during combat simulation.
/// Implementations may be ephemeral (API call per invocation) or stateful.
/// </summary>
public interface IAiAdvisor
{
    /// <summary>
    /// True if the advisor is configured and available. When false,
    /// the "? Ask AI Advisor" option should not be shown.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Explains the outcome of a completed shoot action in natural language.
    /// Returns null if the advisor is unavailable or the call fails.
    /// </summary>
    Task<string?> ExplainShootResultAsync(
        ShootAdvisorContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Suggests the optimal next action in an ongoing fight exchange,
    /// given the current dice pools and available actions.
    /// Returns null if the advisor is unavailable or the call fails.
    /// </summary>
    Task<string?> SuggestFightActionAsync(
        FightAdvisorContext ctx,
        CancellationToken ct = default);

    /// <summary>
    /// Summarises the outcome of a completed fight exchange.
    /// Returns null if the advisor is unavailable or the call fails.
    /// </summary>
    Task<string?> ExplainFightResultAsync(
        FightResultAdvisorContext ctx,
        CancellationToken ct = default);
}
```

### 4.1 Context Record Types

```csharp
// Placed in KillTeam.DataSlate.Domain.Models or a new Models\Advisor\ subfolder

public record ShootAdvisorContext(
    string AttackerName,
    int AttackerWounds,
    int AttackerMaxWounds,
    string WeaponName,
    int WeaponAtk,
    int WeaponHit,
    int WeaponNormalDmg,
    int WeaponCritDmg,
    IReadOnlyList<string> WeaponRules,   // raw rule strings, e.g. ["Piercing 1", "Rending"]
    string DefenderName,
    int DefenderWounds,
    int DefenderMaxWounds,
    int DefenderSave,
    bool InCover,
    bool IsObscured,
    int[] AttackDice,
    int[] DefenceDice,
    ShootResult Result);

public record FightAdvisorContext(
    string AttackerName,
    int AttackerCurrentWounds,
    int AttackerMaxWounds,
    string AttackerWeaponName,
    IReadOnlyList<string> AttackerWeaponRules,
    string DefenderName,
    int DefenderCurrentWounds,
    int DefenderMaxWounds,
    string? DefenderWeaponName,
    IReadOnlyList<FightDie> AttackerRemainingDice,
    IReadOnlyList<FightDie> DefenderRemainingDice,
    IReadOnlyList<FightAction> AvailableActions,
    bool BrutalActive);

public record FightResultAdvisorContext(
    string AttackerName,
    int AttackerDamageDealt,
    bool AttackerCausedIncapacitation,
    string DefenderName,
    int DefenderDamageDealt,
    bool DefenderCausedIncapacitation,
    string AttackerWeaponName,
    IReadOnlyList<string> AttackerWeaponRules);
```

---

## 5. `AnthropicAdvisor` Implementation Sketch

```csharp
namespace KillTeam.DataSlate.Console.Services;

public class AnthropicAdvisor(IChatClient chatClient, IAnsiConsole console) : IAiAdvisor
{
    private const string SystemPrompt = """
        You are a Kill Team 2024 (KT24 V3.0) tactical advisor embedded in a CLI game-tracking tool.
        Your role is to explain dice results and suggest optimal plays in plain, direct English.

        Rules context:
        - Kill Team is a sci-fi skirmish game where operatives fight in alternating activations.
        - Shoot actions: attacker rolls ATK dice (hit on Hit+ threshold, crit on 6+); defender rolls save dice (save on Save+ threshold, crit save on 6+).
          Blocking: 1 crit save cancels 1 crit attack; 2 normal saves cancel 1 crit attack; 1 normal save cancels 1 normal attack.
          Normal hits deal NormalDmg; crits deal CritDmg (overridden by Devastating X).
        - Fight actions: alternating Strike/Block. Strike: spend 1 die, deal damage. Block: spend 1 die, cancel opponent die.
          Normal die can only block a normal opponent die. Crit die can block any opponent die.
          Brutal: opponent cannot block with normal dice. Shock: first crit strike discards opponent's lowest success.
        - Weapon special rules: Rending (hit→crit if any crits), Punishing (all hits→crits if any crits), Severe (hit→crit if no crits),
          Piercing X (remove X defence dice), Accurate X (add X free normal hits), Lethal X (X+ counts as crit),
          Devastating X (crits deal X damage), Stun (crit → target loses 1 APL), Hot (post-shoot self-damage check).
        - Injured: operative below half starting wounds → +1 to hit threshold, -2" Move.

        Response style:
        - Be concise: 2-4 sentences maximum.
        - Lead with the key insight or recommendation.
        - Explain the rule mechanic that determined the outcome.
        - If suggesting a fight action, name the specific action and why it is optimal.
        - Do not repeat the dice values back verbatim — summarise the outcome instead.
        - Do not use markdown formatting — plain text only (no **, no #, no bullet lists).
        """;

    public bool IsAvailable => true;

    public async Task<string?> ExplainShootResultAsync(
        ShootAdvisorContext ctx, CancellationToken ct = default)
    {
        var prompt = BuildShootPrompt(ctx);
        return await CallAsync(prompt, ct);
    }

    public async Task<string?> SuggestFightActionAsync(
        FightAdvisorContext ctx, CancellationToken ct = default)
    {
        var prompt = BuildFightActionPrompt(ctx);
        return await CallAsync(prompt, ct);
    }

    public async Task<string?> ExplainFightResultAsync(
        FightResultAdvisorContext ctx, CancellationToken ct = default)
    {
        var prompt = BuildFightResultPrompt(ctx);
        return await CallAsync(prompt, ct);
    }

    private async Task<string?> CallAsync(string userPrompt, CancellationToken ct)
    {
        try
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            return response.Text?.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Graceful degradation: advisor failure should never crash the game
            console.MarkupLine($"[dim yellow]AI Advisor unavailable: {Markup.Escape(ex.Message)}[/]");
            return null;
        }
    }

    private static string BuildShootPrompt(ShootAdvisorContext ctx)
    {
        var rules = ctx.WeaponRules.Count > 0
            ? string.Join(", ", ctx.WeaponRules)
            : "none";

        var coverNote = ctx.InCover ? "in cover (+1 normal save)" : ctx.IsObscured ? "obscured (crits→normals, -1 normal)" : "no cover";

        return $"""
            Shoot action just resolved. Explain the outcome and what the attacker could do differently next time.

            Attacker: {ctx.AttackerName} ({ctx.AttackerWounds}/{ctx.AttackerMaxWounds} W)
            Weapon: {ctx.WeaponName} — ATK {ctx.WeaponAtk}, Hit {ctx.WeaponHit}+, DMG {ctx.WeaponNormalDmg}/{ctx.WeaponCritDmg}, rules: {rules}
            Attack dice rolled: [{string.Join(", ", ctx.AttackDice)}]

            Defender: {ctx.DefenderName} ({ctx.DefenderWounds}/{ctx.DefenderMaxWounds} W), Save {ctx.DefenderSave}+, {coverNote}
            Defence dice rolled: [{string.Join(", ", ctx.DefenceDice)}]

            Result: {ctx.Result.UnblockedCrits} unblocked crits, {ctx.Result.UnblockedNormals} unblocked normals, {ctx.Result.TotalDamage} total damage.
            {(ctx.Result.StunApplied ? "Stun applied (-1 APL to defender)." : "")}
            {(ctx.Result.SelfDamageDealt > 0 ? $"Hot: {ctx.Result.SelfDamageDealt} self-damage to attacker." : "")}
            """;
    }

    private static string BuildFightActionPrompt(FightAdvisorContext ctx)
    {
        var atkDice = string.Join(", ", ctx.AttackerRemainingDice.Select(d => $"{d.RolledValue}({d.Result})"));
        var defDice = string.Join(", ", ctx.DefenderRemainingDice.Select(d => $"{d.RolledValue}({d.Result})"));
        var actions = string.Join("; ", ctx.AvailableActions.Select(a =>
            a.Type == FightActionType.Strike
                ? $"Strike with {a.ActiveDie.RolledValue}({a.ActiveDie.Result})"
                : $"Block {a.ActiveDie.RolledValue}({a.ActiveDie.Result}) → cancel {a.TargetDie!.RolledValue}({a.TargetDie.Result})"));
        var brutal = ctx.BrutalActive ? " (Brutal active: defender cannot Block with normal dice)" : "";
        var atkRules = ctx.AttackerWeaponRules.Count > 0 ? string.Join(", ", ctx.AttackerWeaponRules) : "none";
        var defWpn = ctx.DefenderWeaponName ?? "no melee weapon";

        return $"""
            It is {ctx.AttackerName}'s turn to act in a fight exchange. Suggest the optimal action.

            {ctx.AttackerName} ({ctx.AttackerCurrentWounds}/{ctx.AttackerMaxWounds} W) using {ctx.AttackerWeaponName} (rules: {atkRules}){brutal}
            Remaining attacker dice: [{atkDice}]

            {ctx.DefenderName} ({ctx.DefenderCurrentWounds}/{ctx.DefenderMaxWounds} W) using {defWpn}
            Remaining defender dice: [{defDice}]

            Available actions: {actions}

            Which action should {ctx.AttackerName} take, and why?
            """;
    }

    private static string BuildFightResultPrompt(FightResultAdvisorContext ctx)
    {
        var rules = ctx.AttackerWeaponRules.Count > 0 ? string.Join(", ", ctx.AttackerWeaponRules) : "none";
        var incapNote = ctx.AttackerCausedIncapacitation
            ? $"{ctx.DefenderName} was incapacitated."
            : ctx.DefenderCausedIncapacitation
                ? $"{ctx.AttackerName} was incapacitated."
                : "Neither operative was incapacitated.";

        return $"""
            A fight exchange just completed. Summarise what happened and offer tactical insight.

            {ctx.AttackerName} attacked with {ctx.AttackerWeaponName} (rules: {rules}).
            {ctx.AttackerName} dealt {ctx.AttackerDamageDealt} damage.
            {ctx.DefenderName} dealt {ctx.DefenderDamageDealt} damage.
            {incapNote}

            Briefly summarise the exchange and suggest anything the losing side could have done differently.
            """;
    }
}
```

---

## 6. `NullAiAdvisor` (Graceful Degradation)

```csharp
namespace KillTeam.DataSlate.Console.Services;

/// <summary>
/// No-op advisor used when no API key is configured.
/// All methods return null; IsAvailable = false suppresses the "?" menu option.
/// </summary>
public class NullAiAdvisor : IAiAdvisor
{
    public bool IsAvailable => false;

    public Task<string?> ExplainShootResultAsync(ShootAdvisorContext ctx, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> SuggestFightActionAsync(FightAdvisorContext ctx, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    public Task<string?> ExplainFightResultAsync(FightResultAdvisorContext ctx, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
```

---

## 7. System Prompt Design

The system prompt (inlined in `AnthropicAdvisor` above) is designed to:

1. **Establish the role clearly**: embedded CLI tool advisor, not a general chatbot. This constrains Claude to relevant output.
2. **Encode the most frequently-misunderstood rules**: the blocking algorithm (2 normals → 1 crit), Brutal constraint, fight die type semantics. These are the rules players ask about most.
3. **Enforce conciseness**: explicit "2-4 sentences maximum" instruction. Verbose responses break the CLI UX.
4. **Forbid markdown**: Claude defaults to markdown in most contexts; plaintext is required for Spectre.Console safety.
5. **Instruct on response framing**: lead with the key insight, explain the rule, name specific actions — keeps responses actionable.

The system prompt is defined as a `const string` in `AnthropicAdvisor` rather than loaded from a file, to keep the implementation self-contained and avoid runtime file dependencies.

**Token budget estimate per call:**
- System prompt: ~350 tokens
- Shoot context prompt: ~120 tokens
- Fight action prompt: ~150 tokens
- Response: ~80-120 tokens
- **Total per call: ~600-650 tokens** (~$0.0006 at claude-sonnet-4-5 pricing)

---

## 8. DI Registration

```csharp
// Program.cs — add after existing service registrations

var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? config["Anthropic:ApiKey"]
    ?? string.Empty;

if (string.IsNullOrWhiteSpace(anthropicApiKey))
{
    services.AddSingleton<IAiAdvisor, NullAiAdvisor>();
}
else
{
    services.AddSingleton<IAiAdvisor>(sp =>
    {
        var client = new AnthropicClient(anthropicApiKey).AsIChatClient("claude-sonnet-4-5");
        var console = sp.GetRequiredService<IAnsiConsole>();
        return new AnthropicAdvisor(client, console);
    });
}
```

**appsettings.json — add Anthropic section:**
```json
{
  "DataSlate": {
    "DatabasePath": "./data/killteam.db",
    "RosterFolder": "./rosters/"
  },
  "Anthropic": {
    "ApiKey": ""
  }
}
```

The API key is intentionally left empty in the committed `appsettings.json`. Users set it via environment variable in practice. The `appsettings.json` entry exists as a documented config hook and for local development convenience.

---

## 9. Integration Points with Simulate (US-008)

The advisor slots into `SimulateShootOrchestrator` and `SimulateFightOrchestrator` as an optional injected dependency:

### Shoot Integration

After `DisplayShootResult(...)` outputs the result table, insert:
```csharp
if (aiAdvisor.IsAvailable)
{
    var ask = console.Prompt(
        new SelectionPrompt<string>()
            .Title("[dim]Want an explanation?[/]")
            .AddChoices("Continue", "? Ask AI Advisor"));

    if (ask == "? Ask AI Advisor")
    {
        console.Status().Start("Consulting advisor...", _ =>
        {
            var ctx = new ShootAdvisorContext(...);
            var advice = aiAdvisor.ExplainShootResultAsync(ctx).GetAwaiter().GetResult();
            if (advice is not null)
            {
                var panel = new Panel(Markup.Escape(advice))
                    .Header("[bold cyan]🤖 AI Advisor[/]")
                    .BorderColor(Color.Cyan1);
                console.Write(panel);
            }
        });
    }
}
```

### Fight Integration

The `"? Ask AI Advisor"` option is added to the `SelectionPrompt<FightAction>` in the fight loop, using a wrapper approach:

```csharp
// In the fight loop, build the action list
var menuItems = new List<string>(actions.Select(FormatFightAction));
if (aiAdvisor.IsAvailable)
    menuItems.Add("? Ask AI Advisor");

var choice = console.Prompt(
    new SelectionPrompt<string>()
        .Title($"[bold]{Markup.Escape(activeOp.Name)}[/] — select an action:")
        .AddChoices(menuItems));

if (choice == "? Ask AI Advisor")
{
    // Build context, call advisor, display panel, then re-show the menu
    continue; // re-loop without changing currentOwner
}
```

### Live Game Integration (play command)

The advisor can be optionally injected into the existing `ShootSessionOrchestrator` and `FightSessionOrchestrator` via a constructor parameter:

```csharp
public class ShootSessionOrchestrator(
    IAnsiConsole console,
    CombatResolutionService combatResolutionService,
    RerollOrchestrator rerollOrchestrator,
    BlastTorrentSessionOrchestrator blastTorrentOrchestrator,
    IGameOperativeStateRepository stateRepository,
    IActionRepository actionRepository,
    IAiAdvisor aiAdvisor)           // ← new optional dependency
```

Since `NullAiAdvisor` is always registered (even without an API key), this parameter is always satisfied — no conditional registration needed. When `aiAdvisor.IsAvailable == false`, no UI change occurs.

---

## 10. Technical Considerations

### 10.1 Async in Spectre.Console Context

Spectre.Console's interactive prompts are synchronous (they block on `console.Prompt(...)`). The AI call is async. The integration pattern (`.GetAwaiter().GetResult()` inside a `console.Status().Start(...)` block) avoids deadlocks because `Status` runs synchronously on the calling thread without configuring an `async`-safe context. Alternatively, the orchestrators can be made fully async with `await`:

```csharp
console.Status().Spinner(Spinner.Known.Dots).Start("Consulting AI Advisor...", ctx =>
{
    // This lambda is synchronous — use .GetAwaiter().GetResult() here
    advice = aiAdvisor.ExplainShootResultAsync(advisorCtx).GetAwaiter().GetResult();
});
```

The existing orchestrators are already `async Task` methods, so the outer method can `await` the advisor call directly if the `Status` wrapper is not needed.

### 10.2 Error Handling

`AnthropicAdvisor.CallAsync(...)` wraps the API call in a `try/catch` that catches all non-cancellation exceptions and returns `null`. The caller treats `null` as "no advice available" and skips the panel. This ensures:
- Network failures do not crash the game
- Rate limit errors (429) do not crash the game
- Invalid API key errors surface once as `[dim yellow]AI Advisor unavailable: ...[/]` and then silently degrade

### 10.3 Cancellation

The `CancellationToken` parameter on all advisor methods supports Ctrl+C interruption. The caller should pass a token linked to the application lifetime. In practice, the CLI command handler does not currently use cancellation tokens — passing `CancellationToken.None` is acceptable for the initial implementation.

### 10.4 Model Selection

`"claude-sonnet-4-5"` is specified at DI registration time. The model string should be exposed as a config key:
```json
"Anthropic": {
  "ApiKey": "",
  "Model": "claude-sonnet-4-5"
}
```

This allows downgrading to `claude-haiku-4-5` for lower latency / cost without code changes.

### 10.5 `IAiAdvisor` Location

`IAiAdvisor` and its context record types (`ShootAdvisorContext`, `FightAdvisorContext`, `FightResultAdvisorContext`) are defined in `KillTeam.DataSlate.Domain.Services` — same as `CombatResolutionService` and `FightResolutionService`. This keeps the domain layer free of infrastructure dependencies while allowing the interface to reference domain model types (`ShootResult`, `FightDie`, `FightAction`).

`AnthropicAdvisor` and `NullAiAdvisor` are defined in `KillTeam.DataSlate.Console.Services` — they depend on the Anthropic NuGet package, which should not be referenced from the Domain project.

---

## 11. Acceptance Criteria (BDD Style)

**AC-009-01: Advisor available with API key**
```
Given ANTHROPIC_API_KEY is set in the environment
When `dataslate simulate` starts
Then IAiAdvisor resolves to AnthropicAdvisor
And IsAvailable == true
And the "? Ask AI Advisor" option appears in the fight action menu
And the "Want an explanation?" prompt appears after a shoot result
```

**AC-009-02: Advisor disabled without API key**
```
Given ANTHROPIC_API_KEY is not set
And Anthropic:ApiKey in appsettings.json is empty
When `dataslate simulate` starts
Then IAiAdvisor resolves to NullAiAdvisor
And IsAvailable == false
And the "? Ask AI Advisor" option is NOT shown anywhere in the simulate flow
And a single startup message "[dim]AI Advisor not configured — set ANTHROPIC_API_KEY to enable.[/]" is shown
And the simulate command runs normally in all other respects
```

**AC-009-03: Shoot result explanation**
```
Given the advisor is available
And a shoot action has resolved (showing the result table)
When the user selects "? Ask AI Advisor"
Then a loading spinner "[dim]Consulting advisor...[/]" is shown
And a Spectre.Console Panel with header "🤖 AI Advisor" appears
And the panel contains a 2-4 sentence explanation of why the result occurred
And the explanation references the specific rule mechanics involved
```

**AC-009-04: Fight action suggestion**
```
Given the advisor is available
And it is the attacker's turn in a fight exchange
When the user selects "? Ask AI Advisor" from the action menu
Then a Spectre.Console Panel with header "🤖 AI Advisor" appears
And the panel suggests a specific action (Strike or Block) with a reason
And after the panel is shown, the action menu is re-displayed (the loop does not advance)
```

**AC-009-05: Advisor failure graceful degradation**
```
Given the advisor is available (API key set)
And the Anthropic API returns an error (rate limit, network timeout, etc.)
When the user selects "? Ask AI Advisor"
Then the error message "[dim yellow]AI Advisor unavailable: <reason>[/]" is shown
And the game / simulate loop continues normally
And no exception is thrown or propagated to the command handler
```

**AC-009-06: AI output escaping**
```
Given the advisor returns a response containing Spectre.Console markup characters (e.g. "[bold]")
When the panel is rendered
Then the markup characters are escaped (displayed as literal text, not interpreted)
And no Spectre.Console MarkupException is thrown
```

**AC-009-07: Live game integration (play command)**
```
Given the advisor is available
And a live game (`play <game-id>`) is in progress
And a shoot action has resolved
When the "? Ask AI Advisor" prompt is displayed
And the user selects it
Then the advisor explains the result with the same Panel format as in simulate mode
And the game state is not modified
And no data is written to SQLite as a result of the advisor interaction
```

**AC-009-08: Model configuration**
```
Given Anthropic:Model is set to "claude-haiku-4-5" in appsettings.json
When the AnthropicAdvisor is constructed
Then it uses "claude-haiku-4-5" as the model ID
And calls to SuggestFightActionAsync use that model
```

---

## 12. Open Questions

1. **Should the advisor remember the conversation across multiple "? Ask AI Advisor" calls within a single simulate session?** Currently each call is independent (new system+user message). Conversation history could provide more contextual advice ("As I mentioned earlier, Rending would have helped here..."). Recommendation: stateless for the initial implementation; add optional conversation history as a follow-up.

2. **Should the advisor's responses be logged anywhere?** For debugging prompt quality and model output, it may be useful to log advisor inputs/outputs to a file (disabled by default). Recommendation: add `DataSlate:AiAdvisorLogPath` config key that writes JSONL when set.

3. **Should `IAiAdvisor` be exposed from the Domain project or the Console project?** Current recommendation: Domain (interface + context records), Console (implementations). This supports future unit-testing with a mock advisor without taking a dependency on `Anthropic` NuGet in tests.

4. **Rate limiting**: the Anthropic SDK may throw on rate limits. Should the advisor back off and retry, or immediately degrade? Recommendation: immediate degradation in the initial implementation — retries add complexity and the user can simply press "?" again.

5. **Should the AI be available during `BlastTorrent` multi-target flows?** The multi-target flow is complex; the context bundle would need to include all target wounds and defence dice results. Recommendation: defer until `BlastTorrent` simulate support is added (open question from spike-simulate-command.md).

6. **Privacy**: the attacker/defender operative names (e.g. "Intercessor Sergeant") and stats are sent to Anthropic's API. These are game data, not personal data, so GDPR is not a concern. However, custom operative names entered by users could theoretically contain PII. Recommend documenting this in the README.
