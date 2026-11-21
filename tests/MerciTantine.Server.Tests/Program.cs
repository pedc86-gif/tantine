using MerciTantine.Server;
using MerciTantine.GameCore;
using MerciTantine.Network;
using MerciTantine.AIPlayers;

string directory=Path.Combine(Path.GetTempPath(),"merci-tantine-tests-"+Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("SAVE_DIRECTORY",directory);
var service=new RoomService();
var credentials=new[]{service.Create(new("Alice",2,2,Difficulty.Hard))};
var host=credentials[0];var guest=service.Join(host.Code,new("Bob"));
var tokens=new[]{host,guest};
int steps=0;
while(true)
{
    var status=service.Read(host.Code,host.Token);if(status.View.Phase==Phase.Finished)break;
    if(++steps>3000)throw new Exception("Blocage réseau");
    var seat=tokens[status.ActingSeat];var s=service.Read(seat.Code,seat.Token);var agent=new Agent(s.Rules);NetworkCommand c=new(){ExpectedRevision=s.Revision};
    switch(s.View.Phase)
    {
        case Phase.Explore:var x=agent.Explore(s.View);c.Kind=x.Room==""?"skip-exploration":"explore";c.Room=x.Room;c.First=x.First;c.Second=x.Second;c.DeferPlacement=true;break;
        case Phase.Placement:c.Kind="placement";c.DeclareObjects=false;break;
        case Phase.Reaction:c.Kind="react";var r=agent.React(s.View);c.Card=r??0;c.PassReaction=r==null;break;
        case Phase.BurglaryChoice:c.Kind="burglary";c.Card=agent.ChooseBurglary(s.View);break;
        default:var a=agent.Decide(s.View);if(a==null)c.Kind="end";else{c.Kind=a.Kind;c.Card=a.Card;c.Target=a.Target;c.Objects=a.Objects;c.Replay=a.Replay;}break;
    }
    var result=service.Command(seat.Code,seat.Token,c);
    var duplicate=service.Command(seat.Code,seat.Token,c);
    if(result.Revision!=duplicate.Revision)throw new Exception("Commande résolue deux fois.");
    // A new service instance rehydrates every game from disk, even mid reaction.
    service=new RoomService();var resumed=service.Read(seat.Code,seat.Token);
    if(resumed.Revision!=result.Revision||resumed.View.Phase!=result.View.Phase)throw new Exception("Reprise serveur divergente.");
}
var final=service.Read(host.Code,host.Token);
if(final.Scores.Count!=4||final.View.Round!=10)throw new Exception("Partie incomplète.");
Console.WriteLine($"OK partie réseau complète : {steps} commandes, doublons ignorés, reprise disque après chaque action, 4 scores.");
// Files remain available for forensic inspection; directory is unique and contains no credentials in logs.
return 0;
