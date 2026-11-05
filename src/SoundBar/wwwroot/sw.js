const CACHE_NAME = 'soundbar-remote-v19';
const ASSETS = [
    '/',
    '/index.html',
    '/style.css?v=5',
    '/app.js?v=15',
    '/manifest.json?v=3'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(ASSETS))
    );
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) => {
            return Promise.all(
                keys.filter(key => key !== CACHE_NAME)
                    .map(key => caches.delete(key))
            );
        })
    );
    self.clients.claim();
});

self.addEventListener('fetch', (event) => {
    // We only want to cache our static UI files, NOT the WebSocket or API (album art, app icons).
    // Returning without responding lets the browser fetch those itself, straight from the PC.
    const url = new URL(event.request.url);
    if (url.pathname === '/ws' || url.pathname.startsWith('/api/')) return;

    event.respondWith(
        caches.match(event.request)
            .then((cachedResponse) => {
                // Return cached version if found
                if (cachedResponse) {
                    // Fetch in background to update cache for next time
                    fetch(event.request).then((response) => {
                        if (response.ok) {
                            caches.open(CACHE_NAME).then((cache) => {
                                cache.put(event.request, response);
                            });
                        }
                    }).catch(() => {});
                    
                    return cachedResponse;
                }

                // Otherwise fetch from network
                return fetch(event.request);
            })
    );
});
