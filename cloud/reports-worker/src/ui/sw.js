// Makes the reports open instantly and keeps the last numbers for when the phone has no signal. The site's files are
// served from here and refreshed in the background; the reports always come from the internet first, and the saved copy
// is only used offline (marked so the page can say so). Signing out forgets the saved reports.
const SHELL = "shell-v4";
const DATA = "data-v1";
const FILES = ["/", "/app.js", "/charts.js", "/dom.js", "/format.js", "/i18n.js", "/icons.js", "/insights.js", "/pages.js", "/app.css", "/icon.svg", "/manifest.webmanifest", "/fonts/inter.woff2"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(SHELL).then((cache) => cache.addAll(FILES)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (event) => {
  event.waitUntil(caches.keys()
    .then((keys) => Promise.all(keys.filter((k) => k !== SHELL && k !== DATA).map((k) => caches.delete(k))))
    .then(() => self.clients.claim()));
});

self.addEventListener("message", (event) => {
  if (event.data === "signed-out") caches.delete(DATA);
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);
  if (event.request.method !== "GET" || url.origin !== location.origin) return;

  // Reports and who is signed in: internet first, the saved copy only offline.
  if (url.pathname === "/api/me" || url.pathname === "/api/docs") {
    event.respondWith(fetch(event.request).then((response) => {
      if (response.ok) {
        const copy = response.clone();
        caches.open(DATA).then((cache) => cache.put(event.request, copy));
      } else if (response.status === 401) {
        caches.delete(DATA);
      }
      return response;
    }).catch(async () => {
      const saved = await caches.match(event.request, { cacheName: DATA });
      if (!saved) throw new Error("offline");
      const headers = new Headers(saved.headers);
      headers.set("X-Offline", "1");
      return new Response(await saved.blob(), { status: 200, headers });
    }));
    return;
  }
  if (url.pathname.startsWith("/api/")) return; // sign-in, polling: never cached

  // The site's own files: straight from the saved copy, refreshed in the background.
  event.respondWith(caches.open(SHELL).then(async (cache) => {
    const saved = await cache.match(event.request, { ignoreSearch: true });
    const fresh = fetch(event.request).then((response) => {
      if (response.ok) cache.put(event.request, response.clone());
      return response;
    }).catch(() => saved);
    return saved ?? fresh;
  }));
});
