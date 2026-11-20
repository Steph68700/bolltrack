# Boutique BollTrack

La boutique utilise ASP.NET Core 8, Entity Framework Core et MySQL 8. Le projet Railway BollTrack contient les services `bolltrack` et `MySQL`. La base `bolltrack_shop` contient le catalogue, les stocks et les commandes. Entreli conserve les comptes, les mots de passe et les profils.

## Stock

Depuis **Mon compte → Administrer la boutique** (ou `/boutique-admin.html`), « Produits » permet de créer une fiche, de modifier son prix et sa photo et de choisir sa visibilité. Un nouveau produit commence masqué. Pour les accessoires, saisir `TU` comme taille unique. Dans « Stock », choisir un produit et une taille, saisir le mouvement et son motif. La transaction conserve la quantité et l’historique. Les clients voient le catalogue et les disponibilités à la prochaine ouverture du panier ou actualisation.

Dans « Commandes et retours », l’association peut consulter les articles à préparer, enregistrer le numéro de suivi d’un colis expédié et suivre le traitement d’un retour. Le remboursement éventuel se fait dans Stripe.

L’accès est contrôlé par le serveur. `Shop__AdminEmails` désigne l’adresse autorisée, dont Entreli doit certifier la vérification. `Shop__AdminSubjects` permet de limiter cet accès à l’identifiant Entreli de la personne habilitée. Lorsque les deux variables sont définies, les deux conditions sont requises.

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
| `MondialRelay__Test__Api2Login`, `MondialRelay__Test__Api2Password`, `MondialRelay__Test__Api2Client` | Identifiants de la colonne « Vos paramètres de test » de l’API 2 pour le bouton « Tester sans expédier » |
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

Dans **Administration → Colis par défaut**, enregistrer le poids de l’emballage vide, un poids habituel par article et, si utile, les dimensions extérieures du colis fermé en centimètres. Une fiche **Produits** peut préciser un poids unitaire hors emballage. Cette valeur est prioritaire sur le poids habituel.

Le poids proposé pour une commande est la somme des poids unitaires multipliés par les quantités, plus un seul emballage. Si un poids unitaire manque et qu’aucun poids habituel ne le remplace, ou si l’emballage n’a pas été pesé, le champ reste à compléter. Chaque valeur proposée peut être corrigée pour cet envoi avant de générer l’étiquette.

Saisir le poids du colis fermé en grammes. En France, l’offre Start accepte de 100 g à 25 kg en Locker et à domicile. En cas de refus, le formulaire affiche le poids transmis, les motifs communiqués par Mondial Relay et la référence marchand à indiquer au support. [Conditions de l’offre Start, § 2.1](https://www.mondialrelay.fr/cgv-professionnels/) · [Codes de l’API d’expédition, p. 21](https://www.mondialrelay.fr/media/123861/web-service-dual-carrier-v-27.pdf)

**Tester sans expédier** génère un PDF de démonstration sur le serveur de test Mondial Relay avec le poids saisi, le mode de livraison et, le cas échéant, le Locker choisi. L’essai utilise des coordonnées fictives et une référence de test indépendante de la commande.

Dans **Administration → Commandes et retours**, ouvrir une commande payée à préparer et vérifier le poids du colis emballé. **Générer l’étiquette et le suivi** crée l’envoi via l’API 2. **Ouvrir l’étiquette PDF** permet ensuite de l’imprimer ou de la réimprimer. Le client retrouve le numéro et le lien de suivi dans « Mon compte ». Une commande déjà expédiée, intégralement remboursée ou pourvue d’un suivi manuel ne peut pas générer une nouvelle étiquette.

Le formulaire transmet au transporteur l’adresse e-mail enregistrée pour la commande. L’offre Start inclut l’e-mail Mondial Relay au client lors de la prise en charge. Marquer la commande « Expédiée » après avoir remis le colis. [Notifications de Mondial Relay](https://www.mondialrelay.fr/faq-pro/envoyer-un-colis/comment-mon-client-est-il-informe-de-la-mise-a-disposition-de-son-colis-et-a-t-il-un-suivi/)

Le bouton s’active avec les réglages API 2, collecte et expéditeur du tableau ci-dessus. Dans Mondial Relay Connect, les valeurs se trouvent dans « Configuration des API → API Version V2.0 ». La variable `MondialRelay__Sandbox=true` sélectionne le serveur de test avec les identifiants correspondants. L’outil en ligne de commande utilise le même traitement.

```sh
dotnet /app/Shop.dll etiquette ID_COMMANDE POIDS_G LONGUEUR_CM LARGEUR_CM HAUTEUR_CM
```

La table `shipments` associe une expédition à une commande. Relancer sur un envoi confirmé restitue le lien de l’étiquette existante. Si le transporteur a pu recevoir l’envoi avant une interruption, le statut passe à `review` ou reste à `processing`. Rechercher la référence marchand (15 premiers caractères de la commande, en majuscules) dans Connect avant toute nouvelle émission. Si Connect confirme la création, reporter le numéro et l’URL dans `shipments` et passer à `ready`. Si Connect confirme son absence, retirer uniquement cette tentative avant de relancer.

## Hébergement

- [Boutique](https://www.bolltrack.fr/boutique.html)
- [Application Railway](https://bolltrack-production.up.railway.app/boutique.html)
- [Projet Railway BollTrack](https://railway.com/project/3bd50c6b-7d6c-48ad-85f8-8b50cd2abaa6)
- [Code](https://github.com/Steph68700/bolltrack)

Le Dockerfile sert le site du festival et la boutique sur la même origine. Tant que le DNS de `www.bolltrack.fr` dirige les visiteurs vers GitHub Pages, le catalogue et les fiches restent sur le domaine du festival. La page lit le catalogue public sur Railway sans identifiants. Le panier et le compte s’ouvrent sur Railway pour retrouver la session de l’acheteur. Après le raccordement du domaine à Railway, les visiteurs gardent `www.bolltrack.fr` pendant la navigation et à leur retour d’Entreli ou de Stripe. Les deux adresses de retour Entreli restent autorisées. Le webhook Stripe peut conserver son adresse Railway.
