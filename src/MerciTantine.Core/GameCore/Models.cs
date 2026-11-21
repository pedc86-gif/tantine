using System.Text.Json;
using System.Text.Json.Serialization;

namespace MerciTantine.GameCore;

public enum Category { Object, Paper, Ally }
public enum Phase { Explore, Placement, Actions, Reaction, BurglaryChoice, Notary, Finished }
public enum Difficulty { Easy, Normal, Hard }
public sealed record PlayerSetup(string Name, bool Human = true, Difficulty Difficulty = Difficulty.Normal, string AiProfile = "");
public sealed class CardDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string AssetId { get; set; } = "";
    public Category Category { get; set; }
    public int Value { get; set; }
    public int Cost { get; set; }
    public string Effect { get; set; } = "";
    public int Amount { get; set; }
    public string TargetModel { get; set; } = "";
    public bool Hostile { get; set; }
    public bool Reaction { get; set; }
    public bool Immediate { get; set; }
}
public sealed record CardInstance(int Id, string Model, string Origin);
public sealed class Distribution
{
    public string Model { get; set; } = "";
    public string? Room { get; set; }
    public int Copies { get; set; }
}
public sealed class RoomDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Risk { get; set; } = "";
    public Category Secondary { get; set; }
    public int Weight { get; set; } = 1;
}
public sealed class Rules
{
    public bool RandomRoomDecks { get; set; }
    public string AiPolicy { get; set; } = "classic";
    public int DeclarationReward { get; set; }
    public int Rounds { get; set; } = 10;
    public int InitialCash { get; set; } = 10;
    public int InitialTax { get; set; } = 30;
    public int MinTax { get; set; } = 0;
    public int MaxTax { get; set; } = 70;
    public int SalesPerTurn { get; set; } = 2;
    public int NormalSalePercent { get; set; } = 50;
    public int BrokerSalePercent { get; set; } = 75;
    public string Exhaustion { get; set; } = "RejectChoice";
    public string TaxRounding { get; set; } = "Floor";
    public bool TaxNegativeBase { get; set; } = false;
    public bool AllowNegativeSwiss { get; set; } = true;
    public bool SellAcquiredSameTurn { get; set; } = true;
    public bool StackSkip { get; set; } = false;
    public bool LockBlocksBurglary { get; set; } = false;
    public string SpecialPlacement { get; set; } = "HiddenThenActivate";
    public string ReactionPriority { get; set; } = "TargetChoosesOne";
    public string NotaryPriority { get; set; } = "RotatingSeatOrder";
}
public sealed class PlayerState
{
    public string AiProfile { get; set; } = "";
    public int DecisionSeed { get; set; }
    public int DeclarationRewardRound { get; set; }
    public string Name { get; set; } = "";
    public bool Human { get; set; }
    public Difficulty Difficulty { get; set; }
    public int Cash { get; set; }
    public int Tax { get; set; }
    public int Skip { get; set; }
    public int Sales { get; set; }
    public int Penalties { get; set; }
    public List<int> Hand { get; set; } = [];
    public List<int> Hidden { get; set; } = [];
    public List<int> Declared { get; set; } = [];
    public List<int> Swiss { get; set; } = [];
    public List<int> Protections { get; set; } = [];
    public List<int> Discard { get; set; } = [];
    public List<int> AcquiredThisTurn { get; set; } = [];
}
public sealed class RoomState
{
    public string Id { get; set; } = "";
    public List<int> Objects { get; set; } = [];
    public List<int> Secondary { get; set; } = [];
    public int? Occupant { get; set; }
}
public sealed class PendingAction
{
    public int Actor { get; set; }
    public int Target { get; set; }
    public int Card { get; set; }
    public int Paid { get; set; }
    public List<int> Objects { get; set; } = [];
    public List<int> Candidates { get; set; } = [];
    public Phase ReturnPhase { get; set; }
}
public sealed record GameEvent(int Round, string Message, int? PrivateTo = null);
public sealed class GameState
{
    public int Schema { get; set; } = 1;
    public ulong RandomState { get; set; }
    public int Round { get; set; } = 1;
    public int TurnIndex { get; set; }
    public int Current { get; set; }
    public int NotaryPasses { get; set; }
    public Phase Phase { get; set; } = Phase.Explore;
    public List<PlayerState> Players { get; set; } = [];
    public List<CardInstance> Cards { get; set; } = [];
    public List<RoomState> Rooms { get; set; } = [];
    public List<int> Removed { get; set; } = [];
    public List<int> Sold { get; set; } = [];
    public List<int> SpecialDiscard { get; set; } = [];
    public List<GameEvent> Events { get; set; } = [];
    public PendingAction? Pending { get; set; }
    public List<int> ExplorationCards { get; set; } = [];
    public int TheftAttempts { get; set; }
    public int Thefts { get; set; }
    public int TaxEffects { get; set; }
    public int Declarations { get; set; }
    public Dictionary<string, List<int>> DrawnValues { get; set; } = [];
}
public sealed record PlayCommand(int Card, int? Target = null, int[]? Objects = null, int? Replay = null);
public sealed record Score(int Seat, string Name, int Declared, int Tax, int Hidden, int Swiss, int Cash, int Penalties, int Fortune);
public sealed class RuleException(string message) : Exception(message);
public static class JsonFormat
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}

// Serializable PRNG: no dependency on System.Random implementation or hidden runtime state.
public static class RandomSource
{
    public static int Next(GameState state, int max)
    {
        if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max));
        ulong x = state.RandomState;
        x ^= x >> 12; x ^= x << 25; x ^= x >> 27;
        state.RandomState = x;
        return (int)((x * 2685821657736338717UL) % (ulong)max);
    }
    public static void Shuffle<T>(GameState state, IList<T> values)
    {
        for (int i = values.Count - 1; i > 0; i--) { int j = Next(state, i + 1); (values[i], values[j]) = (values[j], values[i]); }
    }
}
