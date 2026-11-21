using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MerciTantine.CardCatalog;
using MerciTantine.GameCore;
using MerciTantine.GameEngine;
using MerciTantine.AIPlayers;
using MerciTantine.Network;
using MerciTantine.Persistence;

namespace MerciTantine.Server;

public sealed class RevisionException(string message):Exception(message);
public sealed class RoomData
{
    public string Code { get; set; }="";
    public int RequiredHumans { get; set; }
    public long Revision { get; set; }
    public bool Started { get; set; }
    public Dictionary<int,string> TokenHashes { get; set; }=[];
    public Dictionary<int,List<string>> Requests { get; set; }=[];
    public SaveEnvelope Save { get; set; }=null!;
}
public sealed class RoomService
{
    private readonly ConcurrentDictionary<string,object> gates=new();
    private readonly string directory=Environment.GetEnvironmentVariable("SAVE_DIRECTORY")??Path.Combine(AppContext.BaseDirectory,"Saves");
    private readonly Catalog catalog=Catalog.Load(Path.Combine(AppContext.BaseDirectory,"Data"));
    public RoomService() => Directory.CreateDirectory(directory);
    private string PathFor(string code)
    {
        Engine.Require(code.Length==8&&code.All(Uri.IsHexDigit),"Code de partie invalide.");
        return Path.Combine(directory,code.ToUpperInvariant()+".json");
    }
    private static string Hash(string token)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string NewToken()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Name(string name)
    {
        Engine.Require(!string.IsNullOrWhiteSpace(name)&&name.Length<=30&&!name.Any(char.IsControl),"Le nom doit contenir 1 à 30 caractères imprimables.");return name.Trim();
    }
    private RoomData Load(string code)
    {
        string path=PathFor(code);Engine.Require(File.Exists(path),"Partie inconnue.");
        return JsonSerializer.Deserialize<RoomData>(File.ReadAllText(path),JsonFormat.Options)!;
    }
    private void Save(RoomData room)
    {
        var path=PathFor(room.Code);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(room,JsonFormat.Options));File.Move(path+".tmp",path,true);
    }
    private static int Authenticate(RoomData room,string token)
    {
        if(token.Length!=64)throw new UnauthorizedAccessException();
        string hash=Hash(token);
        foreach(var (seat,known) in room.TokenHashes)
            if(CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(known)))return seat;
        throw new UnauthorizedAccessException();
    }
    public SeatCredential Create(CreateRoomRequest request)
    {
        Engine.Require(request.Humans is >=1 and <=6&&request.Bots>=0&&request.Humans+request.Bots is >=2 and <=6,"Il faut 2 à 6 joueurs dont au moins un humain.");
        string name=Name(request.Name),code;
        do { code=Convert.ToHexString(RandomNumberGenerator.GetBytes(4)); } while(File.Exists(PathFor(code)));
        string token=NewToken();
        var players=Enumerable.Range(0,request.Humans+request.Bots).Select(i=>new PlayerSetup(i==0?name:i<request.Humans?$"Invité {i+1}":$"IA {i+1}",i<request.Humans,request.Difficulty));
        var e=Engine.New(catalog,players,BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
        var room=new RoomData {Code=code,RequiredHumans=request.Humans,TokenHashes=new(){[0]=Hash(token)},Save=new(1,catalog,e.State),Started=request.Humans==1};
        Save(room);return new(code,token,0);
    }
    public SeatCredential Join(string code,JoinRoomRequest request)
    {
        string path=PathFor(code);
        lock(gates.GetOrAdd(path,_=>new()))
        {
            var room=Load(code);Engine.Require(!room.Started&&room.TokenHashes.Count<room.RequiredHumans,"La partie est complète ou déjà lancée.");
            int seat=room.TokenHashes.Count;string token=NewToken();room.Save.State.Players[seat].Name=Name(request.Name);room.TokenHashes.Add(seat,Hash(token));room.Revision++;
            room.Started=room.TokenHashes.Count==room.RequiredHumans;Save(room);return new(room.Code,token,seat);
        }
    }
    public OnlineSnapshot Read(string code,string token)
    {
        lock(gates.GetOrAdd(PathFor(code),_=>new())){var room=Load(code);return Snapshot(room,Authenticate(room,token));}
    }
    private static OnlineSnapshot Snapshot(RoomData room,int seat)
    {
        var e=new Engine(room.Save.Catalog,room.Save.State);int acting=Driver.ActingSeat(e);
        return new(room.Code,room.Revision,room.Started,room.TokenHashes.Count,room.RequiredHumans,seat,acting,e.State.Players[acting].Name,room.Started&&seat==acting&&e.State.Phase!=Phase.Finished,Views.For(e,seat),e.Catalog.Rules,e.State.Phase==Phase.Finished?e.Scores():[]);
    }
    public OnlineSnapshot Command(string code,string token,NetworkCommand command)
    {
        lock(gates.GetOrAdd(PathFor(code),_=>new()))
        {
            var room=Load(code);int seat=Authenticate(room,token);
            Engine.Require(command.RequestId.Length is >=16 and <=64,"Identifiant de requête invalide.");
            if(room.Requests.TryGetValue(seat,out var ids)&&ids.Contains(command.RequestId))return Snapshot(room,seat);
            if(command.ExpectedRevision!=room.Revision)throw new RevisionException("La partie a évolué. Actualisez avant de réessayer.");
            Engine.Require(room.Started,"En attente des autres joueurs.");
            var e=new Engine(room.Save.Catalog,room.Save.State);
            Engine.Require(Driver.ActingSeat(e)==seat,"Ce n'est pas votre tour ou votre fenêtre de réaction.");
            switch(command.Kind)
            {
                case "explore":e.Explore(command.Room,command.First,command.Second,command.DeclareObjects,command.DeferPlacement);break;
                case "placement":e.PlaceExploration(command.DeclareObjects);break;
                case "skip-exploration":e.SkipImpossibleExploration();break;
                case "play":e.Play(new(command.Card,command.Target,command.Objects,command.Replay));break;
                case "sell":e.Sell(command.Card);break;
                case "lock":e.InstallLock(command.Card);break;
                case "lockpick":Engine.Require(command.Target.HasValue,"Cible requise.");e.UseLockpick(command.Card,command.Target!.Value);break;
                case "react":e.React(command.PassReaction?null:command.Card);break;
                case "burglary":e.ChooseBurglary(command.Card);break;
                case "end":e.EndTurn();break;
                default:throw new RuleException("Commande inconnue.");
            }
            Driver.UntilHuman(e);e.AssertConservation();
            room.Revision++;if(!room.Requests.ContainsKey(seat))room.Requests[seat]=[];
            room.Requests[seat].Add(command.RequestId);if(room.Requests[seat].Count>100)room.Requests[seat].RemoveAt(0);
            // The loaded room is isolated. A failed command never reaches disk.
            Save(room);return Snapshot(room,seat);
        }
    }
}
