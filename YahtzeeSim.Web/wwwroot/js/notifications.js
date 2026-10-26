// Challenge notifications on this device (Web Push). Used by NotificationService.cs.
window.yahtzeeSimPush = (function () {
    const installed = () =>
        ['standalone', 'fullscreen', 'minimal-ui'].some(m => window.matchMedia('(display-mode: ' + m + ')').matches)
        || window.navigator.standalone === true;
    const isIos = () => /iPad|iPhone|iPod/.test(navigator.userAgent || '')
        || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;

    const toKey = text => {
        const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((text.length + 3) % 4);
        return Uint8Array.from(atob(padded), c => c.charCodeAt(0));
    };
    const toText = buffer => btoa(String.fromCharCode(...new Uint8Array(buffer)))
        .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    const describe = sub => ({
        endpoint: sub.endpoint,
        p256dh: toText(sub.getKey('p256dh')),
        auth: toText(sub.getKey('auth'))
    });
    // serviceWorker.ready never settles when the worker failed to install, so never wait on it forever.
    const registration = (ms = 10000) => Promise.race([
        navigator.serviceWorker.ready,
        new Promise((_, reject) => setTimeout(() => reject(new Error('no-worker')), ms))
    ]);

    return {
        // "unsupported": this browser cannot do push; "needs-install": iPhone/iPad outside the Home Screen app.
        status: async () => {
            if (!supported()) {
                return { state: isIos() && !installed() ? 'needs-install' : 'unsupported', subscribed: false };
            }
            let subscribed = false;
            try {
                subscribed = !!(await (await registration(3000)).pushManager.getSubscription());
            } catch { }
            return { state: Notification.permission, subscribed: subscribed && Notification.permission === 'granted' };
        },

        // Must run straight from a tap: iOS only shows the permission prompt for a user gesture.
        subscribe: async publicKey => {
            const permission = await Notification.requestPermission();
            if (permission !== 'granted') {
                return { error: permission };
            }
            let reg;
            try {
                reg = await registration();
            } catch {
                return { error: 'no-worker' };
            }
            let sub = await reg.pushManager.getSubscription();
            if (!sub) {
                sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: toKey(publicKey) });
            }
            return describe(sub);
        },

        // Returns the endpoint that was removed (or null) so the server can forget it too.
        unsubscribe: async () => {
            let sub = null;
            try {
                sub = await (await registration(3000)).pushManager.getSubscription();
            } catch { }
            if (!sub) {
                return null;
            }
            const endpoint = sub.endpoint;
            await sub.unsubscribe();
            return endpoint;
        },

        // The service worker posts here when a notification is tapped while the app is already open.
        listen: dotNetRef => {
            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.addEventListener('message', event => {
                    if (event.data && event.data.type === 'yahtzeesim-open') {
                        dotNetRef.invokeMethodAsync('OnNotificationOpened', event.data.url);
                    }
                });
            }
        },

        share: async (title, text, url) => {
            if (navigator.share) {
                try {
                    await navigator.share({ title, text, url });
                    return 'shared';
                } catch (e) {
                    if (e && e.name === 'AbortError') {
                        return 'cancelled';
                    }
                }
            }
            try {
                await navigator.clipboard.writeText(url);
                return 'copied';
            } catch {
                return 'failed';
            }
        }
    };
})();
