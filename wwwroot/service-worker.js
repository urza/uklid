self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(clients.claim());
});

self.addEventListener('fetch', (event) => {
    // Network-first strategy for SSR app
    event.respondWith(
        fetch(event.request)
            .catch(() => caches.match(event.request))
    );
});
