// The reports website: sign-in, the shell (sidebar, top bar, tab bar), page routing and loading the shop's reports.
import { h, $, bdi } from "./dom.js";
import { t, setLanguage, language } from "./i18n.js";
import { setFormat, clock } from "./format.js";
import { icon } from "./icons.js";
import { PAGES } from "./pages.js";

const TABS = ["overview", "analytics", "sales", "drawers", "stock"];
const OFFLINE_AFTER_MS = 15 * 60_000;

// How often a visible page asks whether there is anything new (one tiny request; the reports themselves are only
// downloaded when the shop sent something new). Slower when nothing is happening, never while the page is hidden.
const POLL_MS = 2 * 60_000;
const POLL_QUIET_MS = 10 * 60_000;

const state = {
  me: null,
  publicInfo: null,
  page: "overview",
  report: "period:today",
  compare: true,
  docs: {},
  version: 0,
  lastChange: Date.now(),
  offline: false,
  ui: {}, // per-page choices (sort, filters, search)
};

// ---- Preferences --------------------------------------------------------------------------------------------------

const store = {
  get(key) { try { return localStorage.getItem(key); } catch { return null; } },
  set(key, value) { try { localStorage.setItem(key, value); } catch { /* private mode */ } },
};

function applyTheme(theme) {
  const dark = theme ? theme === "dark" : window.matchMedia?.("(prefers-color-scheme: dark)").matches;
  document.documentElement.dataset.theme = dark ? "dark" : "light";
  document.querySelector('meta[name="theme-color"]')?.setAttribute("content", dark ? "#0a0c12" : "#f6f7fb");
}

function toggleTheme() {
  const next = document.documentElement.dataset.theme === "dark" ? "light" : "dark";
  store.set("theme", next);
  applyTheme(next);
  rerender();
}

function useLanguage(lang) {
  setLanguage(lang);
  setFormat(language(), state.me?.store?.currency);
}

function toggleLanguage() {
  const next = language() === "ar" ? "en" : "ar";
  store.set("lang", next);
  useLanguage(next);
  if (state.me) rerender();
  else showLogin();
}

// ---- Server -------------------------------------------------------------------------------------------------------

class SignedOut extends Error {}

async function api(path, options) {
  const response = await fetch(path, { credentials: "same-origin", ...options });
  if (response.status === 401 && path !== "/api/login") throw new SignedOut();
  const body = await response.json().catch(() => ({}));
  if (response.headers.get("X-Offline") === "1") state.offline = true;
  else if (path.startsWith("/api/me") || path.startsWith("/api/docs")) state.offline = false;
  return { status: response.status, body };
}

async function loadMe() {
  const { status, body } = await api("/api/me");
  if (status !== 200) throw new Error(String(status));
  state.me = body;
  state.version = body.version ?? 0;
  setFormat(language(), body.store?.currency);
}

async function loadDocs(keys) {
  const wanted = [...new Set(keys.filter((k) => k && !(k in state.docs)))];
  for (let i = 0; i < wanted.length; i += 10) {
    const chunk = wanted.slice(i, i + 10);
    const { status, body } = await api(`/api/docs?keys=${encodeURIComponent(chunk.join(","))}`);
    if (status !== 200) throw new Error(String(status));
    for (const key of chunk) state.docs[key] = body.docs?.[key]?.body ?? null;
  }
}

// ---- Sign in ------------------------------------------------------------------------------------------------------

/**
 * The browser does the slow part of the password check (the same PBKDF2 the POS uses), so the server only compares a
 * hash: that fits Cloudflare's free plan, and the password itself never leaves the phone.
 */
async function passwordProof(username, password) {
  const { status, body } = await api(`/api/salt?u=${encodeURIComponent(username)}`);
  if (status === 429) throw Object.assign(new Error("slow"), { slow: true });
  if (status !== 200) throw new Error("salt");
  const salt = Uint8Array.from(atob(body.salt), (c) => c.charCodeAt(0));
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt, iterations: body.iterations }, key, 256);
  return btoa(String.fromCharCode(...new Uint8Array(bits)));
}

const initials = (name) => (name ?? "").trim().split(/\s+/).slice(0, 2).map((w) => w[0] ?? "").join("").toUpperCase() || "•";

