// Challenge notifications (Web Push), imported by both service workers.
// The server sends JSON: { title, body, url, tag } (see functions/_lib/notify.ts).
self.addEventListener('push', event => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch {
        data = { body: event.data ? event.data.text() : '' };
    }

    const options = {
        body: data.body || '',
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        data: { url: data.url || '/' }
    };
    if (data.tag) {
        // A newer message about the same challenge replaces the older one but still alerts.
        options.tag = data.tag;
        options.renotify = true;
    }

    event.waitUntil(self.registration.showNotification(data.title || 'YahtzeeSim', options));
});

// Tapping a notification opens the challenge: in an open window if there is one, otherwise in a new one.
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL((event.notification.data && event.notification.data.url) || '/', self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).origin === self.location.origin) {
                await client.focus();
                client.postMessage({ type: 'yahtzeesim-open', url });
                return;
            }
        }

        await self.clients.openWindow(url);
    })());
});
