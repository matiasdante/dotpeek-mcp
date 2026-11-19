# Contributing to dotpeek-mcp

Thanks for helping. Bug reports, fixes, and small focused improvements are all welcome.

## Before you start

- **Questions and ideas** go to [Discussions](https://github.com/matiasdante/dotpeek-mcp/discussions).
- **Bugs and concrete feature requests** go to [Issues](https://github.com/matiasdante/dotpeek-mcp/issues/new/choose). Use the templates; they ask for the dotPeek version and the assembly's target framework, which most bugs depend on.
- New here? Look for issues labeled [`good first issue`](https://github.com/matiasdante/dotpeek-mcp/labels/good%20first%20issue).
- For anything larger than a bug fix, open an issue first so we can agree on the approach before you write the code.

Every new issue and discussion gets a first reply within one day.

## Development setup

You need Windows x64, **dotPeek 2026.2**, the .NET SDK, and Python 3 for the smoke test. The project compiles against your local dotPeek install; no JetBrains binaries are committed or copied to the output.

```powershell
git clone https://github.com/matiasdante/dotpeek-mcp
cd dotpeek-mcp
dotnet build -c Release
python tests/smoke_test.py
```

If dotPeek is not in the default folder, build with `-p:DotPeekHome=<folder>` and set `DOTPEEK_HOME=<folder>` before running the smoke test.

## Project layout

| File            | Responsibility                                                    |
| --------------- | ----------------------------------------------------------------- |
| `Program.cs`    | Finds dotPeek and resolves its DLLs at runtime                    |
| `Server.cs`     | JSON-RPC over stdio: `initialize`, `tools/list`, `tools/call`     |
| `Tools.cs`      | Tool definitions, arguments, and path allowlist                   |
| `Engine.cs`     | Assembly loading and decompilation through JetBrains' engine      |
| `install.ps1`   | One-command installer                                             |
| `tests/`        | Smoke test and the .NET 8 fixture it decompiles                   |

## Pull requests

1. Branch from `main` and keep the change focused on one thing.
2. Run `python tests/smoke_test.py` and make sure it prints `smoke test OK`. If you add or change a tool, extend the smoke test to cover it, including its error path.
3. Update `README.md` when you change tools, arguments, or configuration. The translations in `docs/readme/` can lag behind; note it in the PR if you did not update them.
4. Add a line under `Unreleased` in `CHANGELOG.md`.
5. Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `ci:`, `chore:`). Messages in English or Spanish are both fine.

CI checks the repository for leaked secrets, lints `install.ps1`, and validates links in the Markdown files. It cannot build the server, because GitHub runners do not have dotPeek; the smoke test on your machine is the build check.

## Ground rules

- Never commit JetBrains binaries, decompiled third-party code, credentials, or `.env` files.
- Keep the server read-only: it must never execute code from the assemblies it inspects.
- Be respectful. Disagreement about code is fine; personal attacks are not.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
