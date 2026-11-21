using MerciTantine.CardCatalog;
using MerciTantine.GameCore;

namespace MerciTantine.GameEngine;

public sealed class Engine
{
    public Catalog Catalog { get; }
    public GameState State { get; }
    public PlayerState Active => State.Players[State.Current];
    public CardDefinition Def(int id) => Catalog[State.Cards[id].Model];
    public Engine(Catalog catalog, GameState state) { Catalog = catalog; State = state; }
    public static Engine New(Catalog catalog, IEnumerable<PlayerSetup> setup, ulong seed, bool simulation = false)
    {
        var players = setup.ToList();
        Require(players.Count is >= 2 and <= 6 && (simulation || players.Any(p => p.Human)), "Il faut 2 à 6 joueurs, dont au moins un humain.");
        var s = new GameState { RandomState = seed == 0 ? 1UL : seed, Cards = catalog.CreateInstances(), Players = players.Select(p => new PlayerState { Name = p.Name, Human = p.Human, Difficulty = p.Difficulty, Tax = catalog.Rules.InitialTax, Cash = catalog.Rules.InitialCash }).ToList() };
        for(int i=0;i<s.Players.Count;i++){s.Players[i].AiProfile=players[i].AiProfile;var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"ai/{seed}/{i}"));s.Players[i].DecisionSeed=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(hash);}
        if(catalog.Rules.RandomRoomDecks)
        {
            foreach(var category in Enum.GetValues<Category>())
            {
                var destinations=catalog.Rooms.Where(r=>category==Category.Object||r.Secondary==category).Select(r=>r.Id).ToList();
                var pool=s.Cards.Where(c=>catalog[c.Model].Category==category).Select(c=>c.Id).ToList();
                RandomSource.Shuffle(s,destinations);RandomSource.Shuffle(s,pool);
                for(int i=0;i<pool.Count;i++){int id=pool[i];s.Cards[id]=s.Cards[id] with {Origin=destinations[i%destinations.Count]};}
            }
        }
        foreach (var r in catalog.Rooms)
        {
            var room = new RoomState { Id = r.Id, Objects = s.Cards.Where(c => c.Origin == r.Id && catalog[c.Model].Category == Category.Object).Select(c => c.Id).ToList(), Secondary = s.Cards.Where(c => c.Origin == r.Id && catalog[c.Model].Category != Category.Object).Select(c => c.Id).ToList() };
            RandomSource.Shuffle(s, room.Objects); RandomSource.Shuffle(s, room.Secondary); s.Rooms.Add(room);
        }
        var engine = new Engine(catalog, s); engine.Log("La famille entre dans le manoir."); return engine;
    }
    public static void Require(bool condition, string message) { if (!condition) throw new RuleException(message); }
    private void Log(string message, int? privateTo = null) => State.Events.Add(new(State.Round, message, privateTo));
    private void RequirePhase(params Phase[] phases) => Require(phases.Contains(State.Phase), "Action indisponible dans cette phase.");
    public bool CanExplore(RoomState room, Category first, Category second)
    {
        var secondary = Catalog.Rooms.Single(r => r.Id == room.Id).Secondary;
        var choices = new[] { first, second };
        return room.Occupant == null && choices.All(c => c == Category.Object || c == secondary) && choices.Count(c => c == Category.Object) <= room.Objects.Count && choices.Count(c => c == secondary) <= room.Secondary.Count;
    }
    public List<(string Room, Category First, Category Second)> ExplorationChoices()
    {
        var choices = new List<(string, Category, Category)>();
        foreach (var room in State.Rooms)
        {
            var c = Catalog.Rooms.Single(r => r.Id == room.Id).Secondary;
            foreach (var (a,b) in new[] { (Category.Object, Category.Object), (Category.Object, c), (c,c) })
                if (CanExplore(room,a,b)) choices.Add((room.Id,a,b));
        }
        return choices;
    }
    public int[] Explore(string roomId, Category first, Category second, bool declareObjects = false, bool deferPlacement = false)
    {
        RequirePhase(Phase.Explore);
        var room = State.Rooms.SingleOrDefault(r => r.Id == roomId);
        Require(room != null && CanExplore(room, first, second), "Pièce occupée ou combinaison de pioches épuisée : choisissez une autre combinaison.");
        Require(!deferPlacement || !declareObjects, "Le placement différé commence escamoté.");
        var drawn = new List<int>(); room!.Occupant = State.Current;
        foreach (var category in new[] { first, second })
        {
            var pile = category == Category.Object ? room.Objects : room.Secondary;
            int id = pile[0]; pile.RemoveAt(0); drawn.Add(id);
            var def = Def(id);
            if (def.Category == Category.Object)
            {
                (declareObjects ? Active.Declared : Active.Hidden).Add(id);
                Active.AcquiredThisTurn.Add(id);
                if (!State.DrawnValues.ContainsKey(roomId)) State.DrawnValues[roomId] = [];
                State.DrawnValues[roomId].Add(def.Value);
            }
            else Active.Hand.Add(id);
            Log($"Vous trouvez {def.Name} ({def.Value:+#;-#;0}).", State.Current);
            if (def.Immediate)
            {
                Require(def.Effect == "cash", "Effet immédiat inconnu dans le catalogue.");
                Active.Hand.Remove(id); Active.Discard.Add(id); Active.Cash += def.Amount;
            }
        }
        Log($"{Active.Name} visite {Catalog.Rooms.Single(r => r.Id == roomId).Name} et prend deux cartes.");
        if (declareObjects) Log($"Objets déclarés : {string.Join(", ", drawn.Where(i => Def(i).Category == Category.Object).Select(i => Def(i).Name))}.");
        State.ExplorationCards = deferPlacement ? drawn : [];
        State.Phase = deferPlacement ? Phase.Placement : Phase.Actions;
        if (!deferPlacement) ActivateAutomaticProtections();
        if(declareObjects)RewardDeclaration(State.Current,drawn);
        return drawn.ToArray();
    }
    // Reveal one physical card immediately; the second choice remains in the same room.
    public int DrawExploration(string roomId, Category category)
    {
        RequirePhase(Phase.Explore, Phase.Placement);
        var room = State.Rooms.SingleOrDefault(r => r.Id == roomId);
        Require(room != null, "Pièce inconnue.");
        var secondary = Catalog.Rooms.Single(r => r.Id == roomId).Secondary;
        Require(category == Category.Object || category == secondary, "Catégorie absente de cette pièce.");
        bool first = State.Phase == Phase.Explore;
        Require(first ? room!.Occupant == null && room.Objects.Count + room.Secondary.Count >= 2 : room!.Occupant == State.Current && State.ExplorationCards.Count == 1,
            "Terminez les deux pioches dans la même pièce.");
        var pile = category == Category.Object ? room!.Objects : room!.Secondary;
        Require(pile.Count > 0, "Cette pioche est épuisée.");
        if (first) { State.ExplorationCards.Clear(); room!.Occupant = State.Current; }
        int id = pile[0]; pile.RemoveAt(0); var def = Def(id);
        if (def.Category == Category.Object)
        {
            Active.Hidden.Add(id); Active.AcquiredThisTurn.Add(id);
            if (!State.DrawnValues.ContainsKey(roomId)) State.DrawnValues[roomId] = [];
            State.DrawnValues[roomId].Add(def.Value);
        }
        else Active.Hand.Add(id);
        if (def.Immediate) { Active.Hand.Remove(id); Active.Discard.Add(id); Active.Cash += def.Amount; }
        State.ExplorationCards.Add(id); State.Phase = Phase.Placement;
        Log($"Vous trouvez {def.Name} ({def.Value:+#;-#;0}).", State.Current);
        if (State.ExplorationCards.Count == 2) Log($"{Active.Name} visite {Catalog.Rooms.Single(r => r.Id == roomId).Name} et prend deux cartes.");
        return id;
    }
    public void PlaceExploration(bool declareObjects)
    {
        RequirePhase(Phase.Placement);
        Require(State.ExplorationCards.Count == 2, "Piochez votre deuxième carte avant de conserver les objets.");
        if (declareObjects)
        {
            foreach (int id in State.ExplorationCards.Where(i => Def(i).Category == Category.Object && Def(i).Effect != "lock")) Move(id, Active.Hidden, Active.Declared);
            Log($"{Active.Name} déclare les objets trouvés : {string.Join(", ", State.ExplorationCards.Where(i => Def(i).Category == Category.Object && Def(i).Effect != "lock").Select(i => Def(i).Name))}.");
            RewardDeclaration(State.Current,State.ExplorationCards);
        }
        State.ExplorationCards = []; State.Phase = Phase.Actions;
        ActivateAutomaticProtections();
    }
    private void RewardDeclaration(int seat,IEnumerable<int> declared)
    {
        var player=State.Players[seat];
        if(Catalog.Rules.DeclarationReward<=0||State.Phase==Phase.Notary||player.DeclarationRewardRound==State.Round||!declared.Any(id=>Def(id).Category==Category.Object&&Def(id).Value>0))return;
        player.Cash+=Catalog.Rules.DeclarationReward;player.DeclarationRewardRound=State.Round;
        Log($"{player.Name} reçoit {Catalog.Rules.DeclarationReward} pièce de prime de déclaration.");
    }
    public void ActivateAutomaticProtections()
    {
        // A discovery awaiting placement must remain unchanged until both cards are kept.
        if (State.Phase == Phase.Placement) return;
        foreach (var player in State.Players)
        foreach (var zone in new[] { player.Hidden, player.Declared })
        foreach (int id in zone.Where(i => Def(i).Effect == "lock").ToArray())
        {
            Move(id, zone, player.Protections);
            Log($"{player.Name} installe automatiquement une Serrure renforcée.");
        }
    }
    public void SkipImpossibleExploration()
    {
        RequirePhase(Phase.Explore); Require(ExplorationChoices().Count == 0, "Une exploration est possible.");
        Log("Aucune combinaison de deux cartes disponible : exploration passée sans créer de carte."); State.Phase = Phase.Actions;
    }
    private static void Move(int id, List<int> from, List<int> to) { Require(from.Remove(id), "Objet absent de la zone attendue."); to.Add(id); }
    private void Tax(PlayerState player, int delta) { player.Tax = Math.Clamp(player.Tax + delta, Catalog.Rules.MinTax, Catalog.Rules.MaxTax); State.TaxEffects++; }
    public void Sell(int id)
    {
        RequirePhase(Phase.Actions); ValidateSale(id);
        CompleteSale(id, Catalog.Rules.NormalSalePercent);
    }
    private void ValidateSale(int id)
    {
        Require(Active.Hidden.Contains(id) && Def(id).Value > 0 && Def(id).Effect == "", "Seul un objet positif ordinaire escamoté peut être vendu.");
        Require(Active.Sales < Catalog.Rules.SalesPerTurn, "Limite de deux ventes par tour atteinte.");
        Require(Catalog.Rules.SellAcquiredSameTurn || !Active.AcquiredThisTurn.Contains(id), "Vente interdite le tour de l'acquisition.");
    }
    private void CompleteSale(int id, int percent)
    {
        int gain = Def(id).Value * percent / 100; Move(id, Active.Hidden, State.Sold); Active.Cash += gain; Active.Sales++;
        Log($"{Active.Name} vend {Def(id).Name} pour {gain} pièces.");
    }
    public void InstallLock(int id)
    {
        RequirePhase(Phase.Actions); Require(Active.Hidden.Contains(id) || Active.Declared.Contains(id), "Serrure inaccessible.");
        Require(Def(id).Effect == "lock", "Ce n'est pas une serrure.");
        Move(id, Active.Hidden.Contains(id) ? Active.Hidden : Active.Declared, Active.Protections); Log($"{Active.Name} installe une Serrure renforcée.");
    }
    public void UseLockpick(int id, int target)
    {
        RequirePhase(Phase.Actions); Require(target >= 0 && target < State.Players.Count && target != State.Current, "Cible invalide.");
        Require((Active.Hidden.Contains(id) || Active.Declared.Contains(id)) && Def(id).Effect == "lockpick", "Rossignol inaccessible.");
        var victim = State.Players[target]; Require(victim.Protections.Count > 0, "Aucune serrure à détruire.");
        Move(id, Active.Hidden.Contains(id) ? Active.Hidden : Active.Declared, State.SpecialDiscard);
        Move(victim.Protections[0], victim.Protections, State.SpecialDiscard); Log($"{Active.Name} détruit une serrure de {victim.Name}.");
    }
    public void Play(PlayCommand command)
    {
        RequirePhase(Phase.Actions, Phase.Notary);
        Require(Active.Hand.Contains(command.Card), "Carte absente de votre main.");
        int effective = command.Card;
        if (Def(effective).Effect == "replay")
        {
            Require(command.Replay.HasValue && Active.Discard.Contains(command.Replay.Value), "Choisissez un Papier de votre défausse.");
            effective = command.Replay!.Value;
            Require(Def(effective).Category == Category.Paper && Def(effective).Effect != "replay" && !Def(effective).Reaction, "Ce Papier ne peut pas être rejoué ainsi.");
        }
        var def = Def(effective); var objects = command.Objects ?? [];
        Require(!def.Reaction && def.Category != Category.Object, "Cette carte se joue uniquement dans son contexte.");
        Require(State.Phase != Phase.Notary || (def.Effect == "claim" && def.Category == Category.Paper && effective == command.Card), "Seuls les Testaments authentiques sont admis chez le notaire.");
        int target = def.Hostile ? command.Target ?? -1 : State.Current;
        Require(target >= 0 && target < State.Players.Count && (!def.Hostile || target != State.Current), "Choisissez un adversaire.");
        Require(Active.Cash >= def.Cost, "Trésorerie insuffisante.");
        Require(objects.Distinct().Count() == objects.Length, "Un même objet ne peut pas être sélectionné deux fois.");
        var victim = State.Players[target];
        switch (def.Effect)
        {
            case "declare": Require(objects.All(Active.Hidden.Contains), "Objet non escamoté."); break;
            case "swiss":
                Require(objects.Length >= 1 && objects.Length <= def.Amount && objects.All(i => Active.Hidden.Contains(i) || Active.Declared.Contains(i)), "Sélection suisse invalide.");
                Require(Catalog.Rules.AllowNegativeSwiss || objects.All(i => Def(i).Value >= 0), "Les objets négatifs ne peuvent pas rejoindre la Suisse."); break;
            case "claim": Require(objects.Length == 1 && victim.Declared.Contains(objects[0]) && (def.TargetModel == "" || Def(objects[0]).Id == def.TargetModel), "Aucun objet déclaré compatible."); break;
            case "steal": case "burglary":
                Require(victim.Hidden.Count > 0, "La pile adverse est vide.");
                Require(victim.Protections.Count == 0 || (def.Effect == "burglary" && !Catalog.Rules.LockBlocksBurglary), "Une serrure bloque le chapardage."); break;
            case "swap": Require(objects.Length == 1 && Active.Hidden.Contains(objects[0]) && victim.Hidden.Count > 0, "Échange impossible."); break;
            case "pickpocket": Require(victim.Hand.Count > 0, "La main adverse est vide."); break;
            case "broker": Require(objects.Length == 1, "Choisissez un objet à vendre."); ValidateSale(objects[0]); break;
            case "tax": case "audit": case "skip": break;
            default: throw new RuleException("Effet inconnu ou non jouable.");
        }
        // All validation precedes payment, consumption and RNG advancement.
        Active.Cash -= def.Cost;
        Move(command.Card, Active.Hand, Active.Discard);
        if (effective != command.Card) Move(effective, Active.Discard, State.Removed);
        var pending = new PendingAction { Actor = State.Current, Target = target, Card = effective, Objects = objects.ToList(), Paid = def.Cost, ReturnPhase = State.Phase };
        State.Pending = pending;
        if (State.Phase == Phase.Notary) State.NotaryPasses = 0;
        if (def.Effect is "steal" or "burglary" or "pickpocket") State.TheftAttempts++;
        Log($"{Active.Name} joue {def.Name}{(def.Hostile ? $" contre {victim.Name}" : "")}.");
        if (def.Hostile && ReactionCards().Count > 0) State.Phase = Phase.Reaction;
        else ResolvePending();
    }
    public List<int> ReactionCards()
    {
        if (State.Pending is not { } p) return [];
        var attack = Def(p.Card); var defender = State.Players[p.Target];
        return defender.Hand.Where(i =>
        {
            var d = Def(i); if (!d.Reaction || d.Cost > defender.Cash) return false;
            return d.Effect switch
            {
                "insurance" => attack.Hostile && attack.Category == Category.Paper,
                "bodyguard" => attack.Hostile,
                "dog" => attack.Effect is "steal" or "burglary" or "pickpocket",
                "lawyer" => attack.Effect == "claim" && p.ReturnPhase != Phase.Notary,
                _ => false
            };
        }).ToList();
    }
    public void React(int? card)
    {
        RequirePhase(Phase.Reaction); var p = State.Pending!;
        if (card == null) { ResolvePending(); return; }
        Require(ReactionCards().Contains(card.Value), "Réaction invalide.");
        var defender = State.Players[p.Target]; var def = Def(card.Value);
        defender.Cash -= def.Cost; Move(card.Value, defender.Hand, defender.Discard);
        if (def.Effect == "dog") State.Players[p.Actor].Cash += (p.Paid + 1) / 2;
        Log($"{defender.Name} joue {def.Name} : action annulée.");
        State.Phase = p.ReturnPhase; State.Pending = null;
        ActivateAutomaticProtections();
    }
    private void ResolvePending()
    {
        var p = State.Pending!; var actor = State.Players[p.Actor]; var victim = State.Players[p.Target]; var def = Def(p.Card);
        int RandomHidden() => victim.Hidden[RandomSource.Next(State, victim.Hidden.Count)];
        switch (def.Effect)
        {
            case "declare": foreach (int id in p.Objects) Move(id, actor.Hidden, actor.Declared); State.Declarations++; RewardDeclaration(p.Actor,p.Objects);break;
            case "tax": Tax(victim, def.Amount); break;
            case "audit": foreach (int id in victim.Hidden.ToArray()) Move(id, victim.Hidden, victim.Declared); Tax(victim, def.Amount); break;
            case "swiss": foreach (int id in p.Objects) Move(id, actor.Hidden.Contains(id) ? actor.Hidden : actor.Declared, actor.Swiss); break;
            case "claim": Move(p.Objects[0], victim.Declared, actor.Declared); break;
            case "skip": victim.Skip = Catalog.Rules.StackSkip ? victim.Skip + def.Amount : Math.Max(victim.Skip, def.Amount); break;
            case "steal": int stolen = RandomHidden(); Move(stolen, victim.Hidden, actor.Hidden); actor.AcquiredThisTurn.Add(stolen); State.Thefts++; Log($"Objet volé : {Def(stolen).Name}.", p.Actor); break;
            case "burglary":
                p.Candidates = victim.Hidden.ToList(); RandomSource.Shuffle(State, p.Candidates); p.Candidates = p.Candidates.Take(3).ToList();
                State.Phase = Phase.BurglaryChoice; Log($"Candidats : {string.Join(", ", p.Candidates.Select(i => Def(i).Name))}.", p.Actor); return;
            case "swap": int other = RandomHidden(); Move(p.Objects[0], actor.Hidden, victim.Hidden); Move(other, victim.Hidden, actor.Hidden); actor.AcquiredThisTurn.Add(other); Log($"Objet reçu : {Def(other).Name}.", p.Actor); Log($"Objet reçu : {Def(p.Objects[0]).Name}.", p.Target); break;
            case "pickpocket": var papers = victim.Hand.Where(i => Def(i).Category == Category.Paper).ToList(); if (papers.Count > 0) { int paper = papers[RandomSource.Next(State, papers.Count)]; Move(paper, victim.Hand, actor.Hand); State.Thefts++; Log($"Papier volé : {Def(paper).Name}.", p.Actor); } break;
            case "broker": CompleteSale(p.Objects[0], Catalog.Rules.BrokerSalePercent); break;
        }
        if (def.Effect is "declare" or "audit" or "claim") Log($"Patrimoine déclaré de {victim.Name} : {string.Join(", ", victim.Declared.Select(i => Def(i).Name))}.");
        State.Phase = p.ReturnPhase; State.Pending = null;
        ActivateAutomaticProtections();
    }
    public void ChooseBurglary(int id)
    {
        RequirePhase(Phase.BurglaryChoice); var p = State.Pending!;
        Require(p.Candidates.Contains(id), "Choisissez l'un des objets révélés.");
        Move(id, State.Players[p.Target].Hidden, State.Players[p.Actor].Hidden); State.Players[p.Actor].AcquiredThisTurn.Add(id); State.Thefts++;
        Log($"Vous emportez {Def(id).Name}.", p.Actor); State.Phase = p.ReturnPhase; State.Pending = null; ActivateAutomaticProtections();
    }
    public void EndTurn()
    {
        RequirePhase(Phase.Actions, Phase.Notary);
        if (State.Phase == Phase.Notary)
        {
            State.NotaryPasses++;
            if (State.NotaryPasses >= State.Players.Count) { State.Phase = Phase.Finished; Log("Le notaire ferme le dossier."); return; }
            State.Current = (State.Current + 1) % State.Players.Count; return;
        }
        Advance();
        // Skipped turns count toward ten turns, never explore or act.
        while (State.Phase == Phase.Explore && Active.Skip > 0)
        {
            Active.Skip--; Log($"{Active.Name} passe intégralement son tour."); Advance();
        }
    }
    private void Advance()
    {
        State.TurnIndex++;
        if (State.TurnIndex == State.Players.Count)
        {
            State.TurnIndex = 0; State.Round++;
            if (State.Round > Catalog.Rules.Rounds)
            {
                State.Round = Catalog.Rules.Rounds; State.Phase = Phase.Notary; State.Current = Catalog.Rules.Rounds % State.Players.Count;
                State.NotaryPasses = 0; Log("Ouverture du testament : seuls les Testaments authentiques restent jouables."); return;
            }
            foreach (var room in State.Rooms) room.Occupant = null;
        }
        State.Current = ((State.Round - 1) + State.TurnIndex) % State.Players.Count;
        Active.Sales = 0; Active.AcquiredThisTurn.Clear(); State.Phase = Phase.Explore;
    }
    public void NotaryActed() { RequirePhase(Phase.Notary, Phase.Reaction); State.NotaryPasses = 0; }
    public List<Score> Scores()
    {
        Require(State.Phase == Phase.Finished, "Le classement sera disponible après le notaire.");
        return State.Players.Select((p, i) =>
        {
            int declared = p.Declared.Sum(id => Def(id).Value);
            decimal raw = (Catalog.Rules.TaxNegativeBase ? declared : Math.Max(0, declared)) * p.Tax / 100m;
            int tax = (int)(Catalog.Rules.TaxRounding switch { "Ceiling" => Math.Ceiling(raw), "Nearest" => Math.Round(raw, 0, MidpointRounding.AwayFromZero), _ => Math.Floor(raw) });
            int hidden = p.Hidden.Sum(id => Def(id).Value), swiss = p.Swiss.Sum(id => Def(id).Value);
            return new Score(i,p.Name,declared,tax,hidden,swiss,p.Cash,p.Penalties,declared-tax+hidden+swiss+p.Cash-p.Penalties);
        }).OrderByDescending(p => p.Fortune).ThenBy(p => p.Seat).ToList();
    }
    public void AssertConservation()
    {
        var ids = new List<int>();
        foreach (var p in State.Players) { ids.AddRange(p.Hand); ids.AddRange(p.Hidden); ids.AddRange(p.Declared); ids.AddRange(p.Swiss); ids.AddRange(p.Protections); ids.AddRange(p.Discard); }
        foreach (var r in State.Rooms) { ids.AddRange(r.Objects); ids.AddRange(r.Secondary); }
        ids.AddRange(State.Removed); ids.AddRange(State.Sold); ids.AddRange(State.SpecialDiscard);
        Require(ids.Count == State.Cards.Count && ids.Distinct().Count() == ids.Count && ids.All(i => i >= 0 && i < State.Cards.Count), "Conservation des exemplaires violée.");
    }
}
