using System.Reflection;
using System.Text.RegularExpressions;
using Sundex.Components.Abstractions;
using Sundex.Components.Attributes;
using Sundex.Style.DSL.Abstract;
using Sundex.Style.DSL.Abstract.Values;
using Sundex.Style.DSL.Abstract.Values.Keywords;

namespace Sundex.Style.DSL.LanguageServer;

/// <summary>A problem in a stylesheet, zero-based like the language server protocol.</summary>
public readonly record struct StyleDiagnostic(int Line, int Column, int Length, bool IsError, string Message);

/// <summary>
///     Checks a stylesheet the way an app will load it: <see cref="StyleParser" /> and
///     <see cref="StyleSheet" /> find the errors, and every property is checked against the
///     settings an element can take, which the runtime would otherwise ignore without a word.
/// </summary>
/// <param name="unsaved">The text of an open, possibly unsaved file, by full path; null to read it from disk.</param>
public sealed partial class StyleLinter(Func<string, string?> unsaved)
{
    private static readonly HashSet<string> SundexSettings = [.. SettingsIn(typeof(UIElement).Assembly)];
    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".git", ".idea", ".vs", "artifacts"];

    // ponytail: read once per solution; a new project or [NamedSetting] needs a server restart.
    private readonly Dictionary<string, (string[] Projects, HashSet<string> Settings)> _solutions = [];

    public List<StyleDiagnostic> Lint(string path, string text)
    {
        var diagnostics = new List<StyleDiagnostic>();
        var project = FindUp(Path.GetDirectoryName(path)!, "*.csproj") ?? Path.GetDirectoryName(path)!;
        var root = FindUp(project, "*.slnx", "*.sln") ?? FindUp(project, ".git") ?? project;
        var solution = Solution(root);
        // The asset provider looks in the sheet's project, then the others; last, like a run from the
        // repository's root would, the root itself.
        string[] folders = [project, .. solution.Projects, root];

        StyleSheetHolder holder;
        try
        {
            holder = StyleParser.Parse(text, import => Load(import, folders));
            // What the sheet checks while an app builds it: units, keyframes, states.
            _ = new StyleSheet(holder);
        }
        catch (StyleParseException exception) when (exception.Imports.Count == 0)
        {
            diagnostics.Add(At(text, exception.Offset, 1, true, exception.Reason));
            return diagnostics;
        }
        catch (StyleParseException exception)
        {
            var failed = exception.Imports[^1];
            var (line, column) = Position(Load(failed, folders), exception.Offset);
            diagnostics.Add(AtImport(text, exception.Imports[0],
                $"{failed}({line + 1},{column + 1}): {exception.Reason}"));
            return diagnostics;
        }
        catch (FileNotFoundException exception)
        {
            diagnostics.Add(AtImport(text, exception.FileName ?? "", exception.Message));
            return diagnostics;
        }
        catch (Exception exception)
        {
            // The sheet's own checks carry no position.
            diagnostics.Add(new StyleDiagnostic(0, 0, 0, true, exception.Message));
            return diagnostics;
        }

        var unknown = new HashSet<string>();
        foreach (var blocks in (Dictionary<string, Dictionary<string, IStyleValue>>[])
                 [holder.Components, holder.Classes, holder.IDTags])
        foreach (var properties in blocks.Values)
            CollectUnknown(properties, solution.Settings, unknown);

        // Blocks merged in from imports are checked in their own files: only flag what is written here.
        foreach (var name in unknown)
        foreach (Match match in Regex.Matches(text, $@"(?<![\w$.-]){Regex.Escape(name)}(?=\s*=)"))
            if (!IsVariableName(text, match.Index))
                diagnostics.Add(At(text, match.Index, name.Length, false,
                    $"No element has a setting called '{name}', so this line does nothing"));

        return diagnostics;
    }

    private static void CollectUnknown(Dictionary<string, IStyleValue> properties, HashSet<string> projectSettings,
        HashSet<string> unknown)
    {
        foreach (var (key, value) in properties)
        {
            if (key.StartsWith("state[", StringComparison.Ordinal) || key.StartsWith("media[", StringComparison.Ordinal))
            {
                var nested = value switch
                {
                    BlockValue block => block,
                    OverrideValue { Value: BlockValue block } => block,
                    _ => null
                };
                if (nested != null) CollectUnknown(nested.Properties, projectSettings, unknown);
                continue;
            }

            if (!SundexSettings.Contains(key) && !projectSettings.Contains(key)) unknown.Add(key);
        }
    }

    private string Load(string import, string[] folders)
    {
        foreach (var folder in folders)
        {
            var candidate = Path.GetFullPath(Path.Combine(folder, import));
            if (unsaved(candidate) is { } text) return text;
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"No stylesheet at '{import}' in this project or the others beside it", import);
    }

    /// <summary>The projects an import can reach, as the asset provider can, and the settings their code adds.</summary>
    private (string[] Projects, HashSet<string> Settings) Solution(string root)
    {
        if (_solutions.TryGetValue(root, out var solution)) return solution;

        var projects = new List<string>();
        var settings = new HashSet<string>();
        var pending = new Stack<string>([root]);
        while (pending.TryPop(out var folder))
        {
            try
            {
                if (Directory.EnumerateFiles(folder, "*.csproj").Any()) projects.Add(folder);
                foreach (var file in Directory.EnumerateFiles(folder, "*.cs"))
                foreach (Match match in NamedSetting().Matches(File.ReadAllText(file)))
                    settings.Add(match.Groups[1].Value);
                foreach (var child in Directory.EnumerateDirectories(folder))
                    if (!SkippedFolders.Contains(Path.GetFileName(child)))
                        pending.Push(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unreadable folder only hides what is in it.
            }
        }

        return _solutions[root] = ([.. projects], settings);
    }

    private static IEnumerable<string> SettingsIn(Assembly assembly) =>
        assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(property => property.GetCustomAttribute<NamedSettingAttribute>()?.Name)
            .OfType<string>();

    private static string? FindUp(string folder, params string[] patterns)
    {
        for (var current = new DirectoryInfo(folder); current != null; current = current.Parent)
            foreach (var pattern in patterns)
                if (current.EnumerateFileSystemInfos(pattern).Any())
                    return current.FullName;
        return null;
    }

    private static bool IsVariableName(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        return text.AsSpan(lineStart, index - lineStart).Trim().SequenceEqual("var");
    }

    private static StyleDiagnostic AtImport(string text, string import, string message)
    {
        var index = text.IndexOf($"\"{import}\"", StringComparison.Ordinal);
        return index < 0
            ? new StyleDiagnostic(0, 0, 0, true, message)
            : At(text, index, import.Length + 2, true, message);
    }

    private static StyleDiagnostic At(string text, int offset, int length, bool isError, string message)
    {
        var (line, column) = Position(text, offset);
        return new StyleDiagnostic(line, column, length, isError, message);
    }

    private static (int Line, int Column) Position(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (text[index] != '\n') continue;
            line++;
            lineStart = index + 1;
        }

        return (line, offset - lineStart);
    }

    [GeneratedRegex("""NamedSetting\("([a-z0-9-]+)"\)""")]
    private static partial Regex NamedSetting();
}
