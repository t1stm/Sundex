using System.Text;
using System.Text.Json.Nodes;
using Sundex.Style.DSL.LanguageServer;

// sundex-style-ls: a language server for .snx.ss over stdio, or `--check <files…>` for a build or CI.
if (args is ["--check", .. var files]) return Check(files);
return new Server().Run();

static int Check(string[] files)
{
    var linter = new StyleLinter(_ => null);
    var failed = false;
    foreach (var file in files)
    foreach (var problem in linter.Lint(Path.GetFullPath(file), File.ReadAllText(file)))
    {
        // MSBuild's format, which editors and CI already turn into links.
        Console.WriteLine($"{file}({problem.Line + 1},{problem.Column + 1}): " +
                          $"{(problem.IsError ? "error" : "warning")} SDXS: {problem.Message}");
        failed |= problem.IsError;
    }

    return failed ? 1 : 0;
}

/// <summary>
///     The few messages an editor needs for live checks: open, change and close, answered with
///     diagnostics. Full-document sync keeps it to that; a stylesheet is small.
/// </summary>
internal sealed class Server
{
    private readonly Stream _input = Console.OpenStandardInput();
    private readonly Stream _output = Console.OpenStandardOutput();
    private readonly Dictionary<string, (string Uri, string Text)> _open = [];
    private readonly StyleLinter _linter;

    public Server() => _linter = new StyleLinter(path => _open.TryGetValue(path, out var document) ? document.Text : null);

    public int Run()
    {
        while (Read() is { } message)
        {
            var id = message["id"]?.DeepClone();
            var parameters = message["params"];
            switch (message["method"]?.GetValue<string>())
            {
                case "initialize":
                    Send(new JsonObject
                    {
                        ["id"] = id,
                        ["result"] = new JsonObject
                        {
                            ["capabilities"] = new JsonObject { ["textDocumentSync"] = 1 },
                            ["serverInfo"] = new JsonObject { ["name"] = "sundex-style-ls" }
                        }
                    });
                    break;
                case "shutdown":
                    Send(new JsonObject { ["id"] = id, ["result"] = null });
                    break;
                case "exit":
                    return 0;
                case "textDocument/didOpen":
                    Store(parameters!["textDocument"]!["uri"]!.GetValue<string>(), parameters["textDocument"]!["text"]!.GetValue<string>());
                    break;
                case "textDocument/didChange":
                    Store(parameters!["textDocument"]!["uri"]!.GetValue<string>(),
                        parameters["contentChanges"]!.AsArray()[^1]!["text"]!.GetValue<string>());
                    break;
                case "textDocument/didClose":
                    var uri = parameters!["textDocument"]!["uri"]!.GetValue<string>();
                    _open.Remove(PathOf(uri));
                    Publish(uri, []);
                    LintOpen();
                    break;
                default:
                    // Notifications need no answer; a request this server doesn't know does.
                    if (id != null)
                        Send(new JsonObject
                        {
                            ["id"] = id,
                            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Not supported" }
                        });
                    break;
            }
        }

        return 0;
    }

    private void Store(string uri, string text)
    {
        _open[PathOf(uri)] = (uri, text);
        LintOpen();
    }

    /// <summary>Every open sheet, not only the edited one: a sheet that imports it may have just broken or healed.</summary>
    private void LintOpen()
    {
        foreach (var (path, (uri, text)) in _open)
        {
            List<StyleDiagnostic> problems;
            try
            {
                problems = _linter.Lint(path, text);
            }
            catch (Exception exception)
            {
                problems = [new StyleDiagnostic(0, 0, 0, true, $"The checker failed: {exception.Message}")];
            }

            Publish(uri, problems);
        }
    }

    private void Publish(string uri, List<StyleDiagnostic> problems)
    {
        var diagnostics = new JsonArray();
        foreach (var problem in problems)
            diagnostics.Add(new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = problem.Line, ["character"] = problem.Column },
                    ["end"] = new JsonObject { ["line"] = problem.Line, ["character"] = problem.Column + problem.Length }
                },
                ["severity"] = problem.IsError ? 1 : 2,
                ["source"] = "sundex",
                ["message"] = problem.Message
            });

        Send(new JsonObject
        {
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics }
        });
    }

    private static string PathOf(string uri) => Path.GetFullPath(new Uri(uri).LocalPath);

    private JsonNode? Read()
    {
        var length = -1;
        while (ReadLine() is { } header)
        {
            if (header.Length == 0)
            {
                if (length < 0) continue;
                var body = new byte[length];
                _input.ReadExactly(body);
                return JsonNode.Parse(body);
            }

            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                length = int.Parse(header["Content-Length:".Length..].Trim());
        }

        return null;
    }

    private string? ReadLine()
    {
        var line = new StringBuilder();
        while (true)
        {
            var next = _input.ReadByte();
            if (next < 0) return null;
            if (next == '\n') return line.ToString().TrimEnd('\r');
            line.Append((char)next);
        }
    }

    private void Send(JsonObject message)
    {
        message["jsonrpc"] = "2.0";
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        _output.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        _output.Write(body);
        _output.Flush();
    }
}
