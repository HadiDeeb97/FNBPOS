// Online reports for one shop. The POS at the shop works out its reports and sends them here (POST /api/upload);
// owners and managers sign in with their POS username and password and read them. Nothing here can change the shop's
// data: the site only shows what the POS sent.

import indexHtml from "./ui/index.html";
import appJs from "./ui/app.js";
import chartsJs from "./ui/charts.js";
import domJs from "./ui/dom.js";
import formatJs from "./ui/format.js";
import i18nJs from "./ui/i18n.js";
import iconsJs from "./ui/icons.js";
import insightsJs from "./ui/insights.js";
import pagesJs from "./ui/pages.js";
import swJs from "./ui/sw.js";
import appCss from "./ui/app.css";
import manifest from "./ui/manifest.webmanifest";
import icon from "./ui/icon.svg";
import interFont from "./ui/fonts/inter.woff2";
import plex400 from "./ui/fonts/plex-arabic-400.woff2";
import plex600 from "./ui/fonts/plex-arabic-600.woff2";
import plex700 from "./ui/fonts/plex-arabic-700.woff2";
import { ensureSchema, getMeta, setMeta } from "./db.js";
import { fromBase64, hmac, randomToken, sameSecret, sha256, toBase64 } from "./crypto.js";

/** Bumped when the upload format changes; the POS reads it from /api/health. */
const VERSION = 1;

const SESSION_DAYS = 30;
const MAX_FAILURES = 5;
const LOCK_MINUTES = 15;
const COOKIE = "__Host-pos";

// What the POS may send. Days are kept 13 months; months and the rest are replaced in place. The item list (for the
// stock search) comes in up to 10 parts.
const DOC_KEY = /^(store|live|stock|catalog:\d|period:(today|yesterday|week|lastweek|month|lastmonth|year)|day:\d{4}-\d{2}-\d{2}|month:\d{4}-\d{2})$/;
const MAX_DOCS_PER_UPLOAD = 30; // D1's free plan allows 50 queries per request
const MAX_DOC_LENGTH = 1_500_000;
const MAX_UPLOAD_LENGTH = 8_000_000;
const DEFAULT_ITERATIONS = 100_000; // the POS's own (PasswordHasher)

// Requests a visitor can make to the pages that work without signing in, per minute. Keeps a bot from using up the
// shop's free daily requests (it would only pause the site until midnight UTC, but the owner would lose the reports).
const PUBLIC_PER_MINUTE = 30;

const JS = "text/javascript; charset=utf-8";
const HTML = "text/html; charset=utf-8";
const FONT = "font/woff2";

// The website's files, served by the Worker itself. [content, type, long cache]
const STATIC = {
  "/": [indexHtml, HTML],
  "/index.html": [indexHtml, HTML],
  "/app.js": [appJs, JS],
  "/charts.js": [chartsJs, JS],
  "/dom.js": [domJs, JS],
  "/format.js": [formatJs, JS],
  "/i18n.js": [i18nJs, JS],
  "/icons.js": [iconsJs, JS],
  "/insights.js": [insightsJs, JS],
  "/pages.js": [pagesJs, JS],
  "/sw.js": [swJs, JS],
  "/app.css": [appCss, "text/css; charset=utf-8"],
  "/manifest.webmanifest": [manifest, "application/manifest+json"],
  "/icon.svg": [icon, "image/svg+xml"],
  "/fonts/inter.woff2": [interFont, FONT, true],
  "/fonts/plex-arabic-400.woff2": [plex400, FONT, true],
  "/fonts/plex-arabic-600.woff2": [plex600, FONT, true],
  "/fonts/plex-arabic-700.woff2": [plex700, FONT, true],
};

const SECURITY_HEADERS = {
  "Content-Security-Policy":
    "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
    "manifest-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer",
  "X-Frame-Options": "DENY",
};

export default {
  async fetch(request, env) {
    try {
      return await route(request, env);
    } catch (error) {
      console.error(error);
      return json({ error: "server" }, 500);
    }
  },
};

