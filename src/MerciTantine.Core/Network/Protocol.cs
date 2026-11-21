using MerciTantine.GameCore;

namespace MerciTantine.Network;

public sealed record CreateRoomRequest(string Name, int Humans = 2, int Bots = 0, Difficulty Difficulty = Difficulty.Normal);
public sealed record JoinRoomRequest(string Name);
public sealed record SeatCredential(string Code, string Token, int Seat);
public sealed record OnlineSnapshot(string Code, long Revision, bool Started, int JoinedHumans, int RequiredHumans, int Seat, int ActingSeat, string ActingName, bool CanAct, PlayerView View, Rules Rules, IReadOnlyList<Score> Scores);
public sealed class NetworkCommand
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
    public long ExpectedRevision { get; set; }
    public string Kind { get; set; } = "";
    public string Room { get; set; } = "";
    public Category First { get; set; }
    public Category Second { get; set; }
    public bool DeclareObjects { get; set; }
    public bool DeferPlacement { get; set; }
    public int Card { get; set; }
    public int? Target { get; set; }
    public int[]? Objects { get; set; }
    public int? Replay { get; set; }
    public bool PassReaction { get; set; }
}
