(() => {
  'use strict';
  // GitHub Pages sert encore le domaine historique. Sur Railway, le Dockerfile
  // identifie la version dynamique de cette même page.
  if (document.documentElement.dataset.shopHost === 'static' &&
      ['www.bolltrack.fr', 'bolltrack.fr', 'steph68700.github.io'].includes(location.hostname)) {
    location.replace('https://bolltrack-production.up.railway.app/boutique.html' + location.search + location.hash);
    return;
  }
  const $ = id => document.getElementById(id);
  const money = cents => new Intl.NumberFormat('fr-FR',{style:'currency',currency:'EUR',minimumFractionDigits:cents % 100 ? 2 : 0}).format(cents / 100);
  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[c]);
  const sizeLabel = size => size === 'TU' ? 'Taille unique' : size;
  const sizeOrder = ['S','M','L','XL','2XL','3XL','TU'];
  const keyFor = (id,size) => id + ':' + size;
  const cartKey = 'bolltrack.cart.v1';
  const requestKey = 'bolltrack.checkout.v1';
  function read(key, fallback) { try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; } }
  function save(key, value) { try { localStorage.setItem(key,JSON.stringify(value)); } catch { /* Navigation privée */ } }
  async function api(path, options = {}) {
    const response = await fetch(path, {credentials:'same-origin',cache:'no-store',...options});
    if (!response.ok) {
      const body = await response.json().catch(() => ({}));
      const error = new Error(body.error || (response.status === 401 ? 'Connecte-toi avec Entreli pour continuer.' : 'Le service est temporairement indisponible.'));
      error.status=response.status; throw error;
    }
    return response.status === 204 ? null : response.json();
  }
  const post = (path, body={}) => api(path,{method:'POST',headers:{'Content-Type':'application/json','X-BollTrack':'1'},body:JSON.stringify(body)});
  const collection = [
    {id:'bolltrack-2026-violet',name:'T-shirt BollTrack 2026 Violet',title:'T-SHIRT BOLLTRACK 2026',category:'tshirts',collection:'BOLLTRACK · 2026',color:'Violet',tag:'HIVERNAL 2026',tone:'purple',image:'tshirt-bolltrack-2.jpg',alt:'T-shirt BollTrack 2026 violet, face et dos'},
    {id:'bolltrack-2026-jaune',name:'T-shirt BollTrack 2026 Jaune',title:'T-SHIRT BOLLTRACK 2026',category:'tshirts',collection:'BOLLTRACK · 2026',color:'Jaune',tag:'HIVERNAL 2026',tone:'yellow',image:'tshirt-bolltrack-1.jpg',alt:'T-shirt BollTrack 2026 jaune, face et dos'},
    {id:'pull-2026-violet',name:'Pull BollTrack 2026 Violet',title:'PULL BOLLTRACK 2026',category:'pulls',collection:'BOLLTRACK · 2026',color:'Violet',tag:'HIVERNAL 2026',tone:'purple',image:'sweat-bolltrack-2.jpg',alt:'Pull à capuche BollTrack 2026 violet, face et dos'},
    {id:'pull-2026-jaune',name:'Pull BollTrack 2026 Jaune',title:'PULL BOLLTRACK 2026',category:'pulls',collection:'BOLLTRACK · 2026',color:'Jaune',tag:'HIVERNAL 2026',tone:'yellow',image:'sweat-bolltrack-1.jpg',alt:'Pull à capuche BollTrack 2026 jaune, face et dos'},
    {id:'bonnet-hivernal-2026',name:'Bonnet BollTrack Hivernal 2026',title:'BONNET BOLLTRACK HIVERNAL',category:'accessoires',collection:'BOLLTRACK · 2026',color:'Noir',tag:'HIVERNAL 2026',tone:'black',image:'bonnet-bolltrack-hivernal.jpg',alt:'Bonnet noir avec inscription BollTrack Hivernal',cardClass:'is-beanie'},
    {id:'bolltrack-2025',name:'T-shirt BollTrack 2025',title:'T-SHIRT BOLLTRACK 2025',category:'tshirts',collection:'BOLLTRACK · 2025',color:null,tag:'ÉDITION 2025',tone:'black',image:'archive/06_09_2025/tshirt.jpg',alt:'Visuel du T-shirt BollTrack de l’édition 2025',cardClass:'is-archive'},
    {id:'casquette-2025',name:'Casquette BollTrack 2025',title:'CASQUETTE BOLLTRACK 2025',category:'accessoires',collection:'BOLLTRACK · 2025',color:null,tag:'ÉDITION 2025',tone:'black',image:null,placeholder:'cap'},
    {id:'omen-haze-homme',name:'T-shirt Omen Haze',title:'T-SHIRT OMEN HAZE',fit:'Homme',category:'tshirts',collection:'OMEN HAZE',color:null,tag:'LE GROUPE',tone:'black',image:null,placeholder:'shirt',cardClass:'is-omen'},
    {id:'omen-haze-femme',name:'T-shirt Omen Haze',title:'T-SHIRT OMEN HAZE',fit:'Femme',category:'tshirts',collection:'OMEN HAZE',color:null,tag:'LE GROUPE',tone:'black',image:null,placeholder:'shirt',cardClass:'is-omen'}
  ];
  const shirtIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="m32 20-22 15 12 22 12-7v34h32V50l12 7 12-22-22-15-9 5H41l-9-5Z"/><path d="M40 24a10 10 0 0 0 20 0M34 50V31M66 50V31"/></svg>';
  const capIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M22 58c0-26 10-39 29-39 21 0 32 16 32 39M22 58h61c5 0 10 5 6 10-14 10-41 12-65 6C12 71 9 63 22 58Z"/><path d="M51 19c-11 8-12 23-12 39M54 58c-3 9-4 15 2 18M47 18h8"/></svg>';
  const bagIcon = '<svg viewBox="0 0 100 100" aria-hidden="true"><path d="M24 33h52l5 53H19l5-53Zm13 0V24a13 13 0 0 1 26 0v9"/></svg>';
  const placeholder = product => `<div class="product-placeholder">${product.placeholder === 'cap' ? capIcon : shirtIcon}<strong>${product.fit ? 'OMEN HAZE' : 'BOLLTRACK 2025'}</strong><span>Photo de l’article à venir</span></div>`;
  const imageMarkup = product => product.image ? `<div class="product-image-stage"><img src="${product.image}" alt="${product.alt}" width="600" height="400" loading="lazy"></div>` : placeholder(product);

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
    const stock=new Map(data.products.map(row=>[row.id,row]));
    products.length=0; byId.clear();
    for (const visual of collection) {
      const row=stock.get(visual.id);
      if (!row) continue;
      const p={...visual,...row};
      p.sizes.sort((a,b)=>sizeOrder.indexOf(a.sizeCode)-sizeOrder.indexOf(b.sizeCode));
      products.push(p); byId.set(p.id,p);
    }
    methods=data.methods; checkoutEnabled=data.checkoutEnabled;
    $('shop-status').textContent=checkoutEnabled ? 'Commandes ouvertes' : 'Stocks en ligne · Commandes prochainement';
    for (const button of document.querySelectorAll('[data-filter]')) {
      const category=button.dataset.filter;
      button.querySelector('span').textContent=products.filter(p=>category==='all'||p.category===category).length;
    }
    renderProducts(); renderCart();
  }
  function renderProducts() {
    const visible=products.filter(p=>filter==='all'||p.category===filter);
    $('results-count').textContent=visible.length+' article'+(visible.length>1?'s':'');
    $('product-grid').innerHTML=visible.map(p=>'<article class="product-card '+(p.cardClass||'')+'"><button class="product-visual" type="button" data-product="'+p.id+'" aria-label="'+esc(p.name)+'"><span class="product-tag '+p.tone+'">'+p.tag+'</span>'+imageMarkup(p)+'<span class="product-arrow" aria-hidden="true">↗</span></button><div class="product-meta"><span>'+p.collection+'</span></div><h3><button class="product-title-button" type="button" data-product="'+p.id+'">'+p.title+(p.fit||p.color&&p.category!=='accessoires'?'<br>'+esc((p.fit||p.color).toUpperCase()):'')+'</button></h3><p class="product-price">'+money(p.priceCents)+'<span class="stock-hint '+(available(p)?'':'sold-out')+'">'+(available(p)?'En stock':'Rupture de stock')+'</span></p></article>').join('');
    document.querySelectorAll('[data-filter]').forEach(b=>{const active=b.dataset.filter===filter;b.classList.toggle('active',active);b.setAttribute('aria-pressed',String(active));});
  }
  function showProduct(id, size) {
    const p=byId.get(id); if(!p) return;
    currentProduct=p;
    selectedSize=size || (p.sizes.length===1 ? p.sizes[0].sizeCode : null);
    const related=products.filter(q=>q.collection===p.collection && q.category===p.category && (p.fit?q.fit:p.color&&q.color&&p.category!=='accessoires'));
    const variants=related.length<2?'':'<div class="variant-links">'+related.map(q=>'<button class="variant-button" type="button" data-product="'+q.id+'" aria-pressed="'+(q.id===p.id)+'">'+esc(q.fit||q.color)+'</button>').join('')+'</div>';
    const sizes=p.sizes.map(s=>'<button type="button" data-size="'+s.sizeCode+'" aria-pressed="'+(selectedSize===s.sizeCode)+'" '+(!s.available?'disabled':'')+'>'+sizeLabel(s.sizeCode)+'<small>'+(s.available?s.available+' en stock':'Épuisé')+'</small></button>').join('');
    const canAdd=selectedSize && sizeStock(p,selectedSize)>(cart.get(keyFor(id,selectedSize))||0);
    $('product-detail').innerHTML='<div class="detail-layout"><div class="detail-image '+(p.cardClass||'')+' '+(!p.image?'is-placeholder':'')+'">'+(p.image?'<img src="'+p.image+'" alt="'+esc(p.alt)+'"><button class="photo-enlarge" id="photo-enlarge" type="button">Agrandir la photo ↗</button>':placeholder(p))+'</div><div class="detail-content"><p class="eyebrow">'+p.collection+'</p><h2 id="detail-title">'+esc(p.name)+'</h2><p class="detail-price">'+money(p.priceCents)+'</p>'+variants+(sizes?'<p>Choisis ta taille</p><div role="group" aria-label="Tailles disponibles" class="size-options">'+sizes+'</div>':'')+'<button class="primary-button" id="add-to-cart" type="button" '+(canAdd?'':'disabled')+'>'+(!available(p)?'Rupture de stock':selectedSize&&!canAdd?'Quantité disponible déjà au panier':'Ajouter au panier')+'</button></div></div>';
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
    $('cart-count').textContent=total; $('cart-open').setAttribute('aria-label','Ouvrir le panier, '+total+' article(s)');
    $('cart-bottom').hidden=!total;
    $('cart-items').innerHTML=lines.length?lines.map(({key,quantity,p,size})=>'<article class="cart-line"><div class="cart-line-visual">'+(p.image?'<img src="'+p.image+'" alt="">':p.placeholder==='cap'?capIcon:shirtIcon)+'</div><div><h3>'+esc(p.name)+'</h3><p>'+sizeLabel(size)+' · '+money(p.priceCents)+' l’unité</p><div class="cart-line-actions"><div class="quantity-control"><button type="button" data-key="'+key+'" data-step="-1" '+(quantity<=1?'disabled':'')+' aria-label="Diminuer la quantité">−</button><output>'+quantity+'</output><button data-key="'+key+'" data-step="1" '+(quantity>=sizeStock(p,size)?'disabled':'')+' type="button" aria-label="Augmenter la quantité">+</button></div><button class="remove-button" type="button" data-remove="'+key+'">Retirer</button></div>'+(quantity>sizeStock(p,size)?'<strong class="stock-hint sold-out">Quantité disponible : '+sizeStock(p,size)+'</strong>':'')+'</div></article>').join(''):'<div class="cart-empty">'+bagIcon+'<h3>TON PANIER EST VIDE.</h3><p>Choisis un article et sa taille dans la collection.</p></div>';
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
  function orderMarkup(o) {
    const checkout=safeLink(o.checkoutUrl,['checkout.stripe.com']);
    const paid=o.status==='paid';
    const req=o.returnRequest;
    const returnStatus=({requested:'Demande de retour enregistrée',processing:'Retour en cours',received:'Retour reçu',closed:'Dossier de retour clôturé'})[req?.status] || 'Demande de retour enregistrée';
    const returnForm=paid && (!req || req.status==='requested') ? `
      <details><summary>${req?'Modifier ma demande de retour':'Demander un retour'}</summary>
        <form class="shop-form return-form" data-return-form="${esc(o.id)}">
          <label>Articles et quantités à retourner<textarea name="details" maxlength="1000" required>${esc(req?.details||'')}</textarea></label>
          <button class="variant-button" type="submit">Enregistrer ma demande</button><p role="status"></p>
        </form>
      </details>` : '';
    const lines=o.lines.map(l=>`<li>${esc(l.name)} · ${esc(sizeLabel(l.sizeCode))} × ${l.quantity}</li>`).join('');
    return `<article class="shop-order">
      <h4>${new Date(o.createdUtc).toLocaleDateString('fr-FR')} · ${money(o.totalCents)}</h4>
      <p>${esc(statusName(o.status))} · ${o.lockerName?'Locker '+esc(o.lockerName):'Livraison à domicile'} · ${money(o.shippingCents)}</p>
      <ul>${lines}</ul>
      ${o.discountCents?'<p>Remise · −'+money(o.discountCents)+'</p>':''}
      ${o.refundedCents?'<p>Remboursement Stripe · '+money(o.refundedCents)+'</p>':''}
      ${o.trackingNumber?'<p>Suivi Mondial Relay · '+esc(o.trackingNumber)+'</p>':''}
      <div class="customer-actions">
        ${checkout?'<a class="variant-button" href="'+checkout+'">Reprendre le paiement</a>':''}
        ${paid?'<button class="variant-button" type="button" data-invoice="'+esc(o.id)+'">Voir ma facture</button>':''}
        ${o.status==='pending'?'<button class="text-button" data-cancel="'+esc(o.id)+'">Annuler ce paiement</button>':''}
        ${o.status!=='expired'?'<button class="text-button" data-refresh="'+esc(o.id)+'">Actualiser</button>':''}
      </div>${req?'<p>'+esc(returnStatus)+'</p>':''}${returnForm}</article>`;
  }
  async function loadAccount() {
    const data=await api('/api/compte'); account=data;
    if(request && data.orders?.some(o=>o.id===request.id && o.status==='expired')) {request=null;save(requestKey,null);}
    const el=$('account-content');
    if(!data.connected) {
      $('checkout-form').reset(); lastPrefill={};
      el.innerHTML='<p>Utilise ton compte Entreli pour retrouver tes commandes BollTrack.</p><div class="customer-actions"><a class="primary-button" href="/compte/connexion">Me connecter avec Entreli</a><a class="variant-button" href="/compte/inscription">Créer mon compte Entreli</a></div>';
    } else {
      if(!$('address-form')) {
        el.replaceChildren($('customer-template').content.cloneNode(true));
        for(const [key,value] of Object.entries(data.address||{})) {
          const field=$('address-form').elements.namedItem(key);
          if(field) field.value=value||'';
        }
      }
      prefillCheckout(data.address);
      $('my-orders').innerHTML=data.orders.length ? data.orders.map(orderMarkup).join('') : '<p>Tu n’as pas encore de commande BollTrack.</p>';
    }
    renderCart();
    return data;
  }
  $('account-content').addEventListener('submit',async event=>{
    const form=event.target;
    if(form.id!=='address-form' && !form.hasAttribute('data-return-form')) return;
    event.preventDefault();
    const button=form.querySelector('[type=submit]');
    const feedback=form.querySelector('[role=status]');
    if(button.disabled) return;
    button.disabled=true; feedback.textContent='';
    try {
      const values=Object.fromEntries(new FormData(form));
      if(form.id==='address-form') {
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
    const delivery=Object.fromEntries(new FormData(form));delivery.mode=$('shipping-mode').value;
    delivery.lockerId=delivery.mode==='locker'?$('locker-id').value:null;
    // Seule l'empreinte est conservée. Les coordonnées restent dans le formulaire.
    try {
      const bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(JSON.stringify({items,delivery})));
      const hash=Array.from(new Uint8Array(bytes),x=>x.toString(16).padStart(2,'0')).join('');
      if(request?.hash!==hash) {request={id:crypto.randomUUID().replaceAll('-',''),hash,items};save(requestKey,request);}
      const result=await post('/api/commandes',{requestId:request.id,items,delivery});
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
    const d=t.dataset;
    if(d.filter||d.categoryLink){filter=d.filter||d.categoryLink;renderProducts();return;}
    if(d.product){showProduct(d.product);return;}
    if(d.size&&currentProduct){showProduct(currentProduct.id,d.size);$('product-detail').querySelector('[aria-pressed="true"][data-size]')?.focus();return;}
    if(t.hasAttribute('data-close')){t.closest('dialog').close();return;}
    if(t.id==='cart-open'){open(cartDialog);try{await loadCatalog();await loadAccount();}catch(e){notify(e.message);}return;}
    if(t.id==='account-open'){await accountDialogOpen();return;}
    if(t.id==='add-to-cart'&&currentProduct&&selectedSize){
      const k=keyFor(currentProduct.id,selectedSize),n=cart.get(k)||0;
      if(n<sizeStock(currentProduct,selectedSize)){cart.set(k,n+1);updateCart();productDialog.close();notify(currentProduct.name+' · '+sizeLabel(selectedSize)+' ajouté au panier.');}return;
    }
    if(d.remove){cart.delete(d.remove);updateCart();return;}
    if(d.key){const [id,size]=d.key.split(':');const next=cart.get(d.key)+Number(d.step);
      if(next>=1&&(d.step==='-1'||next<=sizeStock(byId.get(id),size))){cart.set(d.key,next);updateCart();}return;}
    if(t.id==='photo-enlarge'&&currentProduct?.image){
      let box=$('photo-dialog'); if(!box){box=document.createElement('dialog');box.id='photo-dialog';box.className='lightbox-dialog';box.setAttribute('aria-label','Photo agrandie');document.body.append(box);}
      box.innerHTML='<button type="button" data-close class="dialog-close" aria-label="Fermer">×</button><img src="'+currentProduct.image+'" alt="'+esc(currentProduct.alt)+'">';open(box);return;
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
  async function init(){
    const saved=read(cartKey,{});
    for(const [key,n] of Object.entries(saved||{})) if(Number.isInteger(n)&&n>0&&n<=999)cart.set(key,n);
    try{await loadCatalog();}
    catch(e){$('product-grid').textContent='Le stock ne peut pas être chargé. Actualise la page pour réessayer.';$('results-count').textContent='Catalogue indisponible';}
    const query=new URLSearchParams(location.search);
    if(location.hash==='#compte'){
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