async function route(request, env) {
  const url = new URL(request.url);
  const path = url.pathname;
  const method = request.method;

  if (method === "GET" && (path === "/" || path === "/index.html")) return page(env);
  if (method === "GET" && path === "/robots.txt") {
    // Crawlable (Google has to read the page to review it), but kept out of search results by noindex.
    return new Response("User-agent: *\nAllow: /\n", { headers: { "Content-Type": "text/plain; charset=utf-8", ...SECURITY_HEADERS } });
  }
  if (method === "GET" && STATIC[path]) {
    const [body, type, immutable] = STATIC[path];
    // Fonts never change under the same name; the pages and code are checked each time (the POS may have updated them).
    const cache = immutable ? "public, max-age=31536000, immutable" : "no-cache";
    return new Response(body, { headers: { "Content-Type": type, "Cache-Control": cache, ...SECURITY_HEADERS } });
  }
  if (!path.startsWith("/api/")) return json({ error: "not_found" }, 404);
  if (path === "/api/health" && method === "GET") return json({ ok: true, version: VERSION });

  if ((path === "/api/salt" || path === "/api/login" || path === "/api/public") && tooMany(request, env)) {
    return json({ error: "slow_down" }, 429, { "Retry-After": "60" });
  }

  await ensureSchema(env.DB);
  switch (`${method} ${path}`) {
    case "POST /api/upload": return upload(request, env);
    case "GET /api/public": return publicInfo(env);
    case "GET /api/salt": return salt(url, env);
    case "POST /api/login": return login(request, env);
    case "POST /api/logout": return logout(request, env);
    case "GET /api/me": return me(request, env);
    case "GET /api/poll": return poll(request, env);
    case "GET /api/docs": return docs(request, url, env);
    default: return json({ error: "not_found" }, 404);
  }
}

// ---- Page and public details ------------------------------------------------------------------------------------

/**
 * The page, with the shop's Google Search Console code when the POS sent one (so the owner can verify the site and ask
 * Google to review a wrong "dangerous site" warning).
 */
async function page(env) {
  let html = indexHtml;
  try {
    await ensureSchema(env.DB);
    const code = await getMeta(env.DB, "google_verification");
    if (code) html = html.replace("<!--google-->", `<meta name="google-site-verification" content="${escapeAttr(code)}">`);
  } catch {
    // The page works without it.
  }
  return new Response(html, {
    headers: { "Content-Type": HTML, "Cache-Control": "no-cache", "X-Robots-Tag": "noindex, nofollow", ...SECURITY_HEADERS },
  });
}

function escapeAttr(text) {
  return String(text).replace(/[^A-Za-z0-9_\-]/g, ""); // verification codes are letters, digits, - and _
}

/** The shop's name and language for the sign-in page, so it is clearly that shop's site. Nothing else. */
async function publicInfo(env) {
  const row = await env.DB.prepare("SELECT value FROM meta WHERE key = 'public'").first();
  return json(row ? JSON.parse(row.value) : { name: null, language: null });
}

// Per-visitor counts for the public pages, kept in this Worker instance's memory (no database writes).
const visitors = new Map();

function tooMany(request, env) {
  const limit = Number(env.PUBLIC_PER_MINUTE) || PUBLIC_PER_MINUTE;
  const ip = request.headers.get("CF-Connecting-IP") ?? "local";
  const minute = Math.floor(Date.now() / 60_000);
  const entry = visitors.get(ip);
  if (!entry || entry.minute !== minute) {
    if (visitors.size > 5000) visitors.clear();
    visitors.set(ip, { minute, count: 1 });
    return false;
  }
  entry.count++;
  return entry.count > limit;
}

// ---- Upload (from the POS) --------------------------------------------------------------------------------------

