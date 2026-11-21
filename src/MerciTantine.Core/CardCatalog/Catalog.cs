using System.Text.Json;
using MerciTantine.GameCore;

namespace MerciTantine.CardCatalog;

public sealed class Catalog
{
    public List<CardDefinition> Cards { get; set; } = [];
    public List<Distribution> Distribution { get; set; } = [];
    public List<RoomDefinition> Rooms { get; set; } = [];
    public Rules Rules { get; set; } = new();
    public CardDefinition this[string model] => Cards.Single(c => c.Id == model);
    public static Catalog Load(string directory)
    {
        T Read<T>(string name) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(directory, name)), JsonFormat.Options)!;
        var catalog = new Catalog { Cards = Read<List<CardDefinition>>("cards.json"), Distribution = Read<List<Distribution>>("distribution.json"), Rooms = Read<List<RoomDefinition>>("rooms.json"), Rules = Read<Rules>("rules.json") };
        catalog.Validate(); return catalog;
    }
    public void Validate()
    {
        if(Rules.AiPolicy is not ("classic" or "varied-v1")||Rules.DeclarationReward is <0 or >1)throw new RuleException("Politique IA ou prime de déclaration invalide.");
        if (Rules.Rounds <= 0 || Rules.InitialCash < 0 || Rules.MinTax < 0 || Rules.MaxTax > 100 || Rules.MinTax > Rules.InitialTax || Rules.InitialTax > Rules.MaxTax || Rules.SalesPerTurn <= 0) throw new RuleException("Paramètres de règles invalides.");
        string[] effects = ["", "lock", "lockpick", "declare", "tax", "audit", "swiss", "claim", "skip", "insurance", "replay", "swap", "steal", "burglary", "bodyguard", "dog", "broker", "lawyer", "pickpocket", "cash"];
        if (Cards.Any(c => !effects.Contains(c.Effect) || c.Cost < 0 || (c.Immediate && (c.Category == Category.Object || c.Effect != "cash")))) throw new RuleException("Effet de catalogue invalide ou non pris en charge.");
        if (Rooms.Any(r => r.Secondary == Category.Object || r.Weight <= 0)) throw new RuleException("Catégorie secondaire ou pondération de pièce invalide.");
        if (Cards.Select(c => c.Id).Distinct().Count() != Cards.Count || Rooms.Count != 6 || Rooms.Select(r => r.Id).Distinct().Count() != 6) throw new RuleException("Identifiants de catalogue invalides.");
        if (Distribution.Any(d => d.Copies < 0 || !Cards.Any(c => c.Id == d.Model))) throw new RuleException("Distribution invalide.");
        foreach (var d in Distribution.Where(d => this[d.Model].Category == Category.Object))
            if (!Rooms.Any(r => r.Id == d.Room)) throw new RuleException("Pièce d'origine inconnue.");
        foreach (var (category, count) in new[] { (Category.Object, 100), (Category.Paper, 64), (Category.Ally, 36) })
            if (Distribution.Where(d => this[d.Model].Category == category).Sum(d => d.Copies) != count) throw new RuleException($"Le catalogue doit contenir {count} cartes {category}.");
        if (Rules.Exhaustion != "RejectChoice" || Rules.SpecialPlacement != "HiddenThenActivate" || Rules.ReactionPriority != "TargetChoosesOne" || Rules.NotaryPriority != "RotatingSeatOrder") throw new RuleException("Politique non prise en charge : consulter DECISIONS_A_VALIDER.md.");
        if (Rules.TaxRounding is not ("Floor" or "Ceiling" or "Nearest")) throw new RuleException("Arrondi inconnu.");
    }
    public List<CardInstance> CreateInstances()
    {
        var result = new List<CardInstance>();
        foreach (var d in Distribution)
        {
            var def = this[d.Model];
            var rooms = Rooms.Where(r => def.Category == Category.Object ? r.Id == d.Room : r.Secondary == def.Category).ToList();
            var allocations = rooms.ToDictionary(r => r.Id, _ => 0);
            // Weighted fair allocation; copies are physical, never duplicated across decks.
            for (int i = 0; i < d.Copies; i++)
            {
                var room = rooms.OrderBy(r => (allocations[r.Id] + 1d) / r.Weight).ThenBy(r => Rooms.IndexOf(r)).First();
                allocations[room.Id]++;
                result.Add(new(result.Count, d.Model, room.Id));
            }
        }
        return result;
    }
}
