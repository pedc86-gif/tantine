using MerciTantine.GameCore;

namespace MerciTantine.AIPlayers;

// Decisions depend only on this detached view, never on a deck or an opponent's secrets.
public static class BalancedPolicy
{
    public static readonly string[] Profiles=["collectionneur","intrigant","prudent","declarant","cambrioleur","banquier"];
    static string Profile(PlayerView v)=>Profiles.Contains(v.AiProfile)?v.AiProfile:Profiles[v.Seat%Profiles.Length];
    static Random RandomFor(PlayerView v,int salt)
    {
        int hash=unchecked(v.DecisionSeed^salt^v.Round*7919^v.Seat*101^v.Cash*997^v.Tax*31);
        foreach(var card in v.Hand.Concat(v.Hidden).Concat(v.Declared).Concat(v.Discard))hash=unchecked(hash*16777619^card.Instance);
        foreach(var room in v.Rooms)hash=unchecked(hash*31+room.ObjectsLeft*7+room.SecondaryLeft);
        return new Random(hash);
    }
    public static bool ShouldDeclare(PlayerView v,Rules rules)
    {
        if(v.Tax>35)return false;
        double chance=Profile(v)=="declarant"?.4:0;
        if(rules.DeclarationReward>0&&!v.DeclarationRewardClaimed&&v.Cash<6&&v.Tax<=30)chance=Math.Max(chance,.3);
        return RandomFor(v,431).NextDouble()<chance;
    }
    public static (string Room,Category First,Category Second) Explore(PlayerView v)
    {
        string style=Profile(v);var random=RandomFor(v,131);
        var choices=v.Rooms.Where(r=>!r.Occupied).SelectMany(r=>new[]{(Room:r,First:Category.Object,Second:Category.Object),(Room:r,First:Category.Object,Second:r.Secondary),(Room:r,First:r.Secondary,Second:r.Secondary)})
            .Where(t=>(t.First==Category.Object?1:0)+(t.Second==Category.Object?1:0)<=t.Room.ObjectsLeft&&(t.First!=Category.Object?1:0)+(t.Second!=Category.Object?1:0)<=t.Room.SecondaryLeft).ToArray();
        if(choices.Length==0)return("",Category.Object,Category.Object);
        var best=choices.Select(t=>{int objects=(t.First==Category.Object?1:0)+(t.Second==Category.Object?1:0);double action=v.Hand.Count<2?3.5:v.Hand.Count<4?2:.65;
            if(style=="intrigant"&&t.Room.Secondary==Category.Paper)action+=.7;if(style=="cambrioleur"&&t.Room.Secondary==Category.Ally)action+=.6;if(style=="banquier"&&t.Room.Secondary==Category.Paper)action+=.4;
            double score=objects*t.Room.ExpectedValue*(style=="collectionneur"?1.12:1)+(2-objects)*action;
            if(style=="prudent")score-=objects*.3*(t.Room.Risk switch{"Moyen"=>.5,"Élevé"=>1,"Très élevé"=>1.4,_=>0});
            if(v.Round>=8)score+=objects*.8;return(t,score:score+random.NextDouble()*2.8-1.4);
        }).OrderByDescending(t=>t.score).First().t;return(best.Room.Id,best.First,best.Second);
    }
    public static ActionOption? Decide(IEnumerable<ActionOption> options,PlayerView v,Rules rules)
    {
        string style=Profile(v);var random=RandomFor(v,791);
        var known=v.Hand.Concat(v.Hidden).Concat(v.Declared).Concat(v.Discard).Concat(v.Opponents.SelectMany(o=>o.Declared)).GroupBy(c=>c.Instance).ToDictionary(g=>g.Key,g=>g.First().Definition);
        double Score(ActionOption a){var d=known[a.Replay??a.Card];double sum=(a.Objects??[]).Sum(id=>known[id].Value);
            if(a.Kind=="sell")return v.Cash<6?8-known[a.Card].Value*.3:-2;if(a.Kind=="lockpick")return 3;
            var opponent=a.Target.HasValue?v.Opponents.Single(o=>o.Seat==a.Target):null;
            double value=d.Effect switch{
                "broker"=>(v.Cash<6?7:0)+Math.Floor(sum*.75)-Math.Floor(sum*.5)-d.Cost-(v.Cash>=6?sum*.3:0),
                "swiss"=>sum*(.15+((a.Objects??[]).Any(id=>v.Declared.Any(k=>k.Instance==id))?v.Tax/100d:0))-d.Cost+1.5,
                "declare"=>sum*(v.Protections.Count==0?.12:.02)-sum*v.Tax/100d*.45+(!v.DeclarationRewardClaimed&&rules.DeclarationReward>0?2:0)+(style=="declarant"?.5:0),
                "tax"=>d.Hostile?opponent!.Declared.Sum(k=>Math.Max(0,k.Definition.Value))*d.Amount/100d*.8-d.Cost:v.Declared.Sum(k=>Math.Max(0,k.Definition.Value))*(-d.Amount)/100d+.2,
                "audit"=>opponent!.HiddenCount*3*(opponent.Tax+d.Amount)/100d*.7-d.Cost,
                "steal"=>4.5-d.Cost,"burglary"=>10-d.Cost,"pickpocket"=>4-d.Cost,
                "claim"=>sum*(1-v.Tax/100d)+sum*.5-d.Cost,"swap"=>3-sum,"skip"=>5-d.Cost,_=>a.Utility};
            if(style=="banquier"&&d.Effect=="swiss")value*=1.15;if(style=="cambrioleur"&&d.Effect is "steal" or "burglary" or "pickpocket")value*=1.1;if(style=="intrigant"&&d.Hostile)value*=1.1;
            return value+random.NextDouble()*.4;
        }
        return options.Where(a=>a.Kind!="play"||known[a.Replay??a.Card].Cost<=v.Cash).Select(a=>(a,score:Score(a))).Where(x=>x.score>.1).OrderByDescending(x=>x.score).Select(x=>x.a).FirstOrDefault();
    }
}
