using Sundex.Style.DSL.LanguageServer;

namespace Sundex.Style.DSL.Tests;

public class StyleLinterTests
{
    private DirectoryInfo _folder = null!;

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("sundex-lint-");

    [TearDown]
    public void DeleteFolder() => _folder.Delete(true);

    [Test]
    public void ParseError_IsAnErrorOnItsLine()
    {
        var problems = Lint("""
                            class card {
                                width = $missing;
                            }
                            """);

        Assert.That(problems, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(problems[0].IsError, Is.True);
            Assert.That(problems[0].Line, Is.EqualTo(1));
            Assert.That(problems[0].Message, Does.Contain("Unknown variable 'missing'"));
        }
    }

    [Test]
    public void UnknownSetting_IsAWarningOnTheProperty()
    {
        var problems = Lint("""
                            class card {
                                width = 10px;
                                font-weight = "bold";
                                state[hovered] = { opacty = 0.5; };
                            }
                            """);

        Assert.That(problems.Select(problem => (problem.IsError, problem.Line, problem.Column, problem.Length)),
            Is.EquivalentTo(new[] { (false, 2, 4, 11), (false, 3, 23, 6) }));
    }

    [Test]
    public void ErrorInAnImport_IsReportedAtTheImport()
    {
        var problems = Lint("import \"theme.snx.ss\";\n", ("theme.snx.ss", "var gold = #ffcf86\nvar gold = #ffffff\n"));

        Assert.That(problems, Has.Count.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That((problems[0].Line, problems[0].Column, problems[0].Length), Is.EqualTo((0, 7, 14)));
            Assert.That(problems[0].Message, Does.StartWith("theme.snx.ss(2,5):"));
            Assert.That(problems[0].Message, Does.Contain("already defined"));
        }
    }

    [Test]
    public void MissingImport_IsAnErrorAtTheImport()
    {
        var problems = Lint("import \"nowhere.snx.ss\";\n");

        Assert.That(problems.Single().Message, Does.Contain("No stylesheet at 'nowhere.snx.ss'"));
    }

    [Test]
    public void ValidSheet_HasNoProblems()
    {
        var problems = Lint("""
                            var ink = #fff6e8
                            animation bob {
                                duration = 1400ms;
                                keyframes = !keyframes [
                                    0% = { transform = vec2(0, 0) },
                                    100% = { transform = vec2(0, -8px); loop = "invert" }
                                ];
                            }
                            component label { font-color = $ink; }
                            class again {
                                fill = #ff7a59;
                                state[hovered] = { fill = #ff9470; };
                                media[min-width = 640px] = { padding = 8px; };
                            }
                            """);

        Assert.That(problems, Is.Empty);
    }

    /// <summary>Lints <paramref name="text" /> as a sheet in an empty folder, with <paramref name="others" /> beside it, unsaved.</summary>
    private List<StyleDiagnostic> Lint(string text, params (string Name, string Text)[] others)
    {
        var files = others.ToDictionary(other => Path.Combine(_folder.FullName, other.Name), other => other.Text);
        return new StyleLinter(path => files.GetValueOrDefault(path)).Lint(Path.Combine(_folder.FullName, "sheet.snx.ss"), text);
    }
}
