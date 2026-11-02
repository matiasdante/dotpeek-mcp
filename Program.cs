using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotPeekMcp
{
    static class Program
    {
        internal static readonly string DotPeekHome = Environment.GetEnvironmentVariable("DOTPEEK_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"JetBrains\Installations\dotPeek262");

        static int Main()
        {
            if (!File.Exists(Path.Combine(DotPeekHome, "JetBrains.Decompiler.Core.dll")))
            {
                Console.Error.WriteLine($"dotPeek not found in '{DotPeekHome}'. Set DOTPEEK_HOME to the dotPeek install folder.");
                return 1;
            }

            // Version-agnostic, like the binding redirects dotPeek's own launcher applies.
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var file = Path.Combine(DotPeekHome, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            return Run();
        }

        // Separate method: the JIT must not touch JetBrains/Newtonsoft types before the resolver is hooked.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Run() => Server.Run();
    }
}
