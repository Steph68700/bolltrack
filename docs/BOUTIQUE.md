# Boutique BollTrack

La boutique utilise ASP.NET Core 8, Entity Framework Core et MySQL 8. Le projet Railway BollTrack contient les services `bolltrack` et `MySQL`. La base `bolltrack_shop` contient le catalogue, les stocks et les commandes. Entreli conserve les comptes, les mots de passe et les profils.

## Stock

Dans Railway, ouvrir **BollTrack → MySQL → Data → stock**. Chaque ligne correspond à une référence et une taille. `Available` est la quantité encore disponible à la vente. Mettre à jour cette colonne pour comptabiliser une vente sur stand ou un réassort. Dans `products`, `PriceCents` est le prix unitaire en centimes. Les clients voient les nouvelles valeurs en rechargeant la boutique ou en ouvrant leur panier.

L’inventaire communiqué le 19 novembre 2026 est importé dans une transaction par `shop/stock.sql`. La table `shop_migrations` empêche un redéploiement de rétablir les quantités initiales. Le T-shirt BollTrack 2025 est présent avec son prix et sans variante vendable.

## Compte Entreli

Le client public `bolltrack-boutique-web` utilise le flux OpenID Connect avec code et PKCE de StudioIdentity. Les routes `/compte/connexion` et `/compte/inscription` ramènent le client à la boutique. « Mon compte » présente son nom, son prénom, son adresse de livraison et ses commandes BollTrack. La boutique sauvegarde les coordonnées dans `customer_addresses`, sous l’identifiant signé par Entreli. L’adresse enregistrée préremplit le panier. Le destinataire d’une commande peut être différent et ses coordonnées restent celles saisies au moment de l’achat. Le téléphone est demandé à la commande pour le transporteur.

« Mes commandes » indique les articles, le mode de livraison, les frais, les remises et le total payé. Chaque acheteur peut y consulter sa facture, le suivi de son colis et ses éventuels remboursements, puis y déposer une demande de retour.

## Paiement et livraison

Renseigner les valeurs propres à l’association dans les variables du service Railway **bolltrack**. Les frais sont des montants facturés au client pour l’ensemble du panier, en centimes d’euro. Un tarif à zéro active la gratuité du mode correspondant.

| Variable | Valeur attendue |
| --- | --- |
| `Stripe__SecretKey` | Clé secrète du compte Stripe vendeur dans l’environnement choisi |
| `Stripe__WebhookSecret` | Secret de signature du webhook de ce même environnement |
| `MondialRelay__Enseigne`, `MondialRelay__ClePrivee` | Identifiants API 1 du contrat pour la recherche de Lockers |
| `MondialRelay__LockerCents` | `500` pour une livraison à 5,00 € par commande en Locker |
| `MondialRelay__DomicileCents` | `750` pour une livraison à 7,50 € par commande à domicile |
| `MondialRelay__DomicileActif` | `true` lorsque le contrat couvre la livraison à domicile en France |
| `MondialRelay__Api2Login`, `MondialRelay__Api2Password`, `MondialRelay__Api2Client` | Identifiants API 2 Mondial Relay Connect pour les étiquettes |
| `MondialRelay__Collecte`, `MondialRelay__LieuCollecte` | Mode du contrat, REL ou CCC, et code de lieu si nécessaire |
| `MondialRelay__Expediteur__Nom`, `__Rue`, `__CodePostal`, `__Ville`, `__Telephone`, `__Email` | Chaque suffixe est précédé de `MondialRelay__Expediteur`. `__Complement` est facultatif. |

Dans Stripe, créer une destination d’événements de type snapshot vers `https://bolltrack-production.up.railway.app/api/stripe/webhook`. Sélectionner `checkout.session.completed`, `checkout.session.expired`, `charge.refunded`, `refund.updated` et `refund.failed`. Copier le secret de signature de cette destination dans Railway. Utiliser le même environnement Stripe pour la clé et la destination. [Destinations d’événements Stripe](https://dashboard.stripe.com/workbench/webhooks)

Le client choisit sa livraison en France métropolitaine dans le panier. Les tarifs sont visibles avant connexion. La boutique utilise les prix MySQL, revalide la consigne auprès de Mondial Relay et transmet à Stripe le tarif fixe du mode choisi.

Les articles sont réservés par transaction au début du paiement. Le webhook signé ou une vérification de la session auprès de Stripe confirme le règlement. Une session expirée restitue ses unités une seule fois. Un rapprochement périodique rattrape les notifications manquées. Depuis « Mon compte », le client peut reprendre ou annuler un paiement en attente.

### Factures et codes promo

La boutique active la facture après chaque paiement unique. Renseigner les coordonnées de l’association et les mentions applicables dans les paramètres de facturation de son compte Stripe. Le bouton « Voir ma facture » ouvre le document produit par Stripe. [Factures après paiement](https://docs.stripe.com/receipts)

Dans le Dashboard Stripe de la boutique, ouvrir **Catalogue de produits → Coupons**, créer une remise en pourcentage ou en euros, puis lui associer un code promotionnel que le client peut saisir. Pour une remise sur l’ensemble du merchandising, appliquer le coupon à tous les produits. La livraison est un tarif séparé. La boutique conserve le montant effectivement payé et la remise confirmée par Stripe. [Coupons et codes promotionnels](https://docs.stripe.com/payments/checkout/discounts) · [Gérer les coupons](https://dashboard.stripe.com/coupons)

### Traiter un retour

1. Le client ouvre sa commande payée et indique les articles et les quantités à retourner. Il peut corriger sa demande tant qu’elle est en attente.
2. L’association retrouve la demande dans **Railway → MySQL → return_requests** avec la référence `OrderId`. La table `orders` contient l’adresse e-mail associée à cette commande. L’association répond au client et peut créer une étiquette dans Mondial Relay Connect si elle prend en charge le port retour. [Retour avec Mondial Relay](https://www.mondialrelay.fr/faq-pro/envoyer-un-colis/comment-mon-client-fait-il-le-retour-d-un-colis/)
3. L’association fait évoluer `return_requests.Status` de `requested` à `processing`, `received` puis `closed` selon l’avancement du dossier. Ces statuts sont visibles dans « Mes commandes ». À réception, elle ajuste le stock des unités qu’elle remet en vente.
4. Pour rembourser, ouvrir le paiement de la commande BollTrack dans Stripe et choisir le montant total ou partiel. La notification Stripe actualise le montant remboursé dans la commande. La demande de retour et le remboursement sont deux actions distinctes. [Rembourser un paiement](https://docs.stripe.com/refunds)

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
