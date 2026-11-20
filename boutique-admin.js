(() => {
  'use strict';
  if (document.documentElement.dataset.shopHost === 'static' &&
      ['www.bolltrack.fr','bolltrack.fr','steph68700.github.io'].includes(location.hostname)) {
    location.replace('https://bolltrack-production.up.railway.app/boutique-admin.html');
    return;
  }
  const $ = id => document.getElementById(id);
  const el = (tag, text, cls) => {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (cls) node.className = cls;
    return node;
  };
  const money = n => (n / 100).toLocaleString('fr-FR',{style:'currency',currency:'EUR'});
  const date = s => new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(s) ? s : s+'Z').toLocaleString('fr-FR');
  const uuid = () => crypto.randomUUID().replaceAll('-','');
  const endpoint = '/api/admin/';
  const productForm = $('product-form'), stockForm = $('stock-form');
  const defaultsForm = $('parcel-defaults');
  const dimensions = ['lengthCm','widthCm','heightCm'];
  const defaultFields = ['unitWeightGrams','packagingGrams',...dimensions];
  const optionalNumber = field => field.value === '' ? null : field.valueAsNumber;
  const reloadDefaults = $('reload-defaults');
  let defaultsRevision = null;
  let products = [], edited = null, newId = '', page = 0, ordersVersion = 0, stockVersion = 0, pendingStock, previewUrl;
  let productRevision = 0;
  const notice = s => { $('feedback').textContent = s; };
  function deny(status) {
    $('workspace').hidden = true;
    $('access').hidden = false;
    $('access').textContent = status === 401 ? 'Connecte-toi avec ton compte Entreli pour accéder à la gestion de la boutique.' : 'L’administration est réservée au compte Entreli habilité.';
    $('session-email').hidden = true;
    $('login').textContent = status === 401 ? 'Me connecter avec Entreli' : 'Changer de compte';
    $('login').hidden = false;
  }
  async function api(path, body) {
    const opts = {credentials:'same-origin', cache:'no-store'};
    if (body !== undefined) Object.assign(opts, {method:'POST',headers:{'Content-Type':'application/json','X-BollTrack':'1'},body:JSON.stringify(body)});
    const r=await fetch(endpoint+path,opts);
    if (r.status===401 || r.status===403) deny(r.status);
    if (!r.ok) {
      const info = await r.json().catch(()=>({}));
      const error=new Error(info.error || 'Action indisponible. Actualise la page pour réessayer.');
      error.status=r.status;
      throw error;
    }
    return r.status===204 ? null : r.json();
  }
  const handled = fn => async event => {try {await fn(event);} catch(error){notice(error.message);}};
  function submit(form, fn) {
    form.addEventListener('submit', handled(async event=>{
      event.preventDefault();
      const btn=form.querySelector('[type=submit]');
      if(btn.disabled) return;
      btn.disabled=true;
      try {await fn();} finally {btn.disabled=false;}
    }));
  }
  const optionLabel = p => p.name+(p.fit && !p.name.endsWith(p.fit)?' · '+p.fit:'')+(p.published?'':' · Masqué');
  async function loadProducts() {
    const rows=await api('produits');
    products=rows;
    for(const select of [$('product-select'),$('stock-product')]) {
      const previous=select.value;
      select.replaceChildren(...rows.map(p=>new Option(optionLabel(p),p.id)));
      if(select.id==='product-select') select.add(new Option('Nouveau produit',''),0);
      if(rows.some(p=>p.id===previous)) select.value=previous;
    }
  }
  function row(size='', quantity=0) {
    const group=el('div',undefined,'initial-row');
    const sizeLabel=el('label','Taille'), qtyLabel=el('label','Quantité');
    const sizeInput=el('input'), qtyInput=el('input'), remove=el('button','Retirer');
    sizeInput.name='size'; sizeInput.maxLength=12;sizeInput.required=true;sizeInput.value=size;
    qtyInput.name='count';qtyInput.type='number';qtyInput.min=0;qtyInput.max=1000000;qtyInput.required=true;qtyInput.value=quantity;
    remove.type='button';remove.addEventListener('click',()=>group.remove());
    sizeLabel.append(sizeInput);qtyLabel.append(qtyInput);group.append(sizeLabel,qtyLabel,remove);$('initial-rows').append(group);
  }
  function preview(url) {
    if(previewUrl) {URL.revokeObjectURL(previewUrl);previewUrl=null;}
    const picture=$('preview');
    picture.hidden=!url;
    if(url) picture.src=url; else picture.removeAttribute('src');
  }
  function edit(id) {
    edited=products.find(p=>p.id===id) || null;
    productRevision=edited?.revision || 0; newId=edited?'':uuid();
    $('product-select').value=edited?.id || '';
    productForm.reset();
    const fields=productForm.elements;
    for(const key of ['name','collection','color','fit']) fields.namedItem(key).value=edited?.[key] || '';
    fields.category.value=edited?.category || 'tshirts';
    fields.price.value=edited ? (edited.priceCents/100).toFixed(2) : '';
    fields.unitWeightGrams.value=edited?.unitWeightGrams ?? '';
    fields.published.checked=edited?.published ?? false;
    $('initial').hidden=!!edited;$('initial').disabled=!!edited;
    $('initial-rows').replaceChildren();
    if(!edited) row('TU');
    preview(edited?.image);
  }
  $('new-product').addEventListener('click',()=>{notice('');edit('');productForm.elements.namedItem('name').focus();});
  $('product-select').addEventListener('change',e=>{notice('');edit(e.target.value);});
  $('add-size').addEventListener('click',()=>row());
  productForm.elements.photo.addEventListener('change',event=>{
    const file=event.target.files[0];
    const next=file?URL.createObjectURL(file):null;
    preview(next || edited?.image);previewUrl=next;
  });
  async function photoData(file) {
    if(!file) return null;
    if(file.size>16*1024*1024 || !['image/jpeg','image/png','image/webp'].includes(file.type))
      throw new Error('Choisis une image JPEG, PNG ou WebP de moins de 16 Mo.');
    const bitmap=await createImageBitmap(file);
    try {
      const ratio=Math.min(1,1600/Math.max(bitmap.width,bitmap.height));
      const canvas=el('canvas');canvas.width=Math.max(1,Math.round(bitmap.width*ratio));canvas.height=Math.max(1,Math.round(bitmap.height*ratio));
      const ctx=canvas.getContext('2d');ctx.fillStyle='#fff';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.drawImage(bitmap,0,0,canvas.width,canvas.height);
      return canvas.toDataURL('image/jpeg',.84).split(',')[1];
    } finally {bitmap.close();}
  }
  submit(productForm,async()=>{
    const f=productForm.elements, id=edited?.id || newId;
    const body={revision:productRevision,name:f.namedItem('name').value,category:f.category.value,collection:f.collection.value,color:f.color.value,fit:f.fit.value,
      priceCents:Math.round(f.price.valueAsNumber*100),published:f.published.checked,photo:await photoData(f.photo.files[0]),
      shipping:{unitWeightGrams:optionalNumber(f.unitWeightGrams)}};
    if(!edited) body.initialStock=[...$('initial-rows').children].map(r=>({sizeCode:r.querySelector('[name=size]').value,quantity:r.querySelector('[name=count]').valueAsNumber}));
    await api('produits/'+encodeURIComponent(id),body);
    await loadProducts();edit(id);notice('Fiche produit enregistrée.');
  });
  function selectedStock() {return products.find(p=>p.id===$('stock-product').value);}
  async function showStock() {
    const version=++stockVersion,p=selectedStock();
    $('balances').replaceChildren();$('known-sizes').replaceChildren();$('movements').textContent='';
    if(!p) return;
    for(const s of p.sizes) {
      $('balances').append(el('li',s.sizeCode+' · '+s.available));
      $('known-sizes').append(new Option(s.sizeCode,s.sizeCode));
    }
    if(!p.sizes.some(s=>s.sizeCode===stockForm.elements.sizeCode.value)) stockForm.elements.sizeCode.value=p.sizes[0]?.sizeCode || '';
    const rows=await api('mouvements?productId='+encodeURIComponent(p.id));
    if(version!==stockVersion) return;
    if(!rows.length){$('movements').textContent='Aucun ajustement manuel enregistré pour ce produit.';return;}
    const list=el('ul',undefined,'history');
    for(const m of rows) list.append(el('li',date(m.createdUtc)+' · '+m.sizeCode+' · '+(m.delta>0?'+':'')+m.delta+' · '+m.reason+' · Stock après mouvement : '+m.balanceAfter));
    $('movements').append(list);
  }
  $('stock-product').addEventListener('change',handled(async()=>{stockForm.reset();await showStock();}));
  submit(stockForm,async()=>{
    const p=selectedStock();if(!p) return;
    const f=stockForm.elements,body={sizeCode:f.sizeCode.value.trim().toUpperCase(),delta:Number(f.direction.value)*f.quantity.valueAsNumber,reason:f.reason.value.trim()};
    const signature=JSON.stringify([p.id,body]);
    if(pendingStock?.signature!==signature) pendingStock={signature,id:uuid()};
    await api('produits/'+encodeURIComponent(p.id)+'/stock',{...body,operationId:pendingStock.id});
    pendingStock=null;f.quantity.value='';f.reason.value='';
    await loadProducts();await showStock();notice('Mouvement enregistré.');
  });
  const orderStatus={paid:'Payée',pending:'Paiement en attente',expired:'Paiement clôturé'};
  const fulfillmentLabels={preparing:'En préparation',shipped:'Expédiée'};
  const returnStatus={requested:'Demande reçue',processing:'En cours',received:'Articles reçus',closed:'Dossier clôturé'};
  async function loadParcelDefaults() {
    if (reloadDefaults.disabled) return;
    reloadDefaults.disabled = true;
    const controls = defaultsForm.querySelectorAll('fieldset, button');
    for (const control of controls) control.disabled = true;
    try {
      const settings = await api('colis-par-defaut');
      defaultsRevision = settings.revision;
      for (const name of defaultFields) defaultsForm.elements[name].value = settings[name] ?? '';
    } finally {
      reloadDefaults.disabled = false;
      for (const control of controls) control.disabled = defaultsRevision === null;
    }
  }
  reloadDefaults.addEventListener('click',handled(loadParcelDefaults));
  submit(defaultsForm,async()=>{
    const body = {revision:defaultsRevision};
    for (const name of defaultFields) body[name] = optionalNumber(defaultsForm.elements[name]);
    reloadDefaults.disabled = true;
    try {
      const saved = await api('colis-par-defaut',body);
      defaultsRevision = saved.revision;
      notice('Valeurs par défaut enregistrées.');
    } finally { reloadDefaults.disabled = false; }
  });
  function orderForm(label,control,action,save) {
    const form=el('form'), field=el('label',label), btn=el('button',action);
    btn.type='submit';field.append(control);form.append(field,btn);
    submit(form,async()=>{await save(control.value);await loadOrders();notice('Commande mise à jour.');});
    return form;
  }
  function labelForm(order, configured) {
    const form=$('shipment-label-form').content.firstElementChild.cloneNode(true);
    const fields=form.elements;
    const suggestion = order.suggestedParcel || {};
    for (const name of ['weightGrams',...dimensions]) fields[name].value = suggestion[name] ?? '';
    form.querySelector('.weight-hint').textContent = suggestion.weightGrams != null
      ? 'Poids estimé à partir des articles et de l’emballage. Ajuste-le si le colis préparé diffère.'
      : 'Saisis le poids mesuré. Pour le préremplir, renseigne le poids des articles et de l’emballage dans les réglages.';
    const details = form.querySelector('details'), summary = details.querySelector('summary');
    const dimensionsTitle = summary.textContent;
    function showDimensions() {
      const values = dimensions.map(name => fields[name].value);
      summary.textContent = values.every(Boolean) ? 'Dimensions du colis · '+values.join(' × ')+' cm' : dimensionsTitle;
    }
    details.addEventListener('input',showDimensions);
    showDimensions();
    form.querySelector('[type=submit]').disabled=!configured;
    submit(form,async()=>{
      const parcel={weightGrams:fields.weightGrams.valueAsNumber};
      for(const name of dimensions) {
        if(fields[name].value) parcel[name]=fields[name].valueAsNumber;
      }
      let label;
      try {
        label=await api('commandes/'+encodeURIComponent(order.id)+'/etiquette',parcel);
      } catch(error) {
        if(error.status===409 || error.status===502) await loadOrders();
        throw error;
      }
      await loadOrders();
      notice('Étiquette disponible. Le suivi '+label.trackingNumber+' est enregistré dans le compte client.');
    });
    return form;
  }
  function orderView(o, labelsConfigured) {
    const article=el('article',undefined,'order'), recipient=o.delivery?.recipient || {}, locker=o.delivery?.locker;
    article.append(el('h3',date(o.createdUtc)+' · '+money(o.totalCents)),el('p','Commande '+o.id+' · '+(orderStatus[o.status] || o.status)));
    const list=el('ul');
    for(const item of o.lines) list.append(el('li',item.name+' · '+item.sizeCode+' × '+item.quantity));
    article.append(list,el('p',[recipient.name,o.email,recipient.phone].filter(Boolean).join(' · ')));
    const addr=locker ? ['Locker '+locker.id+' · '+locker.name,locker.street,[locker.postcode,locker.city].join(' ')] :
      ['Livraison à domicile',recipient.street,recipient.complement,[recipient.postcode,recipient.city].filter(Boolean).join(' ')];
    article.append(el('p',addr.filter(Boolean).join('\n'),'address'));
    article.append(el('p','Port facturé au client · '+money(o.shippingCents)));
    if(o.fulfillmentStatus) article.append(el('p','Commande · '+fulfillmentLabels[o.fulfillmentStatus]));
    if(o.canSetFulfillment) {
      const select=el('select');
      select.required=true;
      select.add(new Option('Choisir un état',''));
      for(const [value,label] of Object.entries(fulfillmentLabels)) {
        if(value!==o.fulfillmentStatus) select.add(new Option(label,value));
      }
      article.append(orderForm('État de la commande',select,'Enregistrer l’état',status=>
        api('commandes/'+encodeURIComponent(o.id)+'/expedition',{status,previous:o.fulfillmentStatus})));
      article.append(el('p','Choisis « Expédiée » une fois le colis remis au transporteur.'));
    }
    if(o.refundedCents) article.append(el('p','Remboursé par Stripe · '+money(o.refundedCents)));
    if(o.tracking) article.append(el('p','Suivi Mondial Relay · '+o.tracking));
    if(o.labelUrl) {
      const link=el('a','Ouvrir l’étiquette PDF','action');
      link.href=o.labelUrl;link.target='_blank';link.rel='noopener noreferrer';
      article.append(link);
    } else if(o.shippingStatus==='review' || o.shippingStatus==='processing') {
      article.append(el('p','Émission en cours ou à vérifier dans Mondial Relay Connect. Référence marchand : '+o.id.slice(0,15).toUpperCase()+'.'));
    } else if(o.canCreateLabel) {
      article.append(labelForm(o,labelsConfigured));
    }
    if(o.canSetTracking) {
      const number=el('input');number.inputMode='numeric';number.pattern='[0-9]{6,20}';number.required=true;number.value=o.tracking || '';
      article.append(orderForm('Numéro de suivi Mondial Relay',number,'Enregistrer le suivi',value=>api('commandes/'+encodeURIComponent(o.id)+'/suivi',{number:value,previous:o.tracking})));
    }
    for(const n of o.withdrawals || []) article.append(el('h4','Rétractation du '+date(n.submittedUtc)),el('p',n.firstName+' '+n.lastName+' · '+n.details,'address'));
    const req=o.returnRequest;
    if(req) {
      article.append(el('h4','Retour · '+(returnStatus[req.status] || req.status)),el('p',req.details,'address'));
      const steps=({requested:['processing','received','closed'],processing:['received','closed'],received:['closed']})[req.status] || [];
      if(steps.length) {
        const select=el('select');select.required=true;select.add(new Option('Choisir un statut',''));
        steps.forEach(s=>select.add(new Option(returnStatus[s],s)));
        article.append(orderForm('Traitement du retour',select,'Mettre à jour le retour',value=>api('commandes/'+encodeURIComponent(o.id)+'/retour',{status:value,previous:req.status})));
      }
    }
    return article;
  }
  async function loadOrders() {
    const version=++ordersVersion;
    $('orders').textContent='Chargement des commandes…';$('next').disabled=true;$('previous').disabled=true;
    const data=await api('commandes?filter='+encodeURIComponent($('order-filter').value)+'&page='+page);
    if(version!==ordersVersion) return;
    $('label-setup').hidden=data.labelsConfigured;
    $('label-sandbox').hidden=!data.labelSandbox;
    $('orders').replaceChildren(...data.orders.map(o=>orderView(o,data.labelsConfigured)));
    if(!data.orders.length) $('orders').textContent='Aucune commande pour ce filtre.';
    $('previous').disabled=page===0;$('next').disabled=!data.more;$('page').textContent='Page '+(page+1);
  }
  $('order-filter').addEventListener('change',handled(async()=>{page=0;await loadOrders();}));
  $('reload-orders').addEventListener('click',handled(loadOrders));
  $('previous').addEventListener('click',handled(async()=>{page--;await loadOrders();}));
  $('next').addEventListener('click',handled(async()=>{page++;await loadOrders();}));
  document.querySelector('nav').addEventListener('click',handled(async event=>{
    const tab=event.target.closest('[data-tab]')?.dataset.tab;if(!tab) return;
    notice('');
    document.querySelectorAll('[data-tab]').forEach(b=>{const active=b.dataset.tab===tab;b.setAttribute('aria-pressed',String(active));$(b.dataset.tab).hidden=!active;});
    if(tab==='stock') {await loadProducts();await showStock();}
    if(tab==='commandes') await loadOrders();
    if(tab==='colis' && defaultsRevision===null) await loadParcelDefaults();
  }));
  async function init() {
    try {
      const response=await fetch('/api/compte/session',{credentials:'same-origin',cache:'no-store'});
      if(!response.ok) throw new Error('Impossible de vérifier le compte Entreli. Actualise la page pour réessayer.');
      const session=await response.json();
      window.bollTrackSession(session.accountId);
      if(!session.isAdmin) deny(session.accountId ? 403 : 401);
      if(session.email) {
        $('session-email').textContent='Compte Entreli · '+session.email;
        $('session-email').hidden=false;
      }
      if(!session.isAdmin) return;
      $('login').textContent='Changer de compte';$('login').hidden=false;
      await loadProducts();edit(products[0]?.id || '');
      $('workspace').hidden=false;$('access').hidden=true;
      if(new URLSearchParams(location.search).has('connexion')) notice('Connexion interrompue. Tu peux réessayer.');
    } catch(error){if($('access').textContent.startsWith('Vérification')) $('access').textContent=error.message;}
  }
  init();
})();
