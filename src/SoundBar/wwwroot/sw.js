const CACHE_NAME = 'soundbar-remote-v11';
const ASSETS = [
    '/',
    '/index.html',
    '/style.css?v=4',
    '/app.js?v=8',
    '/manifest.json?v=2'
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
    // We only want to cache our static UI files, NOT the WebSocket or API.
    if (event.request.url.includes('/ws')) return;

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
