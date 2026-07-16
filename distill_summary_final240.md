**Distill Pass Summary**

## Shortlist

| Candidate | Evidence | Frequency / Confidence | Recommended Form | Why |
|-----------|----------|------------------------|------------------|-----|
| Automatic memory consolidation pass (dream) | Duplicate exact user command: "Run one automatic dream memory consolidation pass for the current project." appears twice across sessions. | 2 occurrences, high confidence | Command | Clear, repeatable prompt with stable inputs and stopping condition. |
| Automatic distill pass | User command: "Run one automatic distill pass for the current project." appears once, but is clearly likely to recur as part of maintenance. | 1 occurrence, high confidence (likely to recur) | Command | Structured workflow that benefits from consistent execution. |
| Code audit for bugs/vulnerabilities | Single user request: "阅读代码，检查是否存在bug或漏洞" (review code for bugs/vulnerabilities). No repeated similar requests found. | 1 occurrence, low confidence | Skip | Insufficient repeated evidence; may be one-off. |
| Reading memory files (checkpoint.md, notes.md) | Repeated reads of checkpoint and notes files across sessions (11+ reads of global MEMORY.md, 5+ reads of specific checkpoint files). | High frequency, but part of system routine (checkpoint writer) | Skip | Not a manual workflow; it's an internal system pattern. |
| Reading project source files (e.g., MainWindow.xaml.cs) | Repeated reads of the same source files across sessions (4+ reads each). | Moderate frequency, but ad-hoc analysis, not a fixed procedure | Skip | No stable inputs/stopping condition; each session reads files as needed. |

## Created or Extended

1. **Command: `dream.md`**  
   Path: `.mimocode/command/dream.md`  
   Purpose: Executes an automatic memory consolidation pass that consolidates durable, verified information into project memory.

2. **Command: `distill.md`**  
   Path: `.mimocode/command/distill.md`  
   Purpose: Executes an automatic distill pass that reviews recent sessions, identifies repeated manual workflows, and packages high-confidence missing assets.

## Skipped

- **Code audit for bug/vulnerability review**: Only one occurrence; insufficient repeated evidence to package safely.
- **Memory-file reading patterns**: Internal system routine, not a user-driven manual workflow.
- **Source-file reading patterns**: Ad-hoc analysis without a fixed, repeatable procedure.

## Needs More Evidence

- **Code audit skill**: Awaiting a second similar request (e.g., "review code for bugs" or "audit this project") before creating a skill.
- **Other potential commands**: No other repeated exact user commands were found beyond the two packaged.

## Notes

- No existing skills, agents, or commands were found in the project or global config directories, so all assets are new.
- The database contained 19 sessions total, with 4 sessions in the current project. The analysis focused on the last 30 days of trajectory data.
- Temporary Python inspection scripts were created and cleaned up; no modifications were made to the SQLite database.