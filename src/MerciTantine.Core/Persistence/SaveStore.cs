using System.Text.Json;
using MerciTantine.CardCatalog;
using MerciTantine.GameCore;
using MerciTantine.GameEngine;

namespace MerciTantine.Persistence;

public sealed record SaveEnvelope(int Version, Catalog Catalog, GameState State);
public static class SaveStore
{
    public static string Serialize(Engine engine) => JsonSerializer.Serialize(new SaveEnvelope(1,engine.Catalog,engine.State),JsonFormat.Options);
    public static Engine Deserialize(string json)
    {
        var save=JsonSerializer.Deserialize<SaveEnvelope>(json,JsonFormat.Options) ?? throw new RuleException("Sauvegarde vide.");
        Engine.Require(save.Version==1 && save.State.Schema==1,"Version de sauvegarde incompatible."); save.Catalog.Validate();
        var engine=new Engine(save.Catalog,save.State); engine.AssertConservation(); return engine;
    }
    public static void Write(string path, Engine engine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp"; File.WriteAllText(temp,Serialize(engine)); File.Move(temp,path,true);
    }
    public static Engine Read(string path) => Deserialize(File.ReadAllText(path));
}
