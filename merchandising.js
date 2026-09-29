const merchLightbox = document.getElementById('merch-lightbox');
const merchLightboxImage = merchLightbox.querySelector('.merch-lightbox-image');

document.querySelectorAll('.merch-image-link').forEach((link) => {
  link.addEventListener('click', (event) => {
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey ||
        typeof merchLightbox.showModal !== 'function') {
      return;
    }

    event.preventDefault();
    merchLightboxImage.src = link.href;
    merchLightboxImage.alt = link.querySelector('img').alt;
    merchLightbox.showModal();
    document.body.classList.add('merch-lightbox-open');
  });
});

merchLightbox.querySelector('.merch-lightbox-close').addEventListener('click', () => {
  merchLightbox.close();
});

merchLightbox.addEventListener('click', (event) => {
  if (event.target === merchLightbox) {
    merchLightbox.close();
  }
});

merchLightbox.addEventListener('close', () => {
  document.body.classList.remove('merch-lightbox-open');
});
