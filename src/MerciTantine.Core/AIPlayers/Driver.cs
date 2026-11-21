using MerciTantine.GameCore;
using MerciTantine.GameEngine;

namespace MerciTantine.AIPlayers;

public static class Driver
{
    public static int ActingSeat(Engine e) => e.State.Phase == Phase.Reaction ? e.State.Pending!.Target : e.State.Current;
    public static void Apply(Engine e, ActionOption a)
    {
        switch(a.Kind)
        {
            case "sell": e.Sell(a.Card); break;
            case "lock": e.InstallLock(a.Card); break;
            case "lockpick": e.UseLockpick(a.Card,a.Target!.Value); break;
            default: e.Play(new(a.Card,a.Target,a.Objects,a.Replay)); break;
        }
    }
    public static void Step(Engine e)
    {
        var ai = new Agent(e.Catalog.Rules); var v = Views.For(e,ActingSeat(e));
        switch(e.State.Phase)
        {
            case Phase.Explore:
                var x=ai.Explore(v); if(x.Room=="")e.SkipImpossibleExploration();else e.Explore(x.Room,x.First,x.Second,ai.ShouldDeclare(v));break;
            case Phase.Reaction:e.React(ai.React(v));break;
            case Phase.Placement:
                if(e.State.ExplorationCards.Count == 1) { var room=e.State.Rooms.Single(r=>r.Occupant==e.State.Current);e.DrawExploration(room.Id,room.Objects.Count>0?Category.Object:e.Catalog.Rooms.Single(r=>r.Id==room.Id).Secondary); }
                else e.PlaceExploration(ai.ShouldDeclare(v));break;
            case Phase.BurglaryChoice:e.ChooseBurglary(ai.ChooseBurglary(v));break;
            case Phase.Actions:case Phase.Notary:var action=ai.Decide(v);if(action==null)e.EndTurn();else Apply(e,action);break;
        }
    }
    public static void UntilHuman(Engine e)
    {
        int guard=0;
        while(e.State.Phase!=Phase.Finished&&!e.State.Players[ActingSeat(e)].Human)
        {
            Engine.Require(++guard<3000,"Le pilote IA a dépassé sa limite de sécurité.");Step(e);
        }
    }
}
