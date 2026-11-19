(() => {
  'use strict';
  const railwayShop='https://bolltrack-production.up.railway.app';
  const festivalOrigins=['https://www.bolltrack.fr','https://bolltrack.fr','https://steph68700.github.io'];
  const staticShop=document.documentElement.dataset.shopHost==='static' && festivalOrigins.includes(location.origin);
  if(staticShop && (['#compte','#panier','#retractation'].includes(location.hash) || new URLSearchParams(location.search).has('commande'))) {
    location.replace(railwayShop+'/boutique.html'+location.search+location.hash);
    return;
  }
  const $ = id => document.getElementById(id);
  const money = cents => new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',minimumFractionDigits:cents % 100 ? 2 : 0}).format(cents / 100);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]);
  const sizeLabel = size => size === 'TU' ? 'Taille unique' : size;
  const sizeOrder = ['XXS','XS','S','M','L','XL','2XL','3XL','4XL','5XL','TU'];
  const keyFor = (id,size) => id + ':' + size;
  const cartKey = 'bolltrack.cart.v1';
  const requestKey = 'bolltrack.checkout.v1';
  function read(key, fallback) { try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; } }
  function save(key, value) { try { localStorage.setItem(key,JSON.stringify(value)); } catch { /* Navigation privée */ } }
  async function api(path, options = {}) {
    const publicFeed=staticShop && path==='/api/catalogue';
    const response = await fetch(publicFeed ? railwayShop+path : path, {credentials:publicFeed?'omit':'same-origin',cache:'no-store',...options});
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      const error = new Error(body.error || (response.status === 401 ? 'Connecte-toi avec Entreli pour continuer.' : 'Le service est temporairement indisponible.'));
      error.status=response.status; throw error;
    }
    return response.status === 204 ? null : response.json();
  }
  const post = (path, body={}) => api(path,{method:'POST',headers:{'Content-Type':'application/json','X-BollTrack':'1'},body:JSON.stringify(body)});
  const layouts = {'bonnet-hivernal-2026':'is-beanie','bolltrack-2025':'is-archive','omen-haze-homme':'is-omen','omen-haze-femme':'is-omen'};
  function productView(row) {
    const variant = row.fit || (row.category !== 'accessoires' ? row.color : '');
    const title = variant && row.name.toLocaleLowerCase('fr').endsWith(' '+variant.toLocaleLowerCase('fr'))
      ? row.name.slice(0, -variant.length - 1) : row.name;
    let photo = null;
    if (row.image) {
      const apiPhoto=staticShop && row.image.startsWith('/api/catalogue/');
      const url = new URL(row.image, apiPhoto ? railwayShop : location.href);
      if (url.origin === (apiPhoto ? railwayShop : location.origin)) photo = url.href;
    }
    return {...row, title, image:photo, alt:row.name, cardClass:layouts[row.id] || '',
      tone:({'violet':'purple','jaune':'yellow'})[(row.color || '').toLowerCase()] || 'black',
      placeholder:row.id==='casquette-2025'?'cap':row.category==='accessoires'?'bag':'shirt'};
  }
  const shirtIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="m32 20-22 15 12 22 12-7v34h32V50l12 7 12-22-22-15-9 5H41l-9-5Z"/><path d="M40 24a10 10 0 0 0 20 0M34 50V31M66 50V31"/></svg>';
  const capIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M22 58c0-26 10-39 29-39 21 0 32 16 32 39M22 58h61c5 0 10 5 6 10-14 10-41 12-65 6C12 71 9 63 22 58Z"/><path d="M51 19c-11 8-12 23-12 39M54 58c-3 9-4 15 2 18M47 18h8"/></svg>';
  const bagIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M24 33h52l5 53H19l5-53Zm13 0V24a13 13 0 0 1 26 0v9"/></svg>';
  const placeholder = product => `<div class="product-placeholder">${product.placeholder === 'cap' ? capIcon : product.placeholder === 'bag' ? bagIcon : shirtIcon}<strong>${esc(product.collection || product.name)}</strong><span>Photo de l’article à venir</span></div>`;
  const imageMarkup = product => product.image ? `<div class="product-image-stage"><img src="${esc(product.image)}" alt="${esc(product.alt)}" width="600" height="400" loading="lazy"></div>` : placeholder(product);

  const products = [], byId = new Map(), cart = new Map();
  let methods = [], checkoutEnabled = false, account = null, filter = 'all', currentProduct, selectedSize, toastTimer;
  let request = read(requestKey, null);
  const productDialog = $('product-dialog'), cartDialog = $('cart-dialog'), accountDialog = $('account-dialog');
  const available = p => p.sizes.reduce((n,s) => n+s.available,0);
  const sizeStock = (p,s) => p.sizes.find(row => row.sizeCode === s)?.available ?? 0;
  function notify(message) {
    clearTimeout(toastTimer); $('shop-toast').textContent=message; $('shop-toast').hidden=false;
    toastTimer=setTimeout(()=>{$('shop-toast').hidden=true;},4000);
  }
  function open(dialog) { if (!dialog.open) dialog.showModal(); }
  async function loadCatalog() {
    const data=await api('/api/catalogue');
    products.length=0; byId.clear();
    for (const row of data.products) {
      const p=productView(row);
      const rank=size=>sizeOrder.includes(size)?sizeOrder.indexOf(size):sizeOrder.length;
      p.sizes.sort((a,b)=>rank(a.sizeCode)-rank(b.sizeCode)||a.sizeCode.localeCompare(b.sizeCode,'fr'));
      products.push(p); byId.set(p.id,p);
    }
    methods=data.methods; checkoutEnabled=data.checkoutEnabled;
    $('shop-status').textContent=checkoutEnabled ? 'Commandes ouvertes' : 'Stocks en ligne · Commandes prochainement';
    for (const button of document.querySelectorAll('[data-filter]')) {
      const category=button.dataset.filter;
      button.querySelector('span').textContent=products.filter(p=>category==='all'||p.category===category).length;
    }
    const feature=document.querySelector('.haze-section [data-product]');
    if(feature) {
      const omen=products.find(p=>p.id==='omen-haze-homme'||p.id==='omen-haze-femme');
      feature.hidden=!omen;
      if(omen) feature.dataset.product=omen.id;
    }
    renderProducts(); renderCart();
  }
  function renderProducts() {
    const visible=products.filter(p=>filter==='all'||p.category===filter);
    $('results-count').textContent=visible.length+' article'+(visible.length>1?'s':'');
    $('product-grid').innerHTML=visible.map(p=>'<article class="product-card '+(p.cardClass||'')+'"><button class="product-visual" type="button" data-product="'+esc(p.id)+'" aria-label="'+esc(p.name)+'"><span class="product-tag '+p.tone+'">'+esc(p.collection || p.category)+'</span>'+imageMarkup(p)+'<span class="product-arrow" aria-hidden="true">↗</span></button><div class="product-meta"><span>'+esc(p.collection)+'</span></div><h3><button class="product-title-button" type="button" data-product="'+esc(p.id)+'">'+esc(p.title)+(p.fit||p.color&&p.category!=='accessoires'?'<br>'+esc((p.fit||p.color).toUpperCase()):'')+'</button></h3><p class="product-price">'+money(p.priceCents)+'<span class="stock-hint '+(available(p)?'':'sold-out')+'">'+(available(p)?'En stock':'Rupture de stock')+'</span></p></article>').join('');
    document.querySelectorAll('[data-filter]').forEach(b=>{const active=b.dataset.filter===filter;b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));});
  }
  function showProduct(id, size) {
    const p=byId.get(id); if(!p) return;
    currentProduct=p;
    selectedSize=size || (p.sizes.length===1 ? p.sizes[0].sizeCode : null);
    const related=products.filter(q=>q.collection===p.collection && q.category===p.category && (p.fit?q.fit:p.color&&q.color&&p.category!=='accessoires'));
    const variants=related.length<2?'':'<div class="variant-links">'+related.map(q=>'<button class="variant-button" type="button" data-product="'+esc(q.id)+'" aria-pressed="'+(q.id===p.id)+'">'+esc(q.fit||q.color)+'</button>').join('')+'</div>';
    const sizes=p.sizes.map(s=>'<button type="button" data-size="'+esc(s.sizeCode)+'" aria-pressed="'+(selectedSize===s.sizeCode)+'" '+(!s.available?'disabled':'')+'>'+esc(sizeLabel(s.sizeCode))+'<small>'+(s.available?s.available+' en stock':'Épuisé')+'</small></button>').join('');
    const canAdd=selectedSize && sizeStock(p,selectedSize)>(cart.get(keyFor(id,selectedSize))||0);
    $('product-detail').innerHTML='<div class="detail-layout"><div class="detail-image '+(p.cardClass||'')+' '+(!p.image?'is-placeholder':'')+'">'+(p.image?'<img src="'+esc(p.image)+'" alt="'+esc(p.alt)+'"><button class="photo-enlarge" id="photo-enlarge" type="button">Agrandir la photo ↗</button>':placeholder(p))+'</div><div class="detail-content"><p class="eyebrow">'+esc(p.collection)+'</p><h2 id="detail-title">'+esc(p.name)+'</h2><p class="detail-price">'+money(p.priceCents)+'</p>'+variants+(sizes?'<p>Choisis ta taille</p><div role="group" aria-label="Tailles disponibles" class="size-options">'+sizes+'</div>':'')+'<button class="primary-button" id="add-to-cart" type="button" '+(canAdd?'':'disabled')+'>'+(!available(p)?'Rupture de stock':selectedSize&&!canAdd?'Quantité disponible déjà au panier':'Ajouter au panier')+'</button></div></div>';
    open(productDialog);
  }
  function cartLines() {
    return [...cart].map(([key,quantity])=>{const [id,size]=key.split(':');return {key,quantity,p:byId.get(id),size};}).filter(l=>l.p);
  }
  function updateCart() { save(cartKey,Object.fromEntries(cart)); renderCart(); }
  const subtotal=()=>cartLines().reduce((t,l)=>t+l.quantity*l.p.priceCents,0);
  function renderCart() {
    const lines=cartLines();
    const total=lines.reduce((t,l)=>t+l.quantity,0);
    $('cart-count').hidden=staticShop;
    $('cart-count').textContent=total; $('cart-open').setAttribute('aria-label',staticShop?'Ouvrir mon panier':'Ouvrir le panier, '+total+' article(s)');
    $('cart-bottom').hidden=!total;
    $('cart-items').innerHTML=lines.length?lines.map(({key,quantity,p,size})=>'<article class="cart-line"><div class="cart-line-visual">'+(p.image?'<img src="'+esc(p.image)+'" alt="">':p.placeholder==='cap'?capIcon:shirtIcon)+'</div><div><h3>'+esc(p.name)+'</h3><p>'+esc(sizeLabel(size))+' · '+money(p.priceCents)+' l’unité</p><div class="cart-line-actions"><div class="quantity-control"><button type="button" data-key="'+esc(key)+'" data-step="-1" aria-label="Diminuer la quantité">−</button><output>'+quantity+'</output><button data-key="'+esc(key)+'" data-step="1" '+(quantity>=sizeStock(p,size)?'disabled':'')+' type="button" aria-label="Augmenter la quantité">+</button></div><button class="remove-button" type="button" data-remove="'+esc(key)+'">Retirer</button></div>'+(quantity>sizeStock(p,size)?'<strong class="stock-hint sold-out">Quantité disponible : '+sizeStock(p,size)+'</strong>':'')+'</div></article>').join(''):'<div class="cart-empty">'+bagIcon+'<h3>TON PANIER EST VIDE.</h3><p>Choisis un article et sa taille dans la collection.</p></div>';
    $('subtotal').textContent=money(subtotal());
    const selector=$('shipping-mode');
    const previous=selector.value || read('bolltrack.livraison', '');
    selector.replaceChildren(new Option('Choisir mon mode de livraison', ''));
    for (const [id, name] of [['locker','En Locker Mondial Relay'],['domicile','À domicile par Mondial Relay']]) {
      const method=methods.find(m=>m.id===id);
      const suffix=method ? money(method.cents)+(checkoutEnabled&&!method.available?' · Indisponible':'') : 'Tarif à confirmer';
      const option=new Option(name+' · '+suffix,id);
      option.disabled=checkoutEnabled && !method?.available;
      selector.add(option);
    }
    selector.value=[...selector.options].some(o=>o.value===previous && !o.disabled) ? previous : '';
    shippingChanged();
  }
  function shippingChanged() {
    const mode=$('shipping-mode').value;
    const method=methods.find(m=>m.id===mode);
    const short=cartLines().some(l=>l.quantity>sizeStock(l.p,l.size));
    $('shipping-description').textContent=mode==='locker'?'Retrait dans la consigne de ton choix.':mode==='domicile'?'Livraison à l’adresse du destinataire.':'';
    $('cart-status').textContent=short?'Ajuste les quantités pour continuer.':!checkoutEnabled?'Les commandes ouvriront dès que le paiement et la livraison seront disponibles.':!mode?'Choisis ta livraison pour continuer.':!account?.connected?'Connecte-toi pour continuer ta commande.':'';
    $('cart-login').hidden=!checkoutEnabled||!method?.available||!!account?.connected||short;
    $('checkout-form').hidden=!checkoutEnabled||!method?.available||!account?.connected||short;
    $('shipping-total').hidden=!method;
    $('locker-picker').hidden=mode!=='locker'; $('locker-id').required=mode==='locker';
    $('checkout-total').textContent=method ? money(subtotal()+method.cents) : '';
  }
  function statusName(s) {return ({paid:'Payée',pending:'Paiement en attente',expired:'Paiement clôturé'})[s]||s;}
  function safeLink(value,allowed) {
    try {const url=new URL(value);return url.protocol==='https:' && allowed.some(h=>url.hostname===h||url.hostname.endsWith('.'+h))?esc(url.href):null;} catch {return null;}
  }
  let lastPrefill={};
  function prefillCheckout(address) {
    const form=$('checkout-form');
    if(!address) return;
    const fields={name:address.firstName+' '+address.lastName,street:address.street,complement:address.complement||'',postcode:address.postcode,city:address.city};
    for(const [key,value] of Object.entries(fields)) {
      const field=form.elements.namedItem(key);
      if(!field.value || field.value===lastPrefill[key]) field.value=value;
    }
    lastPrefill=fields;
  }
  const receiptUrl=(orderId,id)=>'/api/commandes/'+encodeURIComponent(orderId)+'/retractations/'+encodeURIComponent(id)+'/accuse';
  function withdrawalMarkup(o) {
    if(o.status!=='paid') return '';
    const receipts=(o.withdrawals || []).map(n=>'<p><a class="terms-link" download href="'+esc(receiptUrl(o.id,n.id))+'">Télécharger l’accusé du '+esc(new Date(n.submittedUtc).toLocaleString('fr-FR'))+'</a></p>').join('');
    const address=account?.address;
    return '<details class="withdrawal"><summary>Renoncer au contrat ici</summary>'+
      '<form class="shop-form" data-withdrawal="'+esc(o.id)+'"><p>Commande '+esc(o.id)+'. Vous pouvez déclarer un retour complet ou partiel, sans indiquer de motif.</p>'+
      '<div class="form-pair"><label>Prénom<input name="firstName" autocomplete="given-name" maxlength="60" value="'+esc(address?.firstName)+'" required></label>'+
      '<label>Nom<input name="lastName" autocomplete="family-name" maxlength="60" value="'+esc(address?.lastName)+'" required></label></div>'+
      '<label>Commande ou articles concernés<textarea name="details" maxlength="1000" required>Toute la commande</textarea></label>'+
      '<label>Réception de l’accusé<select name="receiptMode"><option>Fichier texte à télécharger sur cet appareil</option></select></label>'+
      '<button class="variant-button" type="submit">Confirmer la rétractation</button><p role="status"></p></form></details>'+receipts;
  }
  function orderMarkup(o) {
    const checkout=safeLink(o.checkoutUrl,['checkout.stripe.com']);
    const paid=o.status==='paid';
    const fulfillment=({preparing:'En préparation',shipped:'Expédiée'})[o.fulfillmentStatus];
    const req=o.returnRequest;
    const returnStatus=({requested:'Demande de retour enregistrée',processing:'Retour en cours',received:'Retour reçu',closed:'Dossier de retour clôturé'})[req?.status] || 'Demande de retour enregistrée';
    const returnForm=paid && (!req || req.status==='requested') ? `
      <details><summary>${req?'Préciser ma demande de retour':'Signaler un article défectueux ou demander un autre retour'}</summary>
        <form class="shop-form return-form" data-return-form="${esc(o.id)}">
          <label>Articles et quantités à retourner<textarea name="details" maxlength="1000" required>${esc(req?.details||'')}</textarea></label>
          <button class="variant-button" type="submit">Enregistrer ma demande</button><p role="status"></p>
        </form>
      </details>` : '';
    const lines=o.lines.map(l=>`<li>${esc(l.name)} · ${esc(sizeLabel(l.sizeCode))} × ${l.quantity}</li>`).join('');
    return `<article class="shop-order">
      <h4>${new Date(o.createdUtc).toLocaleDateString('fr-FR')} · ${money(o.totalCents)}</h4>
      ${paid && fulfillment?'<p><strong>'+esc(fulfillment)+'</strong></p>':''}
      <p>${esc(statusName(o.status))} · ${o.lockerName?'Locker '+esc(o.lockerName):'Livraison à domicile'} · ${money(o.shippingCents)}</p>
      <ul>${lines}</ul>
      ${o.termsPdf && /^\/assets\/boutique\/cgv-boutique-[0-9-]+\.pdf$/.test(o.termsPdf)?'<p><a class="terms-link" download href="'+esc(o.termsPdf)+'">Télécharger les CGV de ma commande</a></p>':''}
      ${o.discountCents?'<p>Remise · −'+money(o.discountCents)+'</p>':''}
      ${o.refundedCents?'<p>Remboursement Stripe · '+money(o.refundedCents)+'</p>':''}
      ${o.trackingNumber?'<p>Suivi Mondial Relay · '+esc(o.trackingNumber)+'</p>':''}
      <div class="customer-actions">
        ${checkout?'<a class="variant-button" href="'+checkout+'">Reprendre le paiement</a>':''}
        ${paid?'<button class="variant-button" type="button" data-invoice="'+esc(o.id)+'">Voir ma facture</button>':''}
        ${o.status==='pending'?'<button class="text-button" data-cancel="'+esc(o.id)+'">Annuler ce paiement</button>':''}
        ${o.status!=='expired'?'<button class="text-button" data-refresh="'+esc(o.id)+'">Actualiser</button>':''}
      </div>${req?'<p>'+esc(returnStatus)+'</p>':''}${withdrawalMarkup(o)}${returnForm}</article>`;
  }
  async function loadAccount() {
    const data=await api('/api/compte'); account=data;
    if(request && data.orders?.some(o=>o.id===request.id && o.status==='expired')) {request=null;save(requestKey,null);}
    const el=$('account-content');
    if(!data.connected) {
      $('checkout-form').reset(); lastPrefill={};
      el.innerHTML='<p>Utilise ton compte Entreli pour retrouver tes commandes BollTrack.</p><div class="customer-actions"><a class="primary-button" href="/compte/connexion'+(location.hash==='#retractation'?'?retour=retractation':'')+'">Me connecter avec Entreli</a><a class="variant-button" href="/compte/inscription">Créer mon compte Entreli</a></div>';
    } else {
      if(!$('address-form')) {
        el.replaceChildren($('customer-template').content.cloneNode(true));
        for(const [key,value] of Object.entries(data.address||{})) {
          const field=$('address-form').elements.namedItem(key);
          if(field) field.value=value||'';
        }
      }
      $('admin-link').hidden=!data.isAdmin;
      prefillCheckout(data.address);
      $('my-orders').innerHTML=data.orders.length ? data.orders.map(orderMarkup).join('') : '<p>Tu n’as pas encore de commande BollTrack.</p>';
    }
    shippingChanged();
    return data;
  }
  $('account-content').addEventListener('submit',async event=>{
    const form=event.target;
    if(form.id!=='address-form' && !form.hasAttribute('data-return-form') && !form.hasAttribute('data-withdrawal')) return;
    event.preventDefault();
    const button=form.querySelector('[type=submit]');
    const feedback=form.querySelector('[role=status]');
    if(button.disabled) return;
    button.disabled=true; feedback.textContent='';
    try {
      const values=Object.fromEntries(new FormData(form));
      if(form.hasAttribute('data-withdrawal')) {
        const id=form.dataset.withdrawal;
        form.dataset.noticeId ||= crypto.randomUUID().replaceAll('-','');
        const {id:noticeId}=await post('/api/commandes/'+encodeURIComponent(id)+'/retractations',{...values,noticeId:form.dataset.noticeId});
        const link=document.createElement('a');link.href=receiptUrl(id,noticeId);link.download='';link.textContent='Télécharger mon accusé de réception';feedback.replaceChildren('Déclaration enregistrée. ',link);link.click();
        await loadAccount();notify('Rétractation enregistrée. Ton accusé est aussi disponible dans Mes commandes.');
      } else if(form.id==='address-form') {
        const address=await post('/api/compte/adresse',values);
        account.address=address; prefillCheckout(address);
        feedback.textContent='Tes coordonnées ont été enregistrées.';
      } else {
        await post('/api/commandes/'+form.dataset.returnForm+'/retour',values);
        await loadAccount(); notify('Ta demande de retour est enregistrée.');
      }
    } catch(e){feedback.textContent=e.message;}
    finally{button.disabled=false;}
  });
  async function accountDialogOpen() {
    open(accountDialog);
    $('account-content').textContent='Chargement du compte…';
    try {await loadAccount();} catch(e){$('account-content').textContent=e.message;}
  }
  $('shipping-mode').addEventListener('change',()=>{save('bolltrack.livraison',$('shipping-mode').value);shippingChanged();});
  $('search-cp').addEventListener('input',()=>{$('locker-id').innerHTML='<option value="">Choisir une consigne</option>';});
  $('checkout-form').addEventListener('submit',async event=>{
    event.preventDefault(); const form=event.target,button=form.querySelector('[type=submit]');
    if(button.disabled) return;
    button.disabled=true; $('checkout-feedback').textContent='Préparation du paiement…';
    const items=cartLines().map(l=>({productId:l.p.id,sizeCode:l.size,quantity:l.quantity}));
    const delivery=Object.fromEntries(new FormData(form));const termsVersion=delivery.termsVersion;delete delivery.termsVersion;delivery.mode=$('shipping-mode').value;
    delivery.lockerId=delivery.mode==='locker'?$('locker-id').value:null;
    // Seule l'empreinte est conservée. Les coordonnées restent dans le formulaire.
    try {
      const bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(JSON.stringify({items,delivery})));
      const hash=Array.from(new Uint8Array(bytes),x=>x.toString(16).padStart(2,'0')).join('');
      if(request?.hash!==hash) {request={id:crypto.randomUUID().replaceAll('-',''),hash,items};save(requestKey,request);}
      const result=await post('/api/commandes',{requestId:request.id,items,delivery,termsVersion});
      if(!safeLink(result.url,['checkout.stripe.com'])) throw new Error('Lien de paiement indisponible.');
      location.assign(result.url);
    } catch(e) {
      if(e.status===409){request=null;save(requestKey,null);}
      $('checkout-feedback').textContent=e.message; await loadCatalog().catch(()=>{});
    }
    finally {button.disabled=false;}
  });
  document.addEventListener('click',async event=>{
    const t=event.target.closest('button,a');if(!t) return;
    if(t.matches('a[href="/boutique.html#retractation"]')){
      event.preventDefault();
      if(staticShop){location.assign(railwayShop+'/boutique.html#retractation');return;}
      history.replaceState(null,'',location.pathname+'#retractation');
      await openWithdrawals();return;
    }
    const d=t.dataset;
    if(d.filter||d.categoryLink){filter=d.filter||d.categoryLink;renderProducts();return;}
    if(d.product){showProduct(d.product);return;}
    if(d.size&&currentProduct){showProduct(currentProduct.id,d.size);$('product-detail').querySelector('[aria-pressed="true"][data-size]')?.focus();return;}
    if(t.hasAttribute('data-close')){t.closest('dialog').close();return;}
    if(staticShop && (t.id==='cart-open' || t.id==='account-open')){
      location.assign(railwayShop+'/boutique.html'+(t.id==='cart-open'?'#panier':'#compte'));return;
    }
    if(t.id==='cart-open'){open(cartDialog);try{await loadCatalog();await loadAccount();}catch(e){notify(e.message);}return;}
    if(t.id==='account-open'){await accountDialogOpen();return;}
    if(t.id==='add-to-cart'&&currentProduct&&selectedSize){
      if(staticShop){
        const intent=new URLSearchParams({article:currentProduct.id,taille:selectedSize,operation:crypto.randomUUID()});
        location.assign(railwayShop+'/boutique.html#ajouter?'+intent);
        return;
      }
      const k=keyFor(currentProduct.id,selectedSize),n=cart.get(k)||0;
      if(n<sizeStock(currentProduct,selectedSize)){cart.set(k,n+1);updateCart();productDialog.close();notify(currentProduct.name+' · '+sizeLabel(selectedSize)+' ajouté au panier.');}return;
    }
    if(d.remove){cart.delete(d.remove);updateCart();return;}
    if(d.key){const [id,size]=d.key.split(':');const next=cart.get(d.key)+Number(d.step);
      if(next===0 && d.step==='-1'){cart.delete(d.key);updateCart();}
      else if(next>=1&&(d.step==='-1'||next<=sizeStock(byId.get(id),size))){cart.set(d.key,next);updateCart();}return;}
    if(t.id==='photo-enlarge'&&currentProduct?.image){
      let box=$('photo-dialog'); if(!box){box=document.createElement('dialog');box.id='photo-dialog';box.className='lightbox-dialog';box.setAttribute('aria-label','Photo agrandie');document.body.append(box);}
      box.innerHTML='<button type="button" data-close class="dialog-close" aria-label="Fermer">×</button><img src="'+esc(currentProduct.image)+'" alt="'+esc(currentProduct.alt)+'">';open(box);return;
    }
    try {
      if(t.id==='logout'){await post('/api/deconnexion');await loadAccount();return;}
      if(d.invoice){
        t.disabled=true;
        const {url}=await post('/api/commandes/'+d.invoice+'/facture');
        if(!safeLink(url,['stripe.com'])) throw new Error('Lien de facture indisponible.');
        location.assign(url);return;
      }
      if(d.refresh||d.cancel){t.disabled=true;await post('/api/commandes/'+(d.refresh||d.cancel)+'/'+(d.refresh?'actualiser':'annuler'));await loadAccount();await loadCatalog();return;}
      if(t.id==='search-locker'){
        t.disabled=true;const cp=$('search-cp').value.trim();
        const lockers=await api('/api/livraison/lockers?cp='+encodeURIComponent(cp));
        $('locker-id').replaceChildren(new Option(lockers.length?'Choisir une consigne':'Aucun Locker disponible',''),...lockers.map(l=>new Option(l.name+' · '+l.street+' · '+l.postcode+' '+l.city,l.id)));
        $('checkout-feedback').textContent=lockers.length?'':'Essaie un autre code postal.';
      }
    } catch(e){if(t.id==='search-locker') $('checkout-feedback').textContent=e.message;else notify(e.message);}
    finally {if(t.id==='search-locker'||d.refresh||d.cancel||d.invoice)t.disabled=false;}
  });
  document.addEventListener('click',e=>{if(e.target instanceof HTMLDialogElement){const r=e.target.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)e.target.close();}});
  async function openWithdrawals(){
    await accountDialogOpen();
    if(!account?.connected) return;
    $('my-orders').scrollIntoView({block:'start'});
    const forms=$('my-orders').querySelectorAll('details.withdrawal');
    if(forms.length===1) forms[0].open=true;
    if(!forms.length) notify('Tu peux aussi envoyer ta déclaration à omenhaze68@gmail.com.');
  }
  async function init(){
    const saved=staticShop ? {} : read(cartKey,{});
    for(const [key,n] of Object.entries(saved||{})) if(Number.isInteger(n)&&n>0&&n<=999)cart.set(key,n);
    try{await loadCatalog();}
    catch(e){$('product-grid').textContent='Le stock ne peut pas être chargé. Actualise la page pour réessayer.';$('results-count').textContent='Catalogue indisponible';}
    if(!staticShop && location.hash.startsWith('#ajouter?')){
      const intent=new URLSearchParams(location.hash.slice(9));
      const p=byId.get(intent.get('article')),size=intent.get('taille'),op=intent.get('operation');
      const source=document.referrer ? new URL(document.referrer).origin : '';
      if(!p || !p.sizes.some(s=>s.sizeCode===size)){notify('Cet article ou cette taille est indisponible.');return;}
      history.replaceState(null,'',location.pathname);
      // Si le navigateur masque la provenance, l'acheteur confirme la taille sur la fiche.
      if(!festivalOrigins.includes(source) || !/^[a-f0-9-]{36}$/.test(op||'')){showProduct(p.id,size);return;}
      const key=keyFor(p.id,size),quantity=cart.get(key)||0,seen=read('bolltrack.ajouts',[]);
      if(!seen.includes(op) && quantity<sizeStock(p,size)) {
        cart.set(key,quantity+1);updateCart();save('bolltrack.ajouts',[...seen.slice(-39),op]);
      }
      history.replaceState(null,'',location.pathname+'#panier');open(cartDialog);
      await loadAccount().catch(e=>notify(e.message));return;
    }
    const query=new URLSearchParams(location.search);
    if(location.hash==='#retractation'){
      await openWithdrawals();
    }else if(location.hash==='#compte'){
      await accountDialogOpen();
      const id=query.get('commande');
      if(account?.connected && id && account.orders.some(o=>o.id===id)){
        try{
          await post('/api/commandes/'+id+'/actualiser');
          const confirmed=await loadAccount();
          if(confirmed.orders.some(o=>o.id===id&&o.status==='paid')&&request?.id===id){
            for(const item of request.items){const k=keyFor(item.productId,item.sizeCode),left=(cart.get(k)||0)-item.quantity;if(left>0)cart.set(k,left);else cart.delete(k);}
            request=null;save(requestKey,null);updateCart();notify('Merci, ton paiement est confirmé.');
          }
        }catch(e){notify(e.message);}
      }
      if(query.has('connexion'))notify('Connexion interrompue. Tu peux réessayer.');
      history.replaceState(null,'',location.pathname+'#compte');
    }else if(location.hash==='#panier'){open(cartDialog);await loadAccount().catch(e=>notify(e.message));}
  }
  init();
})();
