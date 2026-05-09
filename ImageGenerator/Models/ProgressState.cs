namespace ImageGenerator.Models;

public enum ProgressKind
{
    Preparing,
    Generating,
    SendingRequest,
}

public sealed class ProgressState
{
    public static readonly ProgressState Preparing = new(ProgressKind.Preparing);
    public static readonly ProgressState SendingRequest = new(ProgressKind.SendingRequest);

    public static ProgressState Generating(int current, int total) =>
        new(ProgressKind.Generating) { Current = current, Total = total };

    public ProgressKind Kind { get; }
    public int Current { get; private init; }
    public int Total { get; private init; }

    private ProgressState(ProgressKind kind) => Kind = kind;
}
