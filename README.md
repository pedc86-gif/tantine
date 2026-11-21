# Merci Tantine — serveur Render

Serveur autoritaire du jeu Merci Tantine, en ASP.NET Core / .NET 10. Ce dépôt contient le moteur de règles, les cartes, le protocole réseau et leurs tests. Il n’héberge pas l’interface graphique du jeu.

## Créer le service Render

Dans Render, créer un **Blueprint** depuis ce dépôt, branche `main`. Le fichier [render.yaml](render.yaml) configure Docker, le contrôle de santé `/health`, une seule instance et le stockage persistant des parties.

Le Blueprint utilise une instance Starter et un disque de 1 Go : vérifier leur tarification dans Render avant création. Après déploiement, ouvrir `https://VOTRE-SERVICE.onrender.com/health` : la réponse attendue est `{"status":"ok","protocol":1}`. Communiquer ensuite l’URL HTTPS du service, sans `/health`, pour le paramétrage par défaut du client.

Le client Godot PC actuel joue en local ; sa connexion au serveur reste à intégrer. Le protocole existant est utilisé par le client MAUI Android historique. Aucun client graphique ni adresse de serveur fictive ne sont publiés ici.

[Configuration manuelle et protocole détaillé](docs/RENDER.md).

## Vérifier localement

Avec le SDK .NET indiqué dans `global.json` et Node 18 ou supérieur :

```sh
dotnet run --project tests/MerciTantine.Tests -c Release
dotnet run --project tests/MerciTantine.Server.Tests -c Release
docker build -t merci-tantine .
docker run -d --name tantine-test -p 10000:10000 -v tantine-test-data:/var/data merci-tantine
node scripts/test-online.cjs http://127.0.0.1:10000 tantine-test
docker stop tantine-test
docker rm tantine-test
```

Le dernier test vérifie aussi la reconnexion après redémarrage du conteneur. Le volume conserve les salles de test. Le serveur écoute `0.0.0.0:$PORT` (10000 par défaut) et enregistre les parties dans `SAVE_DIRECTORY` (`/var/data/merci-tantine` dans le conteneur). Conserver une seule instance tant que le stockage repose sur des fichiers.