async function upload(request, env) {
  if (!env.UPLOAD_KEY) return json({ error: "not_configured" }, 503);
  const auth = request.headers.get("Authorization") ?? "";
  const key = auth.startsWith("Bearer ") ? auth.slice(7) : "";
  if (!key || !(await sameSecret(key, env.UPLOAD_KEY))) return json({ error: "unauthorized" }, 401);

  const text = await request.text();
  if (text.length > MAX_UPLOAD_LENGTH) return json({ error: "too_large" }, 413);
  let payload;
  try {
    payload = JSON.parse(text);
  } catch {
    return json({ error: "bad_json" }, 400);
  }
  if (payload?.v !== VERSION) return json({ error: "bad_version", version: VERSION }, 400);

  const db = env.DB;
  const now = Date.now();
  const statements = [];

  const entries = Object.entries(payload.docs ?? {});
  if (entries.length > MAX_DOCS_PER_UPLOAD) return json({ error: "too_many_docs", max: MAX_DOCS_PER_UPLOAD }, 400);
  for (const [docKey, body] of entries) {
    if (!DOC_KEY.test(docKey) || typeof body !== "object" || body === null) return json({ error: "bad_doc", key: docKey }, 400);
    const value = JSON.stringify(body);
    if (value.length > MAX_DOC_LENGTH) return json({ error: "doc_too_large", key: docKey }, 413);
    statements.push(db.prepare("INSERT INTO docs (key, body, updated_at) VALUES (?, ?, ?) " +
      "ON CONFLICT(key) DO UPDATE SET body = excluded.body, updated_at = excluded.updated_at").bind(docKey, value, now));
  }

  // Days older than the POS keeps online (13 months).
  if (payload.keepDaysFrom !== undefined) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(payload.keepDaysFrom)) return json({ error: "bad_keep_days" }, 400);
    statements.push(db.prepare("DELETE FROM docs WHERE key >= 'day:' AND key < ?").bind(`day:${payload.keepDaysFrom}`));
  }

  if (payload.users !== undefined) {
    const users = validUsers(payload.users);
    if (!users) return json({ error: "bad_users" }, 400);
    const list = JSON.stringify(users);
    // The accounts allowed to sign in are exactly the POS's managers and admins. A session ends when its account is
    // removed or its password changes (sessions keep the verifier they signed in with).
    statements.push(db.prepare("DELETE FROM users WHERE username NOT IN (SELECT json_extract(value, '$.username') FROM json_each(?))").bind(list));
    statements.push(db.prepare(
      "INSERT INTO users (username, name, role, salt, iterations, verifier) " +
      "SELECT json_extract(value, '$.username'), json_extract(value, '$.name'), json_extract(value, '$.role'), " +
      "json_extract(value, '$.salt'), json_extract(value, '$.iterations'), json_extract(value, '$.verifier') FROM json_each(?) WHERE true " +
      "ON CONFLICT(username) DO UPDATE SET name = excluded.name, role = excluded.role, salt = excluded.salt, " +
      "iterations = excluded.iterations, verifier = excluded.verifier").bind(list));
    statements.push(db.prepare("DELETE FROM sessions WHERE username NOT IN (SELECT username FROM users)"));
  }

  // "Sign out all devices" in the POS: a counter that only goes up.
  if (payload.signOutAll !== undefined) {
    if (!Number.isInteger(payload.signOutAll) || payload.signOutAll < 0) return json({ error: "bad_sign_out" }, 400);
    const current = Number(await getMeta(db, "sign_out_all") ?? 0);
    if (payload.signOutAll > current) {
      statements.push(db.prepare("DELETE FROM sessions"));
      statements.push(setMeta(db, "sign_out_all", payload.signOutAll));
    }
  }

  // What the phones check every couple of minutes: a new number only when there is something new to show.
  if (entries.length) statements.push(setMeta(db, "data_version", now));
  const store = payload.docs?.store;
  if (store) {
    statements.push(setMeta(db, "public", JSON.stringify({
      name: typeof store.name === "string" ? store.name.slice(0, 100) : null,
      language: store.language === "ar" ? "ar" : "en",
    })));
    statements.push(typeof store.googleVerification === "string" && store.googleVerification
      ? setMeta(db, "google_verification", escapeAttr(store.googleVerification).slice(0, 100))
      : db.prepare("DELETE FROM meta WHERE key = 'google_verification'"));
  }

  // A beat with nothing new writes this one row.
  statements.push(setMeta(db, "last_upload", now));
  if (payload.keepDaysFrom !== undefined) {
    // Housekeeping with the half-hourly upload only.
    statements.push(db.prepare("DELETE FROM sessions WHERE expires_at < ?").bind(now));
    statements.push(db.prepare("DELETE FROM lockouts WHERE updated_at < ?").bind(now - 24 * 3600_000));
  }
  await db.batch(statements); // all or nothing

  // The list of past days and months, kept in one row so a phone opening the site doesn't read hundreds of rows.
  if (payload.keepDaysFrom !== undefined || entries.some(([k]) => k.startsWith("day:") || k.startsWith("month:"))) {
    const history = await db.prepare("SELECT key FROM docs WHERE key >= 'day:' AND key < 'day;' OR key >= 'month:' AND key < 'month;' ORDER BY key DESC").all();
    const keys = history.results.map((r) => r.key);
    await setMeta(db, "history", JSON.stringify({
      days: keys.filter((k) => k.startsWith("day:")).map((k) => k.slice(4)),
      months: keys.filter((k) => k.startsWith("month:")).map((k) => k.slice(6)),
    })).run();
  }
  return json({ ok: true, docs: entries.length });
}

