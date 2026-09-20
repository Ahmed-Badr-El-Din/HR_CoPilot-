# Issue 001 — Replace scaffold with product skeleton

- **Type:** chore · **Milestone:** M0 (repo setup) · **Status:** open
- **Scope:** Replace the `weatherforecast` template Program.cs; establish project layout,
  `Directory.Build.props`, editorconfig; define ports and entities placeholders.
- **Why:** The repo starts as a story; a weather endpoint is a wrong first sentence.
- **Acceptance:** `dotnet build -warnaserror` green; no template code remains.