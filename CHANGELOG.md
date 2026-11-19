# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-11-19

First tagged release.

### Added

- MCP server over stdio that decompiles .NET assemblies with dotPeek 2026.2's engine.
- Tools: `load_assembly`, `list_types`, `get_type_members`, `decompile_type`, `decompile_method`, `search_members`, `open_in_dotpeek`.
- Runtime selection from the assembly's references (.NET Framework or modern .NET).
- `DOTPEEK_MCP_ALLOWED_DIRS` path allowlist and `DOTPEEK_MCP_MAX_CHARS` response limit.
- `install.ps1`: builds the server from a tagged release and registers it in Claude Code with one command.
- MIT license, contributing guide, issue templates, and this changelog.
- CI: secret scanning, `install.ps1` lint, and Markdown link checks.
- README translations: Spanish, Brazilian Portuguese, and Simplified Chinese.

### Fixed

- `list_types` and `search_members` no longer list nested types twice.

[Unreleased]: https://github.com/matiasdante/dotpeek-mcp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/matiasdante/dotpeek-mcp/releases/tag/v0.1.0
