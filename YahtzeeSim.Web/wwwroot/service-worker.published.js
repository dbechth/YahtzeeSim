// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.importScripts('./push-sw.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('message', event => {
    if (event.data === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});
// The account/leaderboard API (and its login redirects) must always go straight to the network:
// never answer it from the cache or with the cached index.html navigation fallback.
self.addEventListener('fetch', event => {
    if (new URL(event.request.url).pathname.startsWith('/api/')) {
        return;
    }

    event.respondWith(onFetch(event));
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/ ];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service worker: Install');
    self.skipWaiting();

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));

    await self.clients.claim();
}

// Network-first: always try for the latest files, fall back to the cache when offline or slow.
const networkTimeoutMs = 4000;

async function onFetch(event) {
    const request = event.request;
    if (request.method !== 'GET') {
        return fetch(request);
    }

    const isNavigation = request.mode === 'navigate' && !manifestUrlList.some(url => url === request.url);
    const cacheKey = isNavigation ? 'index.html' : request;
    const cache = await caches.open(cacheName);

    try {
        const response = await fetchWithTimeout(request, cache, cacheKey);
        if (response.ok && (isNavigation || manifestUrlList.includes(request.url))) {
            cache.put(cacheKey, response.clone());
        }

        return response;
    } catch (error) {
        const cachedResponse = await cache.match(cacheKey);
        if (cachedResponse) {
            return cachedResponse;
        }

        throw error;
    }
}

async function fetchWithTimeout(request, cache, cacheKey) {
    const hasCachedCopy = !!(await cache.match(cacheKey));
    if (!hasCachedCopy) {
        return fetch(request);
    }

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), networkTimeoutMs);
    try {
        return await fetch(request, { signal: controller.signal });
    } finally {
        clearTimeout(timer);
    }
}
