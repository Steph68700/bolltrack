/* Catalogue de présentation : aucune API, aucun compte, aucun paiement. */
(() => {
  'use strict';
  const products = [
    {id:'bolltrack-2026-violet',name:'T-shirt BollTrack 2026 Violet',title:'T-SHIRT BOLLTRACK 2026',category:'tshirts',collection:'BOLLTRACK · 2026',color:'Violet',tag:'HIVERNAL 2026',tone:'purple',image:'tshirt-bolltrack-2.jpg',alt:'T-shirt BollTrack 2026 violet, face et dos'},
    {id:'bolltrack-2026-jaune',name:'T-shirt BollTrack 2026 Jaune',title:'T-SHIRT BOLLTRACK 2026',category:'tshirts',collection:'BOLLTRACK · 2026',color:'Jaune',tag:'HIVERNAL 2026',tone:'yellow',image:'tshirt-bolltrack-1.jpg',alt:'T-shirt BollTrack 2026 jaune, face et dos'},
    {id:'pull-2026-violet',name:'Pull BollTrack 2026 Violet',title:'PULL BOLLTRACK 2026',category:'pulls',collection:'BOLLTRACK · 2026',color:'Violet',tag:'HIVERNAL 2026',tone:'purple',image:'sweat-bolltrack-2.jpg',alt:'Pull à capuche BollTrack 2026 violet, face et dos'},
    {id:'pull-2026-jaune',name:'Pull BollTrack 2026 Jaune',title:'PULL BOLLTRACK 2026',category:'pulls',collection:'BOLLTRACK · 2026',color:'Jaune',tag:'HIVERNAL 2026',tone:'yellow',image:'sweat-bolltrack-1.jpg',alt:'Pull à capuche BollTrack 2026 jaune, face et dos'},
    {id:'bonnet-hivernal-2026',name:'Bonnet BollTrack Hivernal 2026',title:'BONNET BOLLTRACK HIVERNAL',category:'accessoires',collection:'BOLLTRACK · 2026',color:'Noir',tag:'HIVERNAL 2026',tone:'black',image:'bonnet-bolltrack-hivernal.jpg',alt:'Bonnet noir avec inscription BollTrack Hivernal',cardClass:'is-beanie'},
    {id:'bolltrack-2025',name:'T-shirt BollTrack 2025',title:'T-SHIRT BOLLTRACK 2025',category:'tshirts',collection:'BOLLTRACK · 2025',color:null,tag:'ÉDITION 2025',tone:'black',image:'archive/06_09_2025/tshirt.jpg',alt:'Visuel du T-shirt BollTrack de l’édition 2025',cardClass:'is-archive'},
    {id:'casquette-2025',name:'Casquette BollTrack 2025',title:'CASQUETTE BOLLTRACK 2025',category:'accessoires',collection:'BOLLTRACK · 2025',color:null,tag:'ÉDITION 2025',tone:'black',image:null,placeholder:'cap'},
    {id:'omen-haze',name:'T-shirt Omen Haze',title:'T-SHIRT OMEN HAZE',category:'tshirts',collection:'OMEN HAZE',color:null,tag:'LE GROUPE',tone:'black',image:null,placeholder:'shirt',cardClass:'is-omen'}
  ];
  const byId = new Map(products.map(product => [product.id, product]));
  const cart = new Map(); // Mémoire de l’onglet uniquement : pas de données personnelles.
  const productDialog = document.getElementById('product-dialog');
  const cartDialog = document.getElementById('cart-dialog');
  const accountDialog = document.getElementById('account-dialog');
  const detail = document.getElementById('product-detail');
  let currentProduct = null;
  let toastTimer;
  const shirtIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="m32 20-22 15 12 22 12-7v34h32V50l12 7 12-22-22-15-9 5H41l-9-5Z"/><path d="M40 24a10 10 0 0 0 20 0M34 50V31M66 50V31"/></svg>';
  const capIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M22 58c0-26 10-39 29-39 21 0 32 16 32 39M22 58h61c5 0 10 5 6 10-14 10-41 12-65 6C12 71 9 63 22 58Z"/><path d="M51 19c-11 8-12 23-12 39M54 58c-3 9-4 15 2 18M47 18h8"/></svg>';
  const bagIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M24 33h52l5 53H19l5-53Zm13 0V24a13 13 0 0 1 26 0v9"/></svg>';
  const placeholder = product => `<div class="product-placeholder">${product.placeholder === 'cap' ? capIcon : shirtIcon}<strong>${product.id === 'omen-haze' ? 'OMEN HAZE' : 'BOLLTRACK 2025'}</strong><span>Photo de l’article à venir</span></div>`;
  const imageMarkup = product => product.image ? `<div class="product-image-stage"><img src="${product.image}" alt="${product.alt}" width="600" height="400" loading="lazy"></div>` : placeholder(product);
  const toneClass = product => product.tone === 'yellow' ? 'yellow' : product.tone === 'black' ? 'black' : '';
  function renderProducts(filter = 'all') {
    const visible = products.filter(product => filter === 'all' || product.category === filter);
    document.getElementById('product-grid').innerHTML = visible.map(product => `<article class="product-card ${product.cardClass || ''}"><button class="product-visual" type="button" data-product="${product.id}" aria-label="Voir l’article : ${product.name}"><span class="product-tag ${product.tone}">${product.tag}</span>${imageMarkup(product)}<span class="product-arrow" aria-hidden="true"><svg class="arrow-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 19 19 5M5 5h14v14"/></svg></span></button><div class="product-meta"><span>${product.collection}${product.color ? ` · ${product.color.toUpperCase()}` : ''}</span>${product.color ? `<span class="color-dot ${toneClass(product)}" aria-hidden="true"></span>` : ''}</div><h3><button class="product-title-button" type="button" data-product="${product.id}">${product.title}${product.color && product.category !== 'accessoires' ? `<br>${product.color.toUpperCase()}` : ''}</button></h3><p class="product-price">Prix à confirmer</p></article>`).join('');
    document.getElementById('results-count').textContent = `${visible.length} article${visible.length > 1 ? 's' : ''}`;
    document.querySelectorAll('[data-filter]').forEach(button => {
      const selected = button.dataset.filter === filter;
      button.classList.toggle('active', selected);
      button.setAttribute('aria-pressed', String(selected));
    });
  }
  function closeDialog(dialog) { dialog.close(); }
  function openDialog(dialog) { if (!dialog.open) dialog.showModal(); }
  function renderProduct(product) {
    currentProduct = product;
    const variants = products.filter(other => other.category === product.category && other.id !== product.id && other.collection === product.collection && other.color && product.color && product.category !== 'accessoires');
    const variantButtons = variants.length ? `<div class="variant-links" aria-label="Choisir le motif">${[product, ...variants].sort((a,b) => a.color.localeCompare(b.color,'fr')).map(other => `<button class="variant-button" type="button" data-product="${other.id}" aria-pressed="${other.id === product.id}"><span class="color-dot ${toneClass(other)}" aria-hidden="true"></span>${other.color}</button>`).join('')}</div>` : '';
    detail.innerHTML = `<div class="detail-layout"><div class="detail-image ${product.cardClass || ''} ${!product.image ? 'is-placeholder' : ''}">${product.image ? `<img src="${product.image}" alt="${product.alt}" width="600" height="400"><button class="photo-enlarge" type="button" id="photo-enlarge">Agrandir la photo <svg class="arrow-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 19 19 5M5 5h14v14"/></svg></button>` : placeholder(product)}</div><div class="detail-content"><p class="eyebrow">${product.collection}</p><h2 id="detail-title">${product.name}</h2><p class="detail-price">Prix à confirmer</p>${variantButtons}<dl class="detail-specs">${product.color ? `<div><dt>Motif / couleur</dt><dd>${product.color}</dd></div>` : ''}<div><dt>Tailles</dt><dd>À confirmer</dd></div><div><dt>Disponibilité</dt><dd>À confirmer</dd></div></dl><p class="detail-note">Les prix, les tailles et les quantités seront précisés avant l’ouverture des commandes.</p><button class="primary-button" type="button" id="add-to-cart">Ajouter au panier d’aperçu <span aria-hidden="true">+</span></button><p class="preview-label">Aucune commande ni réservation n’est transmise.</p></div></div>`;
  }
  function showProduct(id) {
    const product = byId.get(id);
    if (!product) return;
    renderProduct(product);
    openDialog(productDialog);
  }
  function notify(message) {
    const toast = document.getElementById('shop-toast');
    clearTimeout(toastTimer);
    toast.textContent = message;
    toast.hidden = false;
    toastTimer = setTimeout(() => { toast.hidden = true; }, 3500);
  }
  function renderCart() {
    const quantity = [...cart.values()].reduce((sum, count) => sum + count, 0);
    document.getElementById('cart-count').textContent = quantity;
    document.getElementById('cart-open').setAttribute('aria-label', `Ouvrir le panier d’aperçu, ${quantity} article${quantity > 1 ? 's' : ''}`);
    const container = document.getElementById('cart-items');
    if (!quantity) {
      container.innerHTML = `<div class="cart-empty">${bagIcon}<h3>LE PROCHAIN SOUVENIR<br>COMMENCE ICI.</h3><p>Découvre la collection et ajoute les articles qui te plaisent à ton panier d’aperçu.</p></div>`;
      return;
    }
    container.innerHTML = [...cart].map(([id, count]) => {
      const product = byId.get(id);
      return `<article class="cart-line"><div class="cart-line-visual">${product.image ? `<img src="${product.image}" alt="${product.alt}" width="82" height="100">` : product.placeholder === 'cap' ? capIcon : shirtIcon}</div><div><h3>${product.name}</h3><p>Prix et taille à confirmer</p><div class="cart-line-actions"><div class="quantity-control"><button type="button" data-quantity="${id}" data-change="-1" ${count <= 1 ? 'disabled' : ''} aria-label="Diminuer la quantité de ${product.name}">−</button><output aria-label="Quantité de ${product.name}">${count}</output><button type="button" data-quantity="${id}" data-change="1" ${count >= 10 ? 'disabled' : ''} aria-label="Augmenter la quantité de ${product.name}">+</button></div><button class="remove-button" type="button" data-remove="${id}" aria-label="Retirer ${product.name} du panier">Retirer</button></div></div></article>`;
    }).join('');
  }
  document.addEventListener('click', event => {
    const target = event.target.closest('button, a');
    if (!target) return;
    if (target.dataset.product) { showProduct(target.dataset.product); return; }
    if (target.dataset.filter) { renderProducts(target.dataset.filter); return; }
    if (target.dataset.categoryLink) { renderProducts(target.dataset.categoryLink); return; }
    if (target.hasAttribute('data-close')) { closeDialog(target.closest('dialog')); return; }
    if (target.id === 'cart-open') { renderCart(); openDialog(cartDialog); return; }
    if (target.id === 'account-open') { openDialog(accountDialog); return; }
    if (target.id === 'add-to-cart' && currentProduct) {
      const currentCount = cart.get(currentProduct.id) || 0;
      if (currentCount >= 10) { notify('Limite de 10 exemplaires dans ce panier d’aperçu.'); return; }
      cart.set(currentProduct.id, currentCount + 1);
      renderCart();
      closeDialog(productDialog);
      notify('Article ajouté à l’aperçu · aucune réservation effectuée.');
      return;
    }
    if (target.dataset.remove) {
      cart.delete(target.dataset.remove);
      renderCart();
      document.querySelector('.cart-heading .dialog-close').focus();
      return;
    }
    if (target.dataset.quantity) {
      const id = target.dataset.quantity;
      const change = Number(target.dataset.change);
      cart.set(id, Math.min(10, Math.max(1, (cart.get(id) || 1) + change)));
      renderCart();
      const replacement = [...document.querySelectorAll('[data-quantity]')].find(button => button.dataset.quantity === id && button.dataset.change === String(change) && !button.disabled);
      (replacement || document.querySelector('.cart-heading .dialog-close')).focus();
      return;
    }
    if (target.id === 'photo-enlarge' && currentProduct?.image) {
      let lightbox = document.getElementById('photo-dialog');
      if (!lightbox) { lightbox = document.createElement('dialog'); lightbox.id = 'photo-dialog'; lightbox.className = 'lightbox-dialog'; lightbox.setAttribute('aria-label','Photo agrandie de l’article'); document.body.append(lightbox); }
      lightbox.innerHTML = `<button class="dialog-close" type="button" data-close aria-label="Fermer la photo agrandie">×</button><img src="${currentProduct.image}" alt="${currentProduct.alt}">`;
      openDialog(lightbox);
    }
  });
  document.addEventListener('click', event => {
    if (event.target instanceof HTMLDialogElement) {
      const box = event.target.getBoundingClientRect();
      if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) closeDialog(event.target);
    }
  });
  renderProducts();
  renderCart();
})();