function showLogin(message) {
  state.me = null;
  state.docs = {};
  $("app").classList.add("hidden");
  const view = $("login");
  view.classList.remove("hidden");
  const shop = state.publicInfo?.name;
  document.title = shop ? `${shop} · ${t("appName")}` : t("appName");

  const error = h("p", { class: "form-error", role: "alert" }, message ?? "");
  const username = h("input", { class: "input", id: "username", name: "username", autocomplete: "username", autocapitalize: "none", spellcheck: "false", required: true });
  const password = h("input", { class: "input", id: "password", name: "password", type: "password", autocomplete: "current-password", required: true });
  const eye = h("button", { type: "button", class: "eye", "aria-label": t("showPassword"), onclick: () => { password.type = password.type === "password" ? "text" : "password"; } }, icon("eye"));
  const submit = h("button", { class: "btn-primary", type: "submit" }, t("signIn"));

  const form = h("form", {
    novalidate: true,
    onsubmit: async (e) => {
      e.preventDefault(); // signed in with fetch: the password is never posted as a form
      error.textContent = "";
      const user = username.value.trim();
      if (!user || !password.value) return;
      submit.disabled = true;
      submit.textContent = t("signingIn");
      try {
        const proof = await passwordProof(user, password.value);
        const { status, body } = await api("/api/login", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ username: user, proof }),
        });
        if (status === 200) {
          password.value = "";
          await start();
          return;
        }
        error.textContent = status === 429 ? t("locked", body.minutes ?? 1)
          : status === 401 ? (body.left > 0 && body.left < 5 ? t("wrongLeft", body.left) : t("wrong"))
          : t("serverError");
      } catch (err) {
        error.textContent = err.slow ? t("locked", 1) : err instanceof TypeError ? t("offlineError") : t("serverError");
      }
      submit.disabled = false;
      submit.textContent = t("signIn");
    },
  },
    h("div", { class: "field" }, h("label", { for: "username" }, t("username")), username),
    h("div", { class: "field" }, h("label", { for: "password" }, t("password")), h("div", { class: "input-wrap" }, password, eye)),
    error,
    submit);

  view.replaceChildren(
    h("section", { class: "auth-brand", "aria-hidden": "true" },
      h("div", { style: "display:flex;gap:12px;align-items:center" }, h("div", { class: "mark lg" }, initials(shop ?? t("appName"))),
        h("strong", { style: "font-size:17px" }, shop ? bdi(shop) : t("appName"))),
      h("div", null,
        h("h2", null, t("brandTagline")),
        h("ul", null, ["brandPoint1", "brandPoint2", "brandPoint3"].map((k, i) => h("li", null, icon(["trend", "drawers", "stock"][i], 20), t(k))))),
      h("div", { style: "font-size:13px;opacity:.6" }, t("appName"))),
    h("section", { class: "auth-panel" },
      h("div", { class: "auth-card" },
        h("div", { class: "auth-top" },
          h("div", { style: "display:flex;gap:12px;align-items:center;min-width:0" },
            h("div", { class: "mark" }, initials(shop ?? t("appName"))),
            h("div", { style: "min-width:0" }, h("strong", { style: "display:block" }, shop ? bdi(shop) : t("appName")), h("span", { class: "muted", style: "font-size:12.5px" }, t("appName")))),
          h("div", { class: "tool-row" },
            h("button", { class: "tool", type: "button", onclick: toggleLanguage }, icon("globe", 16), t("language")),
            h("button", { class: "tool", type: "button", "aria-label": "theme", onclick: () => { toggleTheme(); showLogin(); } }, icon(document.documentElement.dataset.theme === "dark" ? "sun" : "moon", 16)))),
        h("h1", null, shop ? `${t("appName")}` : t("appName")),
        h("p", { class: "lead" }, t("signInHint")),
        form,
        h("div", { class: "secure" }, icon("eye", 14), t("secureNote")),
        h("p", { class: "owner-note" }, shop ? t("ownerNote", shop) : t("ownerNoteGeneric")))));
  username.focus();
}

async function signOut() {
  closeSheet();
  await fetch("/api/logout", { method: "POST" }).catch(() => {});
  navigator.serviceWorker?.controller?.postMessage("signed-out"); // forget the reports saved for offline use
  showLogin();
}

// ---- Shell --------------------------------------------------------------------------------------------------------

async function start() {
  try {
    await loadMe();
  } catch (err) {
    if (err instanceof SignedOut) return showLogin();
    $("login").classList.add("hidden");
    $("app").classList.remove("hidden");
    $("content").replaceChildren(emptyState("wifi", err instanceof TypeError ? t("offlineError") : t("serverError")));
    return;
  }
  if (!store.get("lang") && state.me.store?.language) useLanguage(state.me.store.language);
  $("login").classList.add("hidden");
  $("app").classList.remove("hidden");
  readHash();
  await show();
  schedulePoll();
}

function readHash() {
  const [page, ...rest] = location.hash.slice(1).split("/");
  state.page = TABS.includes(page) ? page : "overview";
  if (state.page === "analytics" && rest.length) state.report = decodeURIComponent(rest.join("/"));
}

