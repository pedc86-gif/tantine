# Mode en ligne et Render

Le dépôt serveur est [pedc86-gif/tantine](https://github.com/pedc86-gif/tantine). Il contient le serveur ASP.NET Core, le moteur commun, les données de cartes et les tests. Le déploiement sur Render est à créer par le propriétaire du compte.

Le protocole en ligne est utilisé par le client MAUI Android historique. L’interface Godot PC actuelle reste locale et ne possède pas encore ce parcours réseau. Ce dépôt est un serveur HTTP, sans interface de jeu dans le navigateur. Aucune adresse Render provisoire n’est définie dans les clients : l’adresse réelle sera intégrée après création du service.

## Déploiement

1. Utiliser le dépôt `https://github.com/pedc86-gif/tantine` et sa branche `main`.
2. Dans Render, créer un Blueprint depuis ce dépôt, en utilisant `render.yaml`.
3. Vérifier le plan `starter` et le disque `parties` avant de créer le service. Ce choix peut être payant. Le disque est nécessaire pour conserver les parties et les jetons après redémarrage.
4. Attendre la construction du Dockerfile et tester `https://VOTRE-SERVICE.onrender.com/health`.
5. Sur Android, ouvrir « Paramètres du serveur » et saisir cette URL HTTPS, sans `/health`.
6. Créer une salle et communiquer son code à huit caractères aux autres joueurs. Ils choisissent « Rejoindre par code ». La partie commence quand tous les sièges humains sont attribués.

Le conteneur écoute `PORT`, avec 10000 par défaut. Les fichiers persistants sont sous `/var/data/merci-tantine`. Le déploiement reste à une instance : les verrous de commandes sont locaux au processus. Une évolution multi-instance devra remplacer les fichiers par une base transactionnelle et des verrous distribués.

Pour une création manuelle « Web Service » : sélectionner le dépôt, la branche `main`, le runtime **Docker**, le Dockerfile `./Dockerfile` et le contexte racine `.` ; laisser les commandes de construction/démarrage par défaut. Configurer la santé sur `/health`, `SAVE_DIRECTORY=/var/data/merci-tantine`, et un disque persistant de 1 Go monté sur `/var/data`, avec une seule instance. Ne pas choisir « Static Site ». Le Blueprint configure déjà ces éléments.

Ces contraintes suivent la documentation officielle : [Docker sur Render](https://render.com/docs/docker), [disques persistants](https://render.com/docs/disks), [référence du Blueprint](https://render.com/docs/blueprint-spec). Un disque Render n’est accessible qu’à une instance et les écritures hors du chemin monté ne survivent pas aux déploiements.

## Protocole v1

| Route | Objet |
|---|---|
| `GET /health` | Santé, version du protocole |
| `POST /rooms` | `{name, humans, bots, difficulty}` → `{code, token, seat}` |
| `POST /rooms/{code}/join` | `{name}` → justificatif du siège attribué |
| `GET /rooms/{code}` | Projection autorisée pour le jeton Bearer |
| `POST /rooms/{code}/commands` | Commande validée, puis projection résultante |

Les enums sont des chaînes : `Object`, `Paper`, `Ally`, `Easy`, `Normal`, `Hard`. Une commande contient `requestId` unique, `expectedRevision`, `kind`, puis les arguments du contrat `NetworkCommand`. Types : `explore`, `placement`, `skip-exploration`, `play`, `sell`, `lock`, `lockpick`, `react`, `burglary`, `end`. Le serveur refuse un mauvais siège, une mauvaise phase, une cible ou carte invalide et une révision périmée (409). Il sérialise les commandes d’une salle et ignore les 100 derniers identifiants déjà traités par siège. Rejouer exactement le même identifiant retourne l’état courant sans répéter l’effet.

Les jetons aléatoires 256 bits sont propres aux sièges ; seuls leurs SHA-256 sont enregistrés côté serveur. Conserver le jeton permet la reconnexion au même siège. L’application mémorise le dernier justificatif dans SecureStorage. Elle ne propose pas encore de transfert de siège entre téléphones, de compte utilisateur, de liste publique des salles ou de récupération après effacement du stockage. Le code de salle autorise l’attribution d’une place libre : le partager uniquement aux personnes invitées.

Le client actualise toutes les deux secondes. En cas de déconnexion ou de redémarrage du service, la partie reste en attente de la même décision humaine. Les adversaires IA sont résolus côté serveur avant la prochaine attente humaine. Pas de limite de temps ni d’abandon automatique dans cette version.

Une limite de requêtes et de taille des corps est configurée. Derrière le proxy Render, la limite IP peut être partagée entre plusieurs utilisateurs car aucun proxy arbitraire n’est approuvé : ajuster cette politique pour un déploiement à grande audience. Il n’y a pas de nettoyage automatique des anciennes salles ni de haute disponibilité.

## Vérification locale

```powershell
dotnet run --project src/MerciTantine.Server --no-launch-profile --urls http://127.0.0.1:5189
# Dans un second terminal, Node 18+ :
node scripts/test-online.cjs
dotnet run --project tests/MerciTantine.Server.Tests
```

Le test HTTP couvre authentification, attribution des sièges, commandes hors tour, confidentialité, duplication, révisions, concurrence et reconnexion. Le test C# termine une partie de quatre joueurs, rechargeant le disque et répétant chaque commande après chaque action.

Les appels HTTP locaux servent aux tests du protocole. L’application Android demande HTTPS ; utiliser un serveur HTTPS accessible pour les essais sur téléphone.

## Version préparée le 21 novembre 2026

- Construction réelle de l’image Linux Docker réussie à partir des seuls fichiers de ce dépôt.
- 148 tests du moteur réussis.
- Partie réseau complète : 82 commandes, doublons ignorés et rechargement disque après chaque action.
- 20 échanges HTTP réussis contre le conteneur Linux : authentification, confidentialité, commandes concurrentes, révisions et reprise de la même salle après redémarrage réel du conteneur.
- Démarrage testé avec `PORT=18080`, différent du port de repli 10000, et volume persistant monté sur `/var/data`.

Ces vérifications sont locales. Le démarrage sur Render et l’accès HTTPS public restent à vérifier après création du service.
