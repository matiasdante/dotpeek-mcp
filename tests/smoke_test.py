"""End-to-end check of the MCP server over stdio.

Builds the server and a .NET 8 fixture, then drives both a modern assembly (the fixture)
and a .NET Framework one (the server's own exe) through every read-only tool.
Run: python tests/smoke_test.py   (needs dotPeek 2026.2 installed and the .NET SDK)
"""
import json
import os
import pathlib
import subprocess

ROOT = pathlib.Path(__file__).resolve().parent.parent
SERVER = ROOT / "bin" / "Release" / "net472" / "DotPeekMcp.exe"
FIXTURE = ROOT / "tests" / "Fixture" / "bin" / "Release" / "net8.0" / "Fixture.dll"


def build():
    subprocess.run(["dotnet", "build", str(ROOT / "DotPeekMcp.csproj"), "-c", "Release", "-v", "q"], check=True)
    subprocess.run(["dotnet", "build", str(ROOT / "tests" / "Fixture" / "Fixture.csproj"), "-c", "Release", "-v", "q"], check=True)


def session(calls, env=None):
    """Send initialize + tools/call requests in one session; return {id: result}."""
    msgs = [
        {"jsonrpc": "2.0", "id": 0, "method": "initialize", "params": {"protocolVersion": "2025-06-18"}},
        {"jsonrpc": "2.0", "method": "notifications/initialized"},
        {"jsonrpc": "2.0", "id": 1, "method": "tools/list"},
    ]
    for i, (name, args) in enumerate(calls, start=2):
        msgs.append({"jsonrpc": "2.0", "id": i, "method": "tools/call", "params": {"name": name, "arguments": args}})
    out = subprocess.run([str(SERVER)], input="\n".join(json.dumps(m) for m in msgs) + "\n",
                         capture_output=True, text=True, encoding="utf-8", timeout=180, check=True,
                         env={**os.environ, **(env or {})}).stdout
    return {r["id"]: r.get("result", r.get("error")) for r in map(json.loads, out.splitlines())}


def text(result):
    assert result["isError"] is False, result
    return result["content"][0]["text"]


def main():
    build()
    fx, fw = str(FIXTURE), str(SERVER)
    r = session([
        ("load_assembly", {"path": fx}),
        ("list_types", {"path": fx, "filter": "Fixture"}),
        ("get_type_members", {"path": fx, "type": "Fixture.Calculator"}),
        ("decompile_type", {"path": fx, "type": "Fixture.Calculator"}),
        ("decompile_method", {"path": fx, "type": "Fixture.Calculator", "method": "Add"}),
        ("search_members", {"path": fx, "query": "greet"}),
        ("decompile_type", {"path": fw, "type": "DotPeekMcp.Program"}),
        ("decompile_type", {"path": fx, "type": "Nope.Missing"}),
        ("load_assembly", {"path": str(ROOT / "missing.dll")}),
    ])

    assert r[0]["serverInfo"]["name"] == "dotpeek-mcp"
    assert len(r[1]["tools"]) == 7
    assert ".NETCoreApp,Version=v8.0" in text(r[2])
    assert "class public Fixture.Calculator" in text(r[3])
    assert "Add(" in text(r[4]) and "Greet(" in text(r[4])
    calc = text(r[5])
    assert "public class Calculator" in calc and "a + b" in calc, calc
    method = text(r[6])
    assert "a + b" in method and "Greet" not in method, method
    assert "Greet" in text(r[7])
    assert "AssemblyResolve" in text(r[8])  # .NET Framework assembly decompiles too
    assert r[9]["isError"] and "not found" in r[9]["content"][0]["text"]
    assert r[10]["isError"] and "File not found" in r[10]["content"][0]["text"]

    confined = session([("load_assembly", {"path": fw}), ("load_assembly", {"path": fx})],
                       env={"DOTPEEK_MCP_ALLOWED_DIRS": str(FIXTURE.parent)})
    assert confined[2]["isError"] and "outside DOTPEEK_MCP_ALLOWED_DIRS" in confined[2]["content"][0]["text"]
    assert confined[3]["isError"] is False
    print("smoke test OK")
    print(calc)


if __name__ == "__main__":
    main()
