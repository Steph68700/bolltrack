(() => {
  'use strict';
  const key = 'bolltrack.session.v1';
  let current;
  window.bollTrackSession = accountId => {
    current = accountId || '';
    try {
      if (localStorage.getItem(key) !== current) localStorage.setItem(key, current);
    } catch { /* Le compte reste utilisable si le stockage est désactivé. */ }
  };
  window.addEventListener('storage', event => {
    if (event.key === key && current !== undefined && event.newValue !== current) location.reload();
  });
  window.addEventListener('pageshow', event => {
    if (event.persisted) location.reload();
  });
})();