export function navigate(page, report) {
  location.hash = report ? `${page}/${encodeURIComponent(report)}` : page;
}

function renderShell(page) {
  const me = state.me;
  const shop = me.store?.name ?? t("appName");
  document.title = `${t(`nav_${state.page}`)} · ${shop}`;
  const navLinks = (cls) => TABS.map((tab) =>
    h("a", { href: `#${tab}`, class: `${cls} ${tab === state.page ? "active" : ""}` }, icon(tab, cls === "nav-item" ? 18 : 20), h("span", null, t(`nav_${tab}`))));
  const dark = document.documentElement.dataset.theme === "dark";

  $("sidebar").replaceChildren(
    h("div", { class: "brand" }, h("div", { class: "mark" }, initials(shop)),
      h("div", { style: "min-width:0" }, h("div", { class: "brand-name" }, bdi(shop)), h("div", { class: "brand-sub" }, t("appName")))),
    ...navLinks("nav-item"),
    h("div", { class: "sidebar-foot" },
      h("div", { class: "tool-row" },
        h("button", { class: "tool grow", type: "button", onclick: toggleLanguage }, icon("globe", 16), t("language")),
        h("button", { class: "tool", type: "button", title: dark ? t("themeLight") : t("themeDark"), onclick: toggleTheme }, icon(dark ? "sun" : "moon", 16))),
      h("div", { class: "user-chip" },
        h("div", { class: "avatar" }, initials(me.user.name)),
        h("div", { class: "who" }, h("strong", null, bdi(me.user.name)), h("span", null, t(`role_${me.user.role}`))),
        h("button", { class: "icon-btn", type: "button", title: t("signOut"), "aria-label": t("signOut"), onclick: signOut }, icon("logout", 16)))));

  $("topbar").replaceChildren(
    h("div", { class: "mobile-brand" }, h("div", { class: "mark" }, initials(shop))),
    h("div", { class: "title" }, h("h1", null, page.title ?? t(`nav_${state.page}`)), page.subtitle ? h("div", { class: "sub" }, page.subtitle) : null),
    statusPill(),
    h("button", { class: "tool desk-only", type: "button", title: t("refresh"), onclick: () => refresh(true) }, icon("refresh", 16)),
    h("button", { class: "tool mobile-only", type: "button", "aria-label": t("menu"), onclick: openMenu }, icon("user", 16)));
  $("tabbar").replaceChildren(...navLinks(""));
}

function statusPill() {
  const { lastUpload, now } = state.me;
  if (state.offline) return h("span", { class: "status-pill off", title: t("offlineCopy") }, icon("wifi", 14), h("span", { class: "desk-only" }, t("offlineCopy")));
  if (!lastUpload) return h("span", { class: "status-pill off" }, h("span", { class: "pulse" }), h("span", { class: "desk-only" }, t("neverUploaded")));
  if (now - lastUpload > OFFLINE_AFTER_MS) {
    return h("span", { class: "status-pill off", title: t("shopOfflineLong", clock(lastUpload)) }, h("span", { class: "pulse" }),
      h("span", { class: "desk-only" }, t("shopOffline", clock(lastUpload))), h("span", { class: "mobile-only num" }, clock(lastUpload)));
  }
  return h("span", { class: "status-pill", title: t("live", clock(lastUpload)) }, h("span", { class: "pulse" }),
    h("span", { class: "desk-only" }, t("live", clock(lastUpload))), h("span", { class: "mobile-only num" }, clock(lastUpload)));
}

function openMenu() {
  const dark = document.documentElement.dataset.theme === "dark";
  openSheet(h("div", { class: "menu-sheet" },
    h("div", { class: "user-chip" }, h("div", { class: "avatar" }, initials(state.me.user.name)),
      h("div", { class: "who" }, h("strong", null, bdi(state.me.user.name)), h("span", null, t(`role_${state.me.user.role}`)))),
    h("button", { class: "tool", type: "button", onclick: () => { closeSheet(); refresh(true); } }, icon("refresh", 16), t("refresh")),
    h("button", { class: "tool", type: "button", onclick: () => { closeSheet(); toggleLanguage(); } }, icon("globe", 16), t("language")),
    h("button", { class: "tool", type: "button", onclick: () => { closeSheet(); toggleTheme(); } }, icon(dark ? "sun" : "moon", 16), dark ? t("themeLight") : t("themeDark")),
    h("button", { class: "tool", type: "button", onclick: signOut }, icon("logout", 16), t("signOut"))));
}

let sheetCleanup = null;