function validUsers(users) {
  if (!Array.isArray(users) || users.length > 50) return null;
  const seen = new Set();
  const result = [];
  for (const u of users) {
    if (typeof u !== "object" || u === null) return null;
    const username = typeof u.username === "string" ? u.username.trim().toLowerCase() : "";
    if (!username || username.length > 50 || seen.has(username)) return null;
    if (typeof u.name !== "string" || u.name.length > 100) return null;
    if (u.role !== "admin" && u.role !== "manager") return null;
    if (!isBase64(u.salt, 8, 64) || !isBase64(u.verifier, 32, 32)) return null;
    if (!Number.isInteger(u.iterations) || u.iterations < 1000 || u.iterations > 10_000_000) return null;
    seen.add(username);
    result.push({ username, name: u.name, role: u.role, salt: u.salt, iterations: u.iterations, verifier: u.verifier });
  }
  return result;
}

function isBase64(value, minBytes, maxBytes) {
  if (typeof value !== "string" || value.length > 100) return false;
  const bytes = fromBase64(value);
  return bytes !== null && bytes.length >= minBytes && bytes.length <= maxBytes;
}

// ---- Sign-in -----------------------------------------------------------------------------------------------------

/**
 * The salt the browser needs to work out the password proof. An unknown name gets a made-up salt that is always the
 * same for that name, so the answer doesn't tell anyone which usernames exist.
 */
async function salt(url, env) {
  const username = normalizeUsername(url.searchParams.get("u"));
  if (!username) return json({ error: "bad_request" }, 400);
  const user = await env.DB.prepare("SELECT salt, iterations FROM users WHERE username = ?").bind(username).first();
  if (user) return json({ salt: user.salt, iterations: user.iterations });
  const fake = (await hmac(env.UPLOAD_KEY || "pos-reports", `salt:${username}`)).slice(0, 16);
  return json({ salt: toBase64(fake), iterations: DEFAULT_ITERATIONS });
}

async function login(request, env) {
  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "bad_request" }, 400);
  }
  const username = normalizeUsername(body?.username);
  const proof = typeof body?.proof === "string" ? fromBase64(body.proof) : null;
  if (!username || !proof) return json({ error: "bad_request" }, 400);

  const db = env.DB;
  const now = Date.now();
  const lockMs = LOCK_MINUTES * 60_000;
  const lock = await db.prepare("SELECT failures, locked_until, updated_at FROM lockouts WHERE username = ?").bind(username).first();
  if (lock && lock.locked_until > now) return locked(lock.locked_until - now);

  const user = await db.prepare("SELECT username, name, role, verifier FROM users WHERE username = ?").bind(username).first();
  const expected = user ? fromBase64(user.verifier) : null;
  const actual = await sha256(proof);
  const ok = proof.length === 32 && expected?.length === 32 && crypto.subtle.timingSafeEqual(actual, expected);

  if (!ok) {
    const failures = (lock && now - lock.updated_at < lockMs ? lock.failures : 0) + 1;
    const lockedUntil = failures >= MAX_FAILURES ? now + lockMs : 0;
    await db.prepare("INSERT INTO lockouts (username, failures, locked_until, updated_at) VALUES (?, ?, ?, ?) " +
      "ON CONFLICT(username) DO UPDATE SET failures = excluded.failures, locked_until = excluded.locked_until, updated_at = excluded.updated_at")
      .bind(username, lockedUntil ? 0 : failures, lockedUntil, now).run();
    return lockedUntil ? locked(lockMs) : json({ error: "wrong", left: MAX_FAILURES - failures }, 401);
  }

  const token = randomToken();
  const expires = now + SESSION_DAYS * 86_400_000;
  await db.batch([
    db.prepare("DELETE FROM lockouts WHERE username = ?").bind(username),
    db.prepare("INSERT INTO sessions (token_hash, username, verifier, created_at, expires_at) VALUES (?, ?, ?, ?, ?)")
      .bind(toBase64(await sha256(token)), user.username, user.verifier, now, expires),
    db.prepare("DELETE FROM sessions WHERE expires_at < ?").bind(now),
  ]);
  return json({ ok: true, user: { name: user.name, role: user.role } }, 200, {
    "Set-Cookie": `${COOKIE}=${token}; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=${SESSION_DAYS * 86_400}`,
  });
}

