using MerciTantine.GameEngine;

namespace MerciTantine.GameCore;

public sealed record KnownCard(int Instance, CardDefinition Definition);
public sealed record OpponentView(int Seat, string Name, int Cash, int Tax, int HandCount, int HiddenCount, IReadOnlyList<KnownCard> Declared, int SwissCount, int LockCount);
public sealed record RoomView(string Id, string Name, string Risk, Category Secondary, int ObjectsLeft, int SecondaryLeft, bool Occupied, double ExpectedValue);
public sealed record PlayerView(int Seat, int Round, Phase Phase, Difficulty Difficulty, int Cash, int Tax, int Sales,
    IReadOnlyList<KnownCard> Hand, IReadOnlyList<KnownCard> Hidden, IReadOnlyList<KnownCard> Declared, IReadOnlyList<KnownCard> Swiss,
    IReadOnlyList<KnownCard> Protections, IReadOnlyList<KnownCard> Discard, IReadOnlyList<OpponentView> Opponents,
    IReadOnlyList<RoomView> Rooms, IReadOnlyList<GameEvent> Events, IReadOnlyList<KnownCard> Reactions, IReadOnlyList<KnownCard> BurglaryCandidates, IReadOnlyList<int> AcquiredThisTurn, IReadOnlyList<KnownCard> ExplorationCards,int DecisionSeed=0,string AiProfile="",bool DeclarationRewardClaimed=false);

public static class Views
{
    // This detached DTO is the sole input to AI. Neither decks nor opponent secrets are included.
    public static PlayerView For(Engine engine, int seat)
    {
        var s = engine.State; var p = s.Players[seat];
        KnownCard Card(int id) => new(id, engine.Def(id));
        var opponents = s.Players.Select((o,i) => new OpponentView(i,o.Name,o.Cash,o.Tax,o.Hand.Count,o.Hidden.Count,o.Declared.Select(Card).ToArray(),o.Swiss.Count,o.Protections.Count)).Where(o => o.Seat != seat).ToArray();
        var rooms = s.Rooms.Select(r =>
        {
            var d = engine.Catalog.Rooms.Single(x => x.Id == r.Id);
            var objects = engine.Catalog.Distribution.Where(i => engine.Catalog[i.Model].Category==Category.Object&&(engine.Catalog.Rules.RandomRoomDecks||i.Room==r.Id)).ToList();
            return new RoomView(r.Id,d.Name,engine.Catalog.Rules.RandomRoomDecks?"":d.Risk,d.Secondary,r.Objects.Count,r.Secondary.Count,r.Occupant.HasValue,objects.Sum(i => engine.Catalog[i.Model].Value*i.Copies)/(double)objects.Sum(i=>i.Copies));
        }).ToArray();
        return new(seat,s.Round,s.Phase,p.Difficulty,p.Cash,p.Tax,p.Sales,p.Hand.Select(Card).ToArray(),p.Hidden.Select(Card).ToArray(),p.Declared.Select(Card).ToArray(),p.Swiss.Select(Card).ToArray(),p.Protections.Select(Card).ToArray(),p.Discard.Select(Card).ToArray(),opponents,rooms,s.Events.Where(e => e.PrivateTo == null || e.PrivateTo == seat).ToArray(),
            s.Phase == Phase.Reaction && s.Pending?.Target == seat ? engine.ReactionCards().Select(Card).ToArray() : [],
            s.Phase == Phase.BurglaryChoice && s.Pending?.Actor == seat ? s.Pending.Candidates.Select(Card).ToArray() : [],p.AcquiredThisTurn.ToArray(),s.Phase == Phase.Placement && s.Current == seat ? s.ExplorationCards.Select(Card).ToArray() : [],p.DecisionSeed,p.AiProfile,p.DeclarationRewardRound==s.Round);
    }
}
