using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotPeekMcp
{
    /// <summary>Minimal MCP server over stdio: newline-delimited JSON-RPC 2.0.</summary>
    static class Server
    {
        public static int Run()
        {
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            Console.SetOut(Console.Error); // stray writes must not corrupt the protocol stream

            string line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Trim().Length == 0) continue;
                JObject request;
                try { request = JObject.Parse(line); }
                catch (JsonException) { output.WriteLine(Error(null, -32700, "Parse error")); continue; }

                var id = request["id"];
                if (id == null) continue; // notification (e.g. notifications/initialized)

                string response;
                try
                {
                    var result = Handle((string)request["method"], request["params"] as JObject ?? new JObject());
                    response = new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result }.ToString(Formatting.None);
                }
                catch (MissingMethodException e) { response = Error(id, -32601, e.Message); }
                catch (Exception e) { response = Error(id, -32603, e.Message); }
                output.WriteLine(response);
            }
            return 0;
        }

        static JToken Handle(string method, JObject p)
        {
            switch (method)
            {
                case "initialize":
                    return new JObject
                    {
                        ["protocolVersion"] = (string)p["protocolVersion"] ?? "2025-06-18",
                        ["capabilities"] = new JObject { ["tools"] = new JObject() },
                        ["serverInfo"] = new JObject { ["name"] = "dotpeek-mcp", ["version"] = "0.1.0" },
                    };
                case "ping":
                    return new JObject();
                case "tools/list":
                    return new JObject { ["tools"] = Tools.Definitions };
                case "tools/call":
                    return Tools.Call((string)p["name"], p["arguments"] as JObject ?? new JObject());
                default:
                    throw new MissingMethodException($"Method not found: {method}");
            }
        }

        static string Error(JToken id, int code, string message) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = code, ["message"] = message } }
                .ToString(Formatting.None);
    }
}