function locked(ms) {
  return json({ error: "locked", minutes: Math.max(1, Math.ceil(ms / 60_000)) }, 429);
}

async function logout(request, env) {
  const token = readCookie(request);
  if (token) await env.DB.prepare("DELETE FROM sessions WHERE token_hash = ?").bind(toBase64(await sha256(token))).run();
  return json({ ok: true }, 200, { "Set-Cookie": `${COOKIE}=; Path=/; Secure; HttpOnly; SameSite=Strict; Max-Age=0` });
}

/** The signed-in account, or null. The account must still exist with the same password it signed in with. */
async function currentUser(request, env) {
  const token = readCookie(request);
  if (!token) return null;
  return env.DB.prepare(
    "SELECT u.username, u.name, u.role FROM sessions s JOIN users u ON u.username = s.username AND u.verifier = s.verifier " +
    "WHERE s.token_hash = ? AND s.expires_at > ?").bind(toBase64(await sha256(token)), Date.now()).first();
}

function readCookie(request) {
  const header = request.headers.get("Cookie") ?? "";
  for (const part of header.split(";")) {
    const [name, ...rest] = part.trim().split("=");
    if (name === COOKIE) return rest.join("=") || null;
  }
  return null;
}

function normalizeUsername(value) {
  if (typeof value !== "string") return null;
  const name = value.trim().toLowerCase();
  return name && name.length <= 50 ? name : null;
}

// ---- Reading (signed in) -----------------------------------------------------------------------------------------

/** Who is signed in, the shop's details, when it last sent, and which past days and months there are. */
async function me(request, env) {
  const user = await currentUser(request, env);
  if (!user) return json({ error: "signed_out" }, 401);
  const db = env.DB;
  const [store, meta] = await Promise.all([
    db.prepare("SELECT body FROM docs WHERE key = 'store'").first(),
    readMeta(db, ["last_upload", "history", "data_version"]),
  ]);
  const history = meta.history ? JSON.parse(meta.history) : { days: [], months: [] };
  return json({
    user: { name: user.name, role: user.role },
    store: store ? JSON.parse(store.body) : null,
    lastUpload: meta.last_upload ? Number(meta.last_upload) : null,
    version: meta.data_version ? Number(meta.data_version) : 0,
    now: Date.now(),
    days: history.days,
    months: history.months,
  });
}

/** What a phone checks every couple of minutes: a few rows, and the documents only when the version moved. */
async function poll(request, env) {
  const user = await currentUser(request, env);
  if (!user) return json({ error: "signed_out" }, 401);
  const meta = await readMeta(env.DB, ["last_upload", "data_version"]);
  return json({
    lastUpload: meta.last_upload ? Number(meta.last_upload) : null,
    version: meta.data_version ? Number(meta.data_version) : 0,
    now: Date.now(),
  });
}

async function readMeta(db, keys) {
  const rows = await db.prepare(`SELECT key, value FROM meta WHERE key IN (${keys.map(() => "?").join(",")})`).bind(...keys).all();
  return Object.fromEntries(rows.results.map((r) => [r.key, r.value]));
}

async function docs(request, url, env) {
  const user = await currentUser(request, env);
  if (!user) return json({ error: "signed_out" }, 401);
  const keys = [...new Set((url.searchParams.get("keys") ?? "").split(",").filter(Boolean))];
  if (keys.length === 0 || keys.length > 10 || keys.some((k) => !DOC_KEY.test(k))) return json({ error: "bad_request" }, 400);
  const rows = await env.DB.prepare(`SELECT key, body, updated_at FROM docs WHERE key IN (${keys.map(() => "?").join(",")})`)
    .bind(...keys).all();
  const result = {};
  for (const row of rows.results) result[row.key] = { body: JSON.parse(row.body), updatedAt: row.updated_at };
  return json({ docs: result });
}

function json(value, status = 200, headers = {}) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...SECURITY_HEADERS, ...headers },
  });
}
