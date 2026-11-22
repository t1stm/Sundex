using Sundex.Reactive;

namespace FlyingMoai;

public enum Phase { Title, Playing, Over }

/// <summary>What the markup reads as <c>Context.X</c>. The scene writes it; the documents only show it.</summary>
public sealed class GameState(int best, Action<int> saveBest)
{
    public State<Phase> Phase { get; } = new(FlyingMoai.Phase.Title);
    public State<int> Score { get; } = new(0);
    public State<int> Best { get; } = new(best);
    public State<bool> NewBest { get; } = new(false);

    /// <summary>Set a moment after a crash, so the results card doesn't cover the fall.</summary>
    public State<bool> ShowResults { get; } = new(false);

    /// <summary>Starts another run. The scene sets it; the results card's button calls it.</summary>
    public Action Again { get; set; } = () => { };

    public void SaveBest() => saveBest(Best.Peek);
}
