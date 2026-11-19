# Boutique BollTrack

La boutique utilise ASP.NET Core 8, Entity Framework Core et MySQL 8. Le projet Railway BollTrack contient les services `bolltrack` et `MySQL`. La base `bolltrack_shop` contient le catalogue, les stocks et les commandes. Entreli conserve les comptes, les mots de passe et les profils.

## Stock

Dans Railway, ouvrir **BollTrack → MySQL → Data → stock**. Chaque ligne correspond à une référence et une taille. `Available` est la quantité encore disponible à la vente. Mettre à jour cette colonne pour comptabiliser une vente sur stand ou un réassort. Dans `products`, `PriceCents` est le prix unitaire en centimes. Les clients voient les nouvelles valeurs en rechargeant la boutique ou en ouvrant leur panier.

L’inventaire communiqué le 19 novembre 2026 est importé dans une transaction par `shop/stock.sql`. La table `shop_migrations` empêche un redéploiement de rétablir les quantités initiales. Le T-shirt BollTrack 2025 est présent avec son prix et sans variante vendable.

## Compte Entreli

Le client public `bolltrack-boutique-web` utilise le flux OpenID Connect avec code et PKCE. Le fournisseur et le portail de profil sont ceux de StudioIdentity. Les routes `/compte/connexion` et `/compte/inscription` conservent le retour à la boutique. La session locale contient les informations signées par Entreli. Chaque commande appartient au sujet Entreli de l’acheteur. L’espace client montre les articles, les montants, le statut, le justificatif Stripe lorsqu’il est disponible et le numéro de suivi après émission de l’étiquette.

## Paiement et livraison

Renseigner les valeurs propres à l’association dans les variables du service Railway **bolltrack**. Les frais sont des montants facturés au client pour l’ensemble du panier, en centimes d’euro. Un tarif à zéro active la gratuité du mode correspondant.

| Variable | Valeur attendue |
| --- | --- |
| `Stripe__SecretKey` | Clé secrète du compte Stripe vendeur dans l’environnement choisi |
| `Stripe__WebhookSecret` | Secret de signature du webhook de ce même environnement |
| `MondialRelay__Enseigne`, `MondialRelay__ClePrivee` | Identifiants API 1 du contrat pour la recherche de Lockers |
| `MondialRelay__LockerCents` | Frais par commande en Locker |
| `MondialRelay__DomicileActif`, `MondialRelay__DomicileCents` | `true` lorsque le contrat couvre la livraison à domicile en France, et frais par commande |
| `MondialRelay__Api2Login`, `MondialRelay__Api2Password`, `MondialRelay__Api2Client` | Identifiants API 2 Mondial Relay Connect pour les étiquettes |
| `MondialRelay__Collecte`, `MondialRelay__LieuCollecte` | Mode du contrat, REL ou CCC, et code de lieu si nécessaire |
| `MondialRelay__Expediteur__Nom`, `__Rue`, `__CodePostal`, `__Ville`, `__Telephone`, `__Email` | Chaque suffixe est précédé de `MondialRelay__Expediteur`. `__Complement` est facultatif. |

Dans Stripe, créer une destination de type événements instantanés (snapshot) vers `https://bolltrack-production.up.railway.app/api/stripe/webhook`, pour `checkout.session.completed` et `checkout.session.expired`. La boutique lance Checkout sur la base des prix MySQL, affiche les frais avant paiement et revalide le Locker auprès de Mondial Relay. La France métropolitaine est la première zone de livraison. Les modes de livraison configurés apparaissent dans le panier.

Les articles sont réservés par transaction au début du paiement. Le webhook signé ou une vérification de la session auprès de Stripe confirme le règlement. Une session expirée restitue ses unités une seule fois. Un rapprochement périodique rattrape les notifications manquées. Depuis « Mon compte », le client peut reprendre ou annuler un paiement en attente.

### Préparer une expédition

Depuis le conteneur de la boutique, exécuter la commande ci-dessous pour une commande payée, avec les mesures du colis emballé. L’API 2 renvoie l’étiquette PDF et le numéro d’expédition. Le client retrouve ce numéro dans « Mon compte ». La variable `MondialRelay__Sandbox=true` sélectionne le serveur de test avec les identifiants correspondants.

```sh
dotnet /app/Shop.dll etiquette ID_COMMANDE POIDS_G LONGUEUR_CM LARGEUR_CM HAUTEUR_CM
```

La table `shipments` associe une expédition à une commande. Relancer sur un envoi confirmé restitue le lien de l’étiquette existante. Si le transporteur a pu recevoir l’envoi avant une interruption, le statut passe à `review` ou reste à `processing`. Rechercher la référence marchand (15 premiers caractères de la commande, en majuscules) dans Connect avant toute nouvelle émission. Si Connect confirme la création, reporter le numéro et l’URL dans `shipments` et passer à `ready`. Si Connect confirme son absence, retirer uniquement cette tentative avant de relancer.

## Hébergement

- [Boutique](https://www.bolltrack.fr/boutique.html)
- [Application Railway](https://bolltrack-production.up.railway.app/boutique.html)
- [Projet Railway BollTrack](https://railway.com/project/3bd50c6b-7d6c-48ad-85f8-8b50cd2abaa6)
- [Code](https://github.com/Steph68700/bolltrack)

GitHub Pages sert actuellement le domaine historique. Sa page boutique ouvre l’application Railway pour conserver la session et le panier sur la même origine. Le Dockerfile sert aussi les pages historiques et leurs médias. La connexion MySQL utilise le réseau privé de Railway via `MYSQL_URL` référencé sur le service MySQL. `Shop__Origin` fixe l’origine publique du parcours de commande et de connexion. La page de retour Entreli autorisée est `/compte/retour` sur l’hôte Railway et sur `www.bolltrack.fr`. Lors d’une future bascule DNS, aligner l’origine de la boutique et la destination de webhook Stripe.

[Stripe Checkout](https://docs.stripe.com/checkout/fulfillment) · [Documentation technique Mondial Relay](https://www.mondialrelay.fr/documentation-technique/boites-a-outils/)
