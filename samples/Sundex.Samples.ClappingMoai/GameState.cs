using Sundex.Reactive;

namespace ClappingMoai;

public enum Phase { Title, Playing, Over }

/// <summary>What the markup reads as <c>Context.X</c>. The scene writes it; the documents only show it.</summary>
public sealed class GameState
{
    // DoNotVerify: a folder that doesn't exist yet would otherwise come back as "", saving into the working directory.
    private static readonly string BestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "ClappingMoai", "best.txt");

    public State<Phase> Phase { get; } = new(ClappingMoai.Phase.Title);
    public State<int> Score { get; } = new(0);
    public State<int> Best { get; } = new(LoadBest());
    public State<bool> NewBest { get; } = new(false);

    /// <summary>Set a moment after a crash, so the results card doesn't cover the fall.</summary>
    public State<bool> ShowResults { get; } = new(false);

    /// <summary>Starts another run. The scene sets it; the results card's button calls it.</summary>
    public Action Again { get; set; } = () => { };

    public void SaveBest()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BestPath)!);
            File.WriteAllText(BestPath, Best.Peek.ToString());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // ponytail: a read-only profile only loses the best score.
        }
    }

    private static int LoadBest()
    {
        try
        {
            return int.TryParse(File.ReadAllText(BestPath), out var best) ? best : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
