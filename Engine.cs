using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using JetBrains.Application.Progress;
using JetBrains.Decompiler;
using JetBrains.Decompiler.Render.CSharp;
using JetBrains.Lifetimes;
using JetBrains.Metadata.Debug.CodeTextWriter;
using JetBrains.Metadata.Reader.API;
using JetBrains.Metadata.Utils.CodeTextWriter;
using JetBrains.Util;

namespace DotPeekMcp
{
    /// <summary>Wraps dotPeek's own engine: JetBrains metadata reader + JetBrains.Decompiler.Core C# renderer.</summary>
    static class Engine
    {
        // ponytail: assemblies stay loaded for the server's lifetime; add eviction if agents load hundreds of them.
        static readonly Dictionary<string, IMetadataAssembly> Loaded = new Dictionary<string, IMetadataAssembly>(StringComparer.OrdinalIgnoreCase);

        public static string Describe(string path)
        {
            var asm = Load(path);
            var tfm = asm.GetCustomAttributes("System.Runtime.Versioning.TargetFrameworkAttribute")
                .Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
                .FirstOrDefault() ?? "unknown";
            var sb = new StringBuilder();
            sb.AppendLine($"Assembly: {asm.AssemblyName}");
            sb.AppendLine($"Target framework: {tfm}");
            sb.AppendLine($"Types: {AllTypes(asm).Count()}");
            sb.AppendLine("References:");
            foreach (var r in asm.ReferencedAssembliesNames) sb.AppendLine($"  {r}");
            return sb.ToString();
        }

        public static string ListTypes(string path, string filter)
        {
            var lines = AllTypes(Load(path))
                .Where(t => string.IsNullOrEmpty(filter) || t.FullyQualifiedName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(t => $"{Kind(t)} {Visibility(t)} {t.FullyQualifiedName}")
                .ToList();
            return lines.Count == 0 ? "No types match." : string.Join("\n", lines);
        }

        public static string Members(string path, string typeName)
        {
            var type = FindType(Load(path), typeName);
            var sb = new StringBuilder();
            foreach (var m in type.GetMethods()) sb.AppendLine($"method   {Signature(m)}");
            foreach (var p in type.GetProperties()) sb.AppendLine($"property {Name(p.Type)} {p.Name}");
            foreach (var f in type.GetFields()) sb.AppendLine($"field    {(f.IsStatic ? "static " : "")}{Name(f.Type)} {f.Name}");
            foreach (var e in type.GetEvents()) sb.AppendLine($"event    {Name(e.Type)} {e.Name}");
            return sb.Length == 0 ? "Type has no members." : sb.ToString();
        }

        public static string Search(string path, string query)
        {
            const int max = 200;
            var hits = new List<string>();
            foreach (var t in AllTypes(Load(path)))
            {
                if (Has(t.FullyQualifiedName)) hits.Add($"type   {t.FullyQualifiedName}");
                hits.AddRange(t.GetMethods().Where(m => Has(m.Name)).Select(m => $"method {t.FullyQualifiedName} :: {Signature(m)}"));
                hits.AddRange(t.GetProperties().Where(p => Has(p.Name)).Select(p => $"prop   {t.FullyQualifiedName} :: {p.Name}"));
                hits.AddRange(t.GetFields().Where(f => Has(f.Name)).Select(f => $"field  {t.FullyQualifiedName} :: {f.Name}"));
                if (hits.Count >= max) break;
            }
            if (hits.Count == 0) return "No matches.";
            return string.Join("\n", hits.Take(max)) + (hits.Count > max ? $"\n... more than {max} matches, refine the query." : "");

            bool Has(string name) => name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string DecompileType(string path, string typeName)
        {
            var asm = Load(path);
            var type = FindType(asm, typeName);
            return Render(asm, type, (renderer, progress) => renderer.RenderTopLevelType(type, progress));
        }

        public static string DecompileMethod(string path, string typeName, string methodName)
        {
            var asm = Load(path);
            var type = FindType(asm, typeName);
            var overloads = type.GetMethods().Where(m => m.Name == methodName).ToList();
            if (overloads.Count == 0)
                throw new ToolError($"Method '{methodName}' not found in {type.FullyQualifiedName}. Use get_type_members to list them.");
            return Render(asm, type, (renderer, progress) =>
            {
                foreach (var m in overloads) renderer.RenderMethod(m, progress);
            });
        }

        // Same pipeline dotPeek uses to decompile a type to C# (CSharpMetadataTranslator), minus PSI-only extras.
        static string Render(IMetadataAssembly asm, IMetadataTypeInfo type, Action<MetadataRenderer, IProgressIndicator> render)
        {
            var cache = new DecompiledClassCache();
            var options = new ClassDecompilerOptions(decompileBodies: true);
            var formatter = FormatterOptions.Default;
            var renderOptions = new MetadataRenderOrchestrationOptions("using ", ";",
                new DecompiledClassPreRenderOptimizations(cache, null, type, false),
                IdentifierUtil.GetIdentifierFixer(true), type.NamespaceName, formatter);

            return MetadataRenderOrchestrator<MetadataRenderer>
                .CreateForDecompiler(renderOptions, writer => new MetadataRenderer(Lifetime.Eternal, asm, writer, cache, formatter, options))
                .DoRender(render, NullProgressIndicator.Create());
        }

        static IMetadataAssembly Load(string path)
        {
            if (Loaded.TryGetValue(path, out var cached)) return cached;

            // Probe the references first, so a .NET Framework assembly resolves against the framework
            // folder and a modern one against the newest shared runtime.
            var probe = new MetadataLoader(Vfs(Path.GetDirectoryName(path)));
            var refs = probe.LoadFrom(Vfs(path), _ => false).ReferencedAssembliesNames.Select(r => r.Name).ToList();
            var isFramework = refs.Contains("mscorlib") && !refs.Contains("System.Runtime") && !refs.Contains("netstandard");

            var folders = new[] { Path.GetDirectoryName(path) }.Concat(RuntimeFolders(isFramework)).Where(Directory.Exists);
            var asm = new MetadataLoader(folders.Select(Vfs).ToArray()).LoadFrom(Vfs(path), _ => true);
            Loaded[path] = asm;
            return asm;
        }

        static IEnumerable<string> RuntimeFolders(bool frameworkFirst)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var framework = new[]
            {
                Path.Combine(windows, @"Microsoft.NET\Framework64\v4.0.30319"),
                Path.Combine(windows, @"Microsoft.NET\Framework64\v4.0.30319\WPF"),
            };
            var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"dotnet\shared");
            var core = Directory.Exists(shared)
                ? Directory.GetDirectories(shared).Select(NewestVersion).Where(d => d != null).ToArray()
                : new string[0];
            return frameworkFirst ? framework.Concat(core) : core.Concat(framework);
        }

