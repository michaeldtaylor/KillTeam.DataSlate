# AI Review — Prompt Template

This template is used by the **spec** skill's AI Review stage. A sub-agent uses this prompt to perform
a thorough review of a spec file, then presents the results via the **playground** skill as an
interactive HTML review document.

---

## AI Review Prompt

Load the **playground** skill, then generate a comprehensive spec review as a self-contained interactive
HTML page, identifying gaps, inconsistencies, unclear acceptance criteria, missing technical context,
and areas that need refinement.

Follow the playground skill workflow using the `document-critique` template as the base format.

**Inputs:**
- Spec file: `$1` (path to the spec.md to review — typically `wiki/specs/<feature>/spec.md`)
- Codebase root: `$2` if provided, otherwise the current working directory

---

## Data Gathering Phase — read these before generating

1. **Read the spec file in full.** Extract:
   - The problem statement and goals
   - Every user story and its acceptance criteria
   - Functional requirements (FR-X)
   - Non-goals
   - Open questions
   - Technical considerations

2. **Cross-reference claims against the codebase.** For each technical claim in the spec, verify:
   - Does the referenced class/function/component actually exist?
   - Does the description of current behaviour match what the code actually does?
   - Are referenced patterns (e.g. "follow X as reference implementation") actually present and similar enough to be useful?
   - Are there implicit assumptions about code structure that may not hold?

3. **Read the research file** `wiki/specs/<feature>/research.md` if it exists.
   - Is there a date on the research? Is it stale (>30 days)?
   - Do the research findings support the spec's assumptions?

4. **Read the design pack** `wiki/specs/<feature>/design.md` if it exists.
   - Are design decisions referenced from the relevant user stories?
   - Do Design IDs (DES-XXX) appear in the spec?

5. **Identify blast radius.** From the codebase, identify:
   - What other features or commands depend on the areas being changed?
   - Are there auth fixtures, snapshot tests, or OpenAPI specs that would need updating?
   - Are there any cross-service API contracts that need versioning considerations?

---

## Verification Checkpoint

Before generating HTML, produce a structured fact sheet covering:
- Every claim about existing code (file path, symbol name, or behaviour) — cite source or mark as UNVERIFIED
- Every acceptance criterion that is ambiguous or unverifiable — flag as NEEDS CLARIFICATION
- Every user story that exceeds ~8 acceptance criteria — flag as POSSIBLY TOO LARGE
- Every functional requirement that is vague or unmeasurable — flag as VAGUE

This fact sheet is your source of truth. Do not deviate from it in the HTML output.

---

## Review Structure — sections to include in the HTML output

The playground document-critique template should be used. Organise content under these review dimensions:

### 1. Spec Summary
One-paragraph plain-English summary of what this feature does and why. A reader who has not read the spec should understand it after reading this. Flag if the introduction/overview section is unclear or missing.

### 2. Coverage Assessment
A visual dashboard showing:
- User stories: count + estimated size (small/medium/large based on acceptance criteria count)
- Backend vs. frontend split
- Test coverage specified: yes/partial/no
- Auth considerations: yes/no
- Design pack linked: yes/no
- Research file present: yes/no (and staleness if present)
- Open questions: count + severity (blocking/informational)

### 3. Story-by-Story Review
For each user story, a card showing:
- **Title and description**
- **Acceptance criteria completeness:** Are criteria verifiable? Flag vague ones.
- **Technical considerations:** Are they specific enough? Flag missing file paths, missing pattern references, ambiguous terms.
- **Quality gates:** Are they present? Are they scoped correctly (filtered, not full suite)?
- **Issues found:** Numbered list of specific problems (use 🔴 critical / 🟡 needs attention / 🟢 fine)

### 4. Functional Requirements Review
- Are all FRs traceable to at least one user story?
- Are there user stories with no corresponding FR?
- Are any FRs vague, unmeasurable, or internally contradictory?
- Are there obvious FRs missing based on the goals?

### 5. Non-Goals & Scope
- Are the non-goals specific enough, or could they be interpreted ambiguously?
- Are there obvious scope creep risks not listed as non-goals?
- Is there anything in the user stories that seems to contradict a listed non-goal?

### 6. Technical & Security Gaps
- Authorisation: Is every operation covered by appropriate permission/auth tests?
- Data model: Are any schema changes or migrations missing from the spec?
- Cross-service: Are any service interfaces or external integrations mentioned but not detailed?
- Error cases: Are failure paths (not-found, unauthorized, invalid state, race conditions) covered in acceptance criteria?

### 7. Diagrams & Clarity
- Are there complex flows that would benefit from a Mermaid diagram but currently have none?
- Are existing diagrams consistent with the written description?

### 8. Recommended Actions
A prioritised list of suggested changes, grouped by:
- 🔴 **Must fix before implementation** — critical gaps, ambiguous requirements, missing auth/test coverage
- 🟡 **Should address** — improvements that will prevent issues during implementation
- 🟢 **Nice to have** — optional enhancements for clarity

---

## Output Requirements

1. **Use the playground skill** to generate a self-contained HTML document using the `document-critique` template.
2. The document MUST be served locally so the user can view it in their browser.
3. The document should be visually clear, with colour-coded severity indicators (🔴🟡🟢) and collapsible sections for each user story.
4. Present the playground link to the user and ask: *"Here is the AI review of the spec. Please review it and provide any feedback, or say 'no changes' to proceed to the next step."*
5. After the user responds:
   - **Feedback given:** Update `spec.md` accordingly, then re-run the AI Review loop.
   - **No changes / looks good:** Return control to the spec orchestrator (Next Steps Check).