export function openSheet(content) {
  closeSheet();
  const backdrop = h("div", { class: "sheet-backdrop", onclick: closeSheet });
  const sheet = h("div", { class: "sheet", role: "dialog", "aria-modal": "true" }, content);
  const onKey = (e) => { if (e.key === "Escape") closeSheet(); };
  document.addEventListener("keydown", onKey);
  document.body.append(backdrop, sheet);
  sheetCleanup = () => { backdrop.remove(); sheet.remove(); document.removeEventListener("keydown", onKey); };
}

export function closeSheet() {
  sheetCleanup?.();
  sheetCleanup = null;
}

function emptyState(iconName, title, text) {
  return h("div", { class: "card empty-state" }, h("div", { class: "ic" }, icon(iconName, 26)), h("h3", null, title), text ? h("p", null, text) : null);
}

function skeleton() {
  return h("div", { class: "skeleton" },
    h("div", { class: "kpis" }, Array.from({ length: 6 }, () => h("div", { class: "sk", style: "height:118px" }))),
    h("div", { class: "grid g-main" }, h("div", { class: "sk", style: "height:320px" }), h("div", { class: "sk", style: "height:320px" })),
    h("div", { class: "sk", style: "height:220px" }));
}

/** What every page gets: the data and the shell's helpers. */
const ctx = {
  state,
  navigate,
  openSheet,
  closeSheet,
  emptyState,
  rerender: () => rerender(),
};

async function show() {
  const page = PAGES[state.page];
  const keys = page.keys(ctx);
  const content = $("content");
  if (keys.some((k) => !(k in state.docs))) {
    renderShell({});
    content.replaceChildren(skeleton());
  }
  try {
    await loadDocs(keys);
    // A page can need more once the first documents are in (the item list says how many parts it has).
    await loadDocs(page.keys(ctx));
  } catch (err) {
    if (err instanceof SignedOut) return showLogin();
    renderShell({});
    content.replaceChildren(emptyState("wifi", err instanceof TypeError ? t("offlineError") : t("serverError")));
    return;
  }
  rerender(true);
}

function rerender(animate) {
  if (!state.me) return;
  const page = PAGES[state.page];
  const view = page.render(ctx);
  renderShell(view);
  const content = $("content");
  content.classList.remove("page-enter");
  if (animate) {
    void content.offsetWidth; // restart the entrance animation
    content.classList.add("page-enter");
  }
  content.replaceChildren(...view.nodes.filter(Boolean)); // a null would show as the text "null"
}

async function refresh(force) {
  try {
    if (force) {
      state.docs = {};
      await loadMe();
      await show();
      return;
    }
    const { status, body } = await api("/api/poll");
    if (status !== 200) return;
    state.me.lastUpload = body.lastUpload;
    state.me.now = body.now;
    if (body.version !== state.version) {
      // Something new from the shop: reload what this page shows (and the lists of days).
      state.lastChange = Date.now();
      state.docs = {};
      await loadMe();
      await show();
    } else {
      renderShell(PAGES[state.page].render(ctx)); // the status line ("updated 16:42")
    }
  } catch (err) {
    if (err instanceof SignedOut) showLogin();
  }
}

let pollTimer = null;

function schedulePoll() {
  clearTimeout(pollTimer);
  const quiet = Date.now() - state.lastChange > 30 * 60_000 || (state.me?.lastUpload && state.me.now - state.me.lastUpload > OFFLINE_AFTER_MS);
  pollTimer = setTimeout(async () => {
    if (document.visibilityState === "visible" && state.me) await refresh(false);
    schedulePoll();
  }, quiet ? POLL_QUIET_MS : POLL_MS);
}

// ---- Start --------------------------------------------------------------------------------------------------------

function wire() {
  window.addEventListener("hashchange", () => {
    if (!state.me) return;
    closeSheet();
    readHash();
    show();
    window.scrollTo({ top: 0 });
  });
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && state.me) { refresh(false); schedulePoll(); }
  });
  window.matchMedia?.("(prefers-color-scheme: dark)").addEventListener?.("change", () => { if (!store.get("theme")) { applyTheme(null); rerender(); } });
  window.addEventListener("beforeprint", () => document.body.classList.add("printing"));
}

async function boot() {
  applyTheme(store.get("theme"));
  try {
    const response = await fetch("/api/public");
    if (response.ok) state.publicInfo = await response.json();
  } catch {
    // Offline: the sign-in page works without the shop's name.
  }
  useLanguage(store.get("lang") ?? state.publicInfo?.language ?? (navigator.language?.startsWith("ar") ? "ar" : "en"));
  wire();
  if ("serviceWorker" in navigator) navigator.serviceWorker.register("/sw.js").catch(() => {});
  start();
}

boot();
