# DubliMark Agent Instructions

DubliMark is a C# / .NET 8 / WPF desktop app for DataMatrix and Chestny ZNAK workflows.

## Prime Safety Rule

Never break preservation of GS = `char 0x1D` or the AI 91/92 crypto tail.

## Strictly Forbidden

- Do not read Chestny ZNAK codes only through a normal `TextBox`.
- Do not replace GS with a space, empty string, Enter, Tab, or a visible text marker.
- Do not cut AI 92 by fixed length.
- Do not mass-replace special characters.
- Do not rewrite Raw Input, COM, or GS1 parsing without tests.
- Do not use real Chestny ZNAK codes in tests, logs, docs, memory, or prompts.
- Do not send marking codes, secrets, or production payloads to external services.

## Key Files

Read these before any scanner, parser, print, or template-sensitive change:

- `src/DubliMark.Desktop/Services/RawInputScannerService.cs`
- `src/DubliMark.Desktop/Services/SerialScannerService.cs`
- `src/DubliMark.Core/Parsing/Gs1Parser.cs`
- `src/DubliMark.Core/Parsing/Gs1BarcodeEncoding.cs`
- DataMatrix/PDF generation files under `src/DubliMark.Core/Export/` and `src/DubliMark.Core/Print/`
- Existing tests under `tests/`

## Roles

- Cursor Agent: main code executor. It analyzes, plans, edits, validates, and reports.
- Ruflo: memory, planning, and review assistant when MCP is available.
- UI Reviewer: checks WPF/XAML, `Theme.xaml`, readability, controls, and dark premium style consistency.
- Scanner Safety Reviewer: checks that GS, AI91, AI92, RawInput, and COM behavior were not touched or broken.
- Print Reviewer: checks print/export/templates/settings synchronization.

Agents must not rewrite code in parallel. Cursor Agent performs edits; other roles are review/checklist roles.

## Ruflo Usage

Use Ruflo only for memory, planning, and review when MCP is connected. Do not let Ruflo autonomously rewrite this project. Never store real Chestny ZNAK payloads, secrets, credentials, or customer data in Ruflo memory.

## Workflow

1. Analyze first.
2. Present a plan for non-trivial changes.
3. Make scoped edits only after the plan.
4. Run `dotnet restore`, `dotnet build`, and `dotnet test` after code changes.
5. Provide a short report with changed files, validation, and residual risk.

Do not change business logic unless explicitly requested.

