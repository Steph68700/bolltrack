# Le magasin du Fest — première version

La page `boutique.html` présente les huit articles communiqués par l’organisateur.
Le catalogue, les filtres, les variantes, les photos agrandies et le panier
d’aperçu fonctionnent uniquement dans le navigateur. Le panier est conservé en
mémoire pendant la consultation et disparaît au rechargement. Aucun appel API,
compte, paiement, réservation, commande ou envoi d’email n’est déclenché.

## Contenu à compléter avant la vente

- Confirmer le prix de chacun des huit articles (les pages historiques du site
  présentent des tarifs différents pour certains modèles).
- Renseigner les tailles et le stock par référence.
- Ajouter les photos du T-shirt Omen Haze et de la casquette BollTrack 2025.
- Confirmer les modalités et tarifs de retrait, d’expédition et de retour.
- Connecter l’identité Entreli, une base commerciale dédiée et le compte Stripe
  de l’association Omen Haze dans une prochaine phase.
- Préparer les informations vendeur, les conditions de vente et la facturation
  correspondant à la boutique avant d’activer une prise de commande.

Les images utilisées sont celles déjà présentes dans ce dépôt. Les visuels de
produits absents sont explicitement indiqués comme étant à venir.

## Déploiement Railway

Le `Dockerfile` sert le site existant, ses archives et la boutique avec Nginx.
Le port suit la variable Railway `PORT` (8080 par défaut). `/health` est utilisé
comme contrôle de santé. `/boutique` et `/magasin` redirigent vers
`/boutique.html`. Aucune base, aucun volume et aucun secret ne sont nécessaires.

- Projet : `BollTrack` (`3bd50c6b-7d6c-48ad-85f8-8b50cd2abaa6`).
- Environnement : `production` (`73859b2d-bf7f-4eb2-836f-9229c9380b1b`).
- Source : dépôt GitHub `Steph68700/bolltrack`, branche `main`.

La bascule du domaine nécessite de remplacer les enregistrements DNS de
l’ancien hébergement par les valeurs fournies par Railway. Conserver les
autres enregistrements, en particulier ceux des emails.

Les polices sont servies localement ; leurs licences OFL accompagnent les
fichiers dans `assets/fonts/`.
