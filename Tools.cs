using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DotPeekMcp
{
    /// <summary>A failure caused by the caller's input; reported back to the agent as a tool error.</summary>
    class ToolError : Exception
    {
        public ToolError(string message) : base(message) { }
    }

    static class Tools
    {
        static readonly int MaxChars = int.TryParse(Environment.GetEnvironmentVariable("DOTPEEK_MCP_MAX_CHARS"), out var n) && n > 0 ? n : 60000;

        // ponytail: unset means any path is allowed (local, single-user tool); set it to confine the agent.
        static readonly string[] AllowedDirs = (Environment.GetEnvironmentVariable("DOTPEEK_MCP_ALLOWED_DIRS") ?? "")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.GetFullPath(d.Trim()).TrimEnd('\\', '/') + Path.DirectorySeparatorChar)
            .ToArray();

        public static readonly JArray Definitions = new JArray
        {
            Def("load_assembly", "Load a .NET assembly (.dll/.exe) with dotPeek's metadata reader. Returns name, version, target framework, references and type count.",
                ("path", "Absolute path to the assembly", true)),
            Def("list_types", "List types of an assembly (kind, visibility, full name). Optional case-insensitive filter on the full name.",
                ("path", "Absolute path to the assembly", true), ("filter", "Substring to match in namespace or type name", false)),
            Def("get_type_members", "List methods, properties, fields and events of a type with their signatures.",
                ("path", "Absolute path to the assembly", true), ("type", "Full type name, e.g. My.Namespace.MyClass (nested: Outer+Inner)", true)),
            Def("decompile_type", "Decompile a whole type to C# with the dotPeek decompiler engine.",
                ("path", "Absolute path to the assembly", true), ("type", "Full type name", true)),
            Def("decompile_method", "Decompile one method (all overloads with that name) to C# with the dotPeek decompiler engine.",
                ("path", "Absolute path to the assembly", true), ("type", "Full type name", true), ("method", "Method name, e.g. Add or .ctor", true)),
            Def("search_members", "Search type and member names in an assembly (case-insensitive substring).",
                ("path", "Absolute path to the assembly", true), ("query", "Text to search for", true)),
            Def("open_in_dotpeek", "Open the assembly in the dotPeek GUI for a human to review (reuses a running dotPeek instance).",
                ("path", "Absolute path to the assembly", true)),
        };

        public static JObject Call(string name, JObject args)
        {
            try
            {
                return Text(Limit(Dispatch(name, args)), isError: false);
            }
            catch (ToolError e)
            {
                return Text(e.Message, isError: true);
            }
            catch (Exception e)
            {
                return Text($"{e.GetType().Name}: {e.Message}", isError: true);
            }
        }

        static string Dispatch(string name, JObject args)
        {
            var path = CheckedPath(args);
            switch (name)
            {
                case "load_assembly": return Engine.Describe(path);
                case "list_types": return Engine.ListTypes(path, (string)args["filter"]);
                case "get_type_members": return Engine.Members(path, Required(args, "type"));
                case "decompile_type": return Engine.DecompileType(path, Required(args, "type"));
                case "decompile_method": return Engine.DecompileMethod(path, Required(args, "type"), Required(args, "method"));
                case "search_members": return Engine.Search(path, Required(args, "query"));
                case "open_in_dotpeek":
                    Process.Start(Path.Combine(Program.DotPeekHome, "dotPeek64.exe"), $"\"{path}\"");
                    return $"Opened {path} in dotPeek.";
                default: throw new ToolError($"Unknown tool: {name}");
            }
        }

        static string CheckedPath(JObject args)
        {
            var raw = Required(args, "path");
            string full;
            try { full = Path.GetFullPath(raw); }
            catch (Exception e) { throw new ToolError($"Invalid path '{raw}': {e.Message}"); }

            if (AllowedDirs.Length > 0 && !AllowedDirs.Any(d => full.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                throw new ToolError($"Path '{full}' is outside DOTPEEK_MCP_ALLOWED_DIRS.");
            if (!File.Exists(full))
                throw new ToolError($"File not found: {full}");
            return full;
        }

        static string Required(JObject args, string key)
        {
            var value = (string)args[key];
            if (string.IsNullOrWhiteSpace(value)) throw new ToolError($"Missing required argument '{key}'.");
            return value;
        }

        static string Limit(string text) =>
            text.Length <= MaxChars
                ? text
                : text.Substring(0, MaxChars) + $"\n// [truncated: {MaxChars} of {text.Length} chars; use decompile_method or a narrower filter]";

        static JObject Text(string text, bool isError) =>
            new JObject { ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } }, ["isError"] = isError };

        static JObject Def(string name, string description, params (string Name, string Description, bool Required)[] parameters) =>
            new JObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject(parameters.Select(p => new JProperty(p.Name, new JObject { ["type"] = "string", ["description"] = p.Description }))),
                    ["required"] = new JArray(parameters.Where(p => p.Required).Select(p => p.Name)),
                },
            };
    }
}
