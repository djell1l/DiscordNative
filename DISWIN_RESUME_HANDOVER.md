# 🚀 Diswin — Dossier de Reprise de Contexte & Handover

> **Archivage & Continuité de Projet**  
> **Source de discussion d'origine :** `422b6dd7-eb1c-4b08-96fb-735631f837df`  
> **Auteur / Lead :** djell1l  
> **Dépôt Git :** `https://github.com/djell1l/Diswin.git`  
> **Emplacement local :** `c:\Users\djell\Downloads\DiscordNative\`

---

## 1. Vue d'Ensemble & Stack Technique

Diswin est un client Discord alternatif de classe industrielle, conçu à 100% en **C# (.NET 8)** et **WinUI 3** natif (Windows App SDK), sans aucun wrapper web (0% Electron, 0% Chromium, 0% CEF).

- **Architecture Réseau Autonome** :
  - Client WebSocket Gateway Discord v9/v10 avec reconnexion automatique, heartbeat précis et compression zlib.
  - Client REST natif avec pagination, cache et gestion des rate-limits.
  - Serveur Discord Local RPC (IPC Named Pipes `\\.\pipe\discord-ipc-0`) intégré pour la détection automatique des jeux et applications (Spotify, VS Code, Steam).
- **Audio & Vocal Professionnel** :
  - Pipeline vocal complet (RTP/UDP, chiffrement XSalsa20_Poly1305 / AEAD).
  - Suppression de bruit neuronale officielle **Krisp** (DSP 48kHz avec chargement direct des modèles profonds officiels Discord sans abonnement Nitro).
  - Partage d'écran **Go Live** haute fidélité (1080p 60 FPS via DXGI Desktop Duplication + capture audio loopback système).
- **Interface & Expérience Utilisateur** :
  - Rendu WinUI 3 Fluent complet : Mica backdrop, géométrie squircle, synchronisation automatique avec le thème Windows (Dark/Light).
  - Multi-langue complet avec sélection automatique selon la langue du système Windows et sélecteur dans les paramètres.
  - Cartes de profil personnalisées avec dégradés de couleurs Nitro réels de l'utilisateur, pills de clan (`[🐱 illit]`), compteurs d'amis/serveurs mutuels et bulles de statut.
  - Affichage direct des membres connectés dans chaque salon vocal dans la barre latérale.

---

## 2. Dernières Évolutions Clés (Release v0.4.1)

1. **Correction Complète du Login Mobile & QR Auth v2** :
   - Élimination de l'erreur Cloudflare HTTP 400 `Invalid request origin` (code 50067) causée par l'injection de doublons dans les en-têtes `Origin` et `Referer`.
   - Maintien du WebSocket avec `KeepAliveInterval = TimeSpan.FromSeconds(20)` pour éviter que l'application mobile affiche *"Ordinateur introuvable !"*.
   - Re-génération automatique du QR Code lors de l'expiration.
2. **Affichage Temps Réel du Rich Presence (Spotify & Jeux)** :
   - Ajout d'un ticker temps réel `DispatcherTimer` (1s) : les timestamps ne restent plus bloqués à "00:02", ils défilent en direct (`00:12 / 03:45` pour Spotify).
   - Suppression du formulaire de saisie manuelle de RPC selon la consigne du projet.
3. **Membres Vocaux dans la Liste des Salons** :
   - Les utilisateurs assis dans les salons vocaux s'affichent sous le salon avec leur photo, leur nom, leurs icônes micro/sourdine et le badge rouge `LIVE`.
   - Traitement des `voice_states` dès l'événement `GUILD_CREATE`.
4. **Paramètres, Langues & Thème Système** :
   - Thème configuré sur `Default` (suit le thème sombre ou clair de Windows).
   - Langue configurée sur `Default` (suit la culture Windows de l'utilisateur), avec possibilité de bascule manuelle dans les Paramètres.
   - Nettoyage des dossiers de langues inutiles (`SatelliteResourceLanguages`) pour éliminer les 70+ sous-dossiers parasites signalés par les testeurs.
5. **CI GitHub Actions & Packageur Robuste** :
   - Détection dynamique de MSBuild et de Roslyn via `vswhere.exe` pour supporter aussi bien les runners GitHub Actions Enterprise que les machines de dev locales.
   - Création automatique du Setup WinUI 3 single-file autonome (`Diswin-Setup-v0.4.1-win-x64.exe`) et du portable (`Diswin-v0.4.1-win-x64-portable.zip`).

---

## 3. Reprendre dans une Nouvelle Discussion

Dans une nouvelle discussion dédiée à **Diswin**, envoyez simplement :
```
Reprends le projet Diswin en te basant sur c:\Users\djell\Downloads\DiscordNative\DISWIN_RESUME_HANDOVER.md
```
L'assistant sera instantanément synchronisé avec l'état exact du projet.