        static string NewestVersion(string frameworkDir) =>
            Directory.GetDirectories(frameworkDir)
                .Select(d => (Dir: d, Ok: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v), Ver: v))
                .Where(x => x.Ok)
                .OrderByDescending(x => x.Ver)
                .Select(x => x.Dir)
                .FirstOrDefault();

        static VirtualFileSystemPath Vfs(string path) => VirtualFileSystemPath.Parse(path, InteractionContext.Local);

        // GetTypes() is the flat TypeDef table: nested types are already included.
        static IEnumerable<IMetadataTypeInfo> AllTypes(IMetadataAssembly asm) => asm.GetTypes();

        static IMetadataTypeInfo FindType(IMetadataAssembly asm, string name) =>
            AllTypes(asm).FirstOrDefault(t => t.FullyQualifiedName == name)
            ?? throw new ToolError($"Type '{name}' not found. Use list_types or search_members to find it.");

        static string Kind(IMetadataTypeInfo t)
        {
            if (t.IsInterface) return "interface";
            switch (t.Base?.Type.FullyQualifiedName)
            {
                case "System.Enum": return "enum";
                case "System.ValueType": return "struct";
                case "System.MulticastDelegate": return "delegate";
                default: return "class";
            }
        }

        static string Visibility(IMetadataTypeInfo t) =>
            t.IsPublic || t.IsNestedPublic ? "public"
            : t.IsNestedPrivate ? "private"
            : t.IsNestedFamily ? "protected"
            : "internal";

        // "System.Func`1[[A.B, Asm, Version=..., PublicKeyToken=...]]" -> "System.Func`1<A.B>"
        static string Name(IMetadataType type) =>
            Regex.Replace(type.FullName, @", [^\[\],]+, Version=[^\]]*", "")
                .Replace("[[", "<").Replace("]]", ">").Replace("],[", ", ");

        static string Signature(IMetadataMethod m) =>
            $"{(m.IsStatic ? "static " : "")}{Name(m.ReturnValue.Type)} {m.Name}({string.Join(", ", m.Parameters.Select(p => $"{Name(p.Type)} {p.Name}"))})";
    }
}
