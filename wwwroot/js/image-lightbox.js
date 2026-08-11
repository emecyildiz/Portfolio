(() => {
    const dialog = document.getElementById('imageLightbox');
    const triggers = Array.from(document.querySelectorAll('[data-lightbox-image]'));
    if (!dialog || triggers.length === 0) return;

    const image = dialog.querySelector('#imageLightboxImage');
    const caption = dialog.querySelector('#imageLightboxCaption');
    const counter = dialog.querySelector('[data-lightbox-counter]');
    const closeButton = dialog.querySelector('[data-lightbox-close]');
    const previousButton = dialog.querySelector('[data-lightbox-previous]');
    const nextButton = dialog.querySelector('[data-lightbox-next]');
    const items = [];
    let currentIndex = 0;
    let previousFocus = null;

    triggers.forEach((trigger) => {
        const src = trigger.dataset.lightboxSrc;
        if (!src) return;

        let index = items.findIndex((item) => item.src === src);
        if (index === -1) {
            index = items.length;
            items.push({
                src,
                alt: trigger.dataset.lightboxAlt || 'Project image',
                caption: trigger.dataset.lightboxCaption || trigger.dataset.lightboxAlt || 'Project image'
            });
        }

        trigger.dataset.lightboxIndex = String(index);
        trigger.addEventListener('click', () => open(index, trigger));
    });

    const render = () => {
        const item = items[currentIndex];
        image.src = item.src;
        image.alt = item.alt;
        caption.textContent = item.caption;
        counter.textContent = `${currentIndex + 1} / ${items.length}`;

        const hasMultipleImages = items.length > 1;
        previousButton.classList.toggle('hidden', !hasMultipleImages);
        nextButton.classList.toggle('hidden', !hasMultipleImages);
    };

    const open = (index, trigger) => {
        currentIndex = index;
        previousFocus = trigger;
        render();
        dialog.classList.remove('hidden');
        dialog.classList.add('flex');
        dialog.setAttribute('aria-hidden', 'false');
        document.body.classList.add('overflow-hidden');
        closeButton.focus();
    };

    const close = () => {
        dialog.classList.add('hidden');
        dialog.classList.remove('flex');
        dialog.setAttribute('aria-hidden', 'true');
        document.body.classList.remove('overflow-hidden');
        image.src = '';
        previousFocus?.focus();
        previousFocus = null;
    };

    const move = (step) => {
        currentIndex = (currentIndex + step + items.length) % items.length;
        render();
    };

    closeButton.addEventListener('click', close);
    previousButton.addEventListener('click', () => move(-1));
    nextButton.addEventListener('click', () => move(1));
    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) close();
    });

    document.addEventListener('keydown', (event) => {
        if (dialog.getAttribute('aria-hidden') !== 'false') return;

        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'ArrowLeft' && items.length > 1) {
            event.preventDefault();
            move(-1);
        } else if (event.key === 'ArrowRight' && items.length > 1) {
            event.preventDefault();
            move(1);
        } else if (event.key === 'Tab') {
            const focusable = Array.from(dialog.querySelectorAll('button:not(.hidden)'));
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }
    });
})();
