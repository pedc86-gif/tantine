using MerciTantine.GameCore;

namespace MerciTantine.AIPlayers;

public sealed record ActionOption(string Label, string Kind, int Card, int? Target = null, int[]? Objects = null, int? Replay = null, double Utility = 0);
public sealed class Agent
{
    private readonly Rules rules;
    public Agent(Rules rules) => this.rules = rules;
    public bool ShouldDeclare(PlayerView view)=>rules.AiPolicy=="varied-v1"&&view.Difficulty!=Difficulty.Easy&&BalancedPolicy.ShouldDeclare(view,rules);
    public List<ActionOption> Options(PlayerView v)
    {
        var result = new List<ActionOption>();
        var sellable = v.Hidden.Where(c => c.Definition.Value > 0 && c.Definition.Effect == "" && (rules.SellAcquiredSameTurn || !v.AcquiredThisTurn.Contains(c.Instance))).OrderBy(c => c.Definition.Value).ToArray();
        if (v.Phase == Phase.Actions)
        {
            foreach (var c in v.Hidden.Concat(v.Declared).Where(c => c.Definition.Effect == "lock")) result.Add(new("Installer la serrure", "lock",c.Instance,Utility:8));
            foreach (var c in v.Hidden.Concat(v.Declared).Where(c => c.Definition.Effect == "lockpick"))
                foreach (var o in v.Opponents.Where(o => o.LockCount > 0)) result.Add(new($"Détruire la serrure de {o.Name}","lockpick",c.Instance,o.Seat,Utility:5));
            if (v.Sales < rules.SalesPerTurn)
                foreach (var c in sellable) result.Add(new($"Vendre {c.Definition.Name}","sell",c.Instance,Utility:v.Cash < 5 ? 9 - c.Definition.Value * .1 : -4));
        }
        foreach (var hand in v.Hand.Where(c => !c.Definition.Reaction && c.Definition.Cost <= v.Cash))
        {
            if (hand.Definition.Effect == "replay")
            {
                foreach (var paper in v.Discard.Where(c => c.Definition.Category == Category.Paper && !c.Definition.Reaction && c.Definition.Effect != "replay")) Add(paper,hand.Instance,paper.Instance);
            }
            else Add(hand,hand.Instance,null);
        }
        return result;
        void Add(KnownCard c, int played, int? replay)
        {
            var d = c.Definition;
            if (v.Phase == Phase.Notary && (d.Effect != "claim" || d.Category != Category.Paper || replay != null)) return;
            void Option(int? target = null, int[]? objects = null, double utility = 1) => result.Add(new($"{v.Hand.Single(h => h.Instance == played).Definition.Name}{(replay.HasValue ? $" → {d.Name}" : "")}{(target.HasValue ? $" → {v.Opponents.Single(o => o.Seat == target).Name}" : "")}{(objects?.Length > 0 ? $" ({objects.Length} objet(s))" : "")}","play",played,target,objects,replay,utility));
            switch(d.Effect)
            {
                case "declare":
                    var positive = v.Hidden.Where(i => i.Definition.Value > 0).Select(i => i.Instance).ToArray();
                    if (positive.Length > 0) Option(objects:positive,utility:v.Protections.Count == 0 && v.Round < 8 && v.Tax < 40 ? 3 : -2); break;
                case "swiss":
                    var safe = v.Hidden.Concat(v.Declared).Where(i => rules.AllowNegativeSwiss || i.Definition.Value >= 0).OrderByDescending(i => i.Definition.Value).Take(d.Amount).Where(i => i.Definition.Value > 0).ToArray();
                    if (safe.Length > 0) Option(objects:safe.Select(i => i.Instance).ToArray(),utility:safe.Sum(i => i.Definition.Value)*.3+3); break;
                case "tax": if (!d.Hostile) { if(v.Tax > rules.MinTax) Option(utility:4+v.Declared.Sum(i=>i.Definition.Value)*.1); } else foreach(var o in v.Opponents.Where(o => o.Tax < rules.MaxTax)) Option(o.Seat,utility:2+o.Declared.Sum(i=>i.Definition.Value)*.1); break;
                case "audit": foreach(var o in v.Opponents) Option(o.Seat,utility:o.HiddenCount*1.5+1); break;
                case "skip": foreach(var o in v.Opponents) Option(o.Seat,utility:7); break;
                case "claim": foreach(var o in v.Opponents) foreach(var item in o.Declared.Where(i => (d.TargetModel=="" || d.TargetModel==i.Definition.Id) && i.Definition.Value > 0)) Option(o.Seat,[item.Instance],utility:item.Definition.Value*1.5-d.Cost); break;
                case "steal": case "burglary": foreach(var o in v.Opponents.Where(o => o.HiddenCount>0 && (o.LockCount==0 || (d.Effect=="burglary"&&!rules.LockBlocksBurglary)))) Option(o.Seat,utility:d.Effect=="burglary"?8:3); break;
                case "pickpocket": foreach(var o in v.Opponents.Where(o => o.HandCount>0)) Option(o.Seat,utility:2); break;
                case "swap": foreach(var o in v.Opponents.Where(o => o.HiddenCount>0)) foreach(var own in v.Hidden.OrderBy(i=>i.Definition.Value).Take(1)) Option(o.Seat,[own.Instance],utility:3-own.Definition.Value); break;
                case "broker": if(v.Sales<rules.SalesPerTurn) foreach(var item in sellable) Option(objects:[item.Instance],utility:v.Cash<5?7:-5); break;
            }
        }
    }
    // Deterministic local choice based only on this player's visible observation.
    private static int Roll(PlayerView v, int max) => (int)(((uint)(v.Round * 7919 + v.Seat * 101 + v.Hand.Count * 97 + v.Hidden.Count * 53 + v.Cash * 13)) % (uint)max);
    public (string Room, Category First, Category Second) Explore(PlayerView v)
    {
        if(rules.AiPolicy=="varied-v1"&&v.Difficulty!=Difficulty.Easy)return BalancedPolicy.Explore(v);
        var options = v.Rooms.Where(r=>!r.Occupied).SelectMany(r=>new[]{(r,Category.Object,Category.Object),(r,Category.Object,r.Secondary),(r,r.Secondary,r.Secondary)})
            .Where(t => (t.Item2==Category.Object?1:0)+(t.Item3==Category.Object?1:0)<=t.r.ObjectsLeft && (t.Item2!=Category.Object?1:0)+(t.Item3!=Category.Object?1:0)<=t.r.SecondaryLeft).ToList();
        if(options.Count==0) return ("",Category.Object,Category.Object);
        var pick = v.Difficulty == Difficulty.Easy ? options[Roll(v, options.Count)] : options.OrderByDescending(t =>
            (t.Item2==Category.Object?1:0)+(t.Item3==Category.Object?1:0) is var n ? n*t.r.ExpectedValue + (2-n)*(v.Hand.Count < 3 ? 5 : 1) + (v.Difficulty==Difficulty.Hard && v.Round>7 ? n*2 : 0) : 0).First();
        return (pick.r.Id,pick.Item2,pick.Item3);
    }
    public ActionOption? Decide(PlayerView v)
    {
        if(rules.AiPolicy=="varied-v1"&&v.Difficulty!=Difficulty.Easy)return BalancedPolicy.Decide(Options(v),v,rules);
        var options = Options(v); if(options.Count==0) return null;
        if(v.Difficulty==Difficulty.Easy) return Roll(v,4)==0 ? null : options[Roll(v,options.Count)];
        var best = options.OrderByDescending(o=>o.Utility).First();
        if (v.Difficulty==Difficulty.Hard && v.Phase!=Phase.Notary && v.Round<9 && best.Kind=="play" && v.Hand.Single(c=>c.Instance==best.Card).Definition.TargetModel!="")
            best=options.Where(o=>o.Card!=best.Card).OrderByDescending(o=>o.Utility).FirstOrDefault() ?? best;
        return best.Utility>0?best:null;
    }
    public int? React(PlayerView v) => v.Reactions.OrderBy(c=>c.Definition.Cost).FirstOrDefault()?.Instance;
    public int ChooseBurglary(PlayerView v) => v.BurglaryCandidates.OrderByDescending(c=>c.Definition.Value).First().Instance;
}
