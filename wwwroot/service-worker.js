self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(clients.claim());
});

// Pro SSR aplikaci - jen pass-through, necachujeme
self.addEventListener('fetch', (event) => {
    // Neinterferujeme s požadavky - necháme je projít na server
    return;
});
