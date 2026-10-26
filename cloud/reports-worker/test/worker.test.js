// Runs the Worker locally (workerd + a local D1 database, through wrangler) and talks to it as the POS and a phone would.
import { after, before, test } from "node:test";
import assert from "node:assert/strict";
import { pbkdf2Sync, createHash, randomBytes } from "node:crypto";
import { fileURLToPath } from "node:url";
import { unstable_startWorker } from "wrangler";

const UPLOAD_KEY = "test-upload-key-0123456789";
let worker;

before(async () => {
  worker = await unstable_startWorker({
    config: fileURLToPath(new URL("../wrangler.toml", import.meta.url)),
    // A higher per-visitor limit than live: every test here comes from the same address.
    bindings: { UPLOAD_KEY: { type: "plain_text", value: UPLOAD_KEY }, PUBLIC_PER_MINUTE: { type: "plain_text", value: "150" } },
    dev: { server: { port: 0 }, inspector: false, persist: false, logLevel: "error" },
  });
}, { timeout: 90_000 });

after(async () => {
  await worker?.dispose();
});

const call = (path, init) => worker.fetch(`http://localhost${path}`, init);

function upload(payload, key = UPLOAD_KEY) {
  return call("/api/upload", {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${key}` },
    body: JSON.stringify({ v: 1, ...payload }),
  });
}

/** What the POS sends for an account: its PBKDF2 salt and a hash of its stored PBKDF2 key, never the password. */
function account(username, password, role = "admin", name = username) {
  const salt = randomBytes(16);
  const key = pbkdf2Sync(password, salt, 1000, 32, "sha256");
  return {
    username, name, role, iterations: 1000, salt: salt.toString("base64"),
    verifier: createHash("sha256").update(key).digest("base64"),
  };
}

/** What the browser does: fetch the salt, run PBKDF2 on the typed password, send the result. */
async function signIn(username, password) {
  const saltResponse = await call(`/api/salt?u=${encodeURIComponent(username)}`);
  const { salt, iterations } = await saltResponse.json();
  const proof = pbkdf2Sync(password, Buffer.from(salt, "base64"), iterations, 32, "sha256").toString("base64");
  const response = await call("/api/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, proof }),
  });
  const cookie = response.headers.get("Set-Cookie")?.split(";")[0] ?? null;
  return { response, cookie, body: await response.json() };
}

const get = (path, cookie) => call(path, { headers: cookie ? { Cookie: cookie } : {} });

test("serves the pages with a strict content policy", async () => {
  const page = await call("/");
  assert.equal(page.status, 200);
  assert.match(await page.text(), /<script type="module" src="\/app.js">/);
  assert.match(page.headers.get("Content-Security-Policy"), /script-src 'self'/);
  assert.equal((await call("/app.js")).headers.get("Content-Type"), "text/javascript; charset=utf-8");
  assert.equal((await call("/nope")).status, 404);
  const health = await (await call("/api/health")).json();
  assert.deepEqual(health, { ok: true, version: 1 });
});

test("refuses uploads without the shop's key", async () => {
  assert.equal((await upload({ docs: {} }, "wrong")).status, 401);
  const noHeader = await call("/api/upload", { method: "POST", body: "{}" });
  assert.equal(noHeader.status, 401);
});

test("checks what is uploaded", async () => {
  assert.equal((await upload({ v: 2 })).status, 400);
  assert.equal((await upload({ docs: { "hack:1": {} } })).status, 400);
  assert.equal((await upload({ docs: { live: "text" } })).status, 400);
  assert.equal((await upload({ users: [{ username: "x", role: "cashier" }] })).status, 400);
  const tooMany = Object.fromEntries(Array.from({ length: 31 }, (_, i) => [`day:2026-01-${String(i + 1).padStart(2, "0")}`, {}]));
  assert.equal((await upload({ docs: tooMany })).status, 400);
});

test("reports can only be read after signing in", async () => {
  assert.equal((await upload({ docs: { live: { recentSales: [] } }, users: [account("owner", "secret-pass")] })).status, 200);
  assert.equal((await get("/api/me")).status, 401);
  assert.equal((await get("/api/docs?keys=live")).status, 401);
  assert.equal((await get("/api/docs?keys=live", "__Host-pos=made-up")).status, 401);
});

test("signs in with the POS username and password, and reads the reports", async () => {
  await upload({
    docs: {
      store: { name: "Rami Boutique", currency: "$", lbp: true, language: "en" },
      "period:today": { from: "2026-10-23", to: "2026-10-23", totals: { totalSales: 120.5 } },
      "day:2026-10-22": { from: "2026-10-22", to: "2026-10-22" },
      "month:2026-09": { from: "2026-09-01", to: "2026-09-30" },
    },
    users: [account("owner", "secret-pass", "admin", "Rami")],
  });
  const { response, cookie, body } = await signIn("Owner", "secret-pass"); // usernames ignore case, as in the POS
  assert.equal(response.status, 200);
  assert.deepEqual(body.user, { name: "Rami", role: "admin" });
  assert.match(response.headers.get("Set-Cookie"), /HttpOnly; SameSite=Strict/);

  const me = await (await get("/api/me", cookie)).json();
  assert.equal(me.store.name, "Rami Boutique");
  assert.deepEqual(me.days, ["2026-10-22"]);
  assert.deepEqual(me.months, ["2026-09"]);
  assert.ok(me.lastUpload > 0);

  const docs = await (await get("/api/docs?keys=period:today,live,day:2026-01-01", cookie)).json();
  assert.equal(docs.docs["period:today"].body.totals.totalSales, 120.5);
  assert.ok(docs.docs.live);
  assert.equal(docs.docs["day:2026-01-01"], undefined);
  assert.equal((await get("/api/docs?keys=users", cookie)).status, 400);
});

test("the item list is stored in parts and read back", async () => {
  const part = (i) => ({ part: i, parts: 2, products: [{ n: `Tee ${i}`, c: "Tees", b: null, s: null, p: 20, v: [["S", "Black", `T${i}-S`, null, 4, 2, null, 1]] }] });
  await upload({ docs: { "catalog:0": part(0), "catalog:1": part(1) }, users: [account("owner", "secret-pass")] });
  assert.equal((await upload({ docs: { "catalog:10": part(10) } })).status, 400, "ten parts at most");
  const { cookie } = await signIn("owner", "secret-pass");
  const docs = await (await get("/api/docs?keys=catalog:0,catalog:1", cookie)).json();
  assert.equal(docs.docs["catalog:1"].body.products[0].v[0][2], "T1-S");
  assert.equal(docs.docs["catalog:0"].body.parts, 2);
});

test("an unknown username gets a made-up salt that never changes, so names can't be guessed", async () => {
  const a = await (await call("/api/salt?u=nobody")).json();
  const b = await (await call("/api/salt?u=nobody")).json();
  const c = await (await call("/api/salt?u=someone")).json();
  assert.equal(a.salt, b.salt);
  assert.notEqual(a.salt, c.salt);
  assert.equal(a.iterations, 100000);
  const wrong = await signIn("nobody", "x");
  assert.equal(wrong.response.status, 401);
});

test("locks an account for 15 minutes after 5 wrong passwords", async () => {
  await upload({ users: [account("owner", "secret-pass"), account("manager1", "right-pass", "manager")] });
  for (let i = 1; i <= 4; i++) {
    const { response, body } = await signIn("manager1", "bad");
    assert.equal(response.status, 401);
    assert.equal(body.left, 5 - i);
  }
  const fifth = await signIn("manager1", "bad");
  assert.equal(fifth.response.status, 429);
  assert.equal(fifth.body.minutes, 15);
  const right = await signIn("manager1", "right-pass");
  assert.equal(right.response.status, 429, "even the right password waits");
  // Other accounts aren't affected.
  assert.equal((await signIn("owner", "secret-pass")).response.status, 200);
});

test("changing the password in the POS signs that account out everywhere", async () => {
  const user = account("cpass", "old-pass");
  await upload({ users: [account("owner", "secret-pass"), user] });
  const { cookie } = await signIn("cpass", "old-pass");
  assert.equal((await get("/api/me", cookie)).status, 200);

  await upload({ users: [account("owner", "secret-pass"), account("cpass", "new-pass")] });
  assert.equal((await get("/api/me", cookie)).status, 401);
  assert.equal((await signIn("cpass", "old-pass")).response.status, 401);
  assert.equal((await signIn("cpass", "new-pass")).response.status, 200);
});

test("a removed or demoted account can't read anything", async () => {
  await upload({ users: [account("owner", "secret-pass"), account("leaver", "pass-1234")] });
  const { cookie } = await signIn("leaver", "pass-1234");
  assert.equal((await get("/api/me", cookie)).status, 200);
  await upload({ users: [account("owner", "secret-pass")] });
  assert.equal((await get("/api/me", cookie)).status, 401);
});

test("sign out all devices ends every session once", async () => {
  const owner = account("owner", "secret-pass");
  await upload({ users: [owner] });
  const first = await signIn("owner", "secret-pass");
  await upload({ signOutAll: 1 });
  assert.equal((await get("/api/me", first.cookie)).status, 401);

  const second = await signIn("owner", "secret-pass");
  await upload({ signOutAll: 1 }); // sent again with every upload: no effect the second time
  assert.equal((await get("/api/me", second.cookie)).status, 200);
});

test("signing out ends the session", async () => {
  await upload({ users: [account("owner", "secret-pass")] });
  const { cookie } = await signIn("owner", "secret-pass");
  const out = await call("/api/logout", { method: "POST", headers: { Cookie: cookie } });
  assert.match(out.headers.get("Set-Cookie"), /Max-Age=0/);
  assert.equal((await get("/api/me", cookie)).status, 401);
});

test("old days are removed, months are kept", async () => {
  await upload({ docs: { "day:2025-01-05": {}, "day:2025-09-30": {}, "month:2024-12": {} }, users: [account("owner", "secret-pass")] });
  await upload({ keepDaysFrom: "2025-09-01" });
  const { cookie } = await signIn("owner", "secret-pass");
  const me = await (await get("/api/me", cookie)).json();
  assert.ok(!me.days.includes("2025-01-05"));
  assert.ok(me.days.includes("2025-09-30"));
  assert.ok(me.months.includes("2024-12"));
});

test("the sign-in page knows the shop, and Google's code is on the page once the POS sends it", async () => {
  await upload({ docs: { store: { name: "Rami Boutique", currency: "$", language: "ar", googleVerification: "AbC123_xyz-987654321" } } });
  assert.deepEqual(await (await call("/api/public")).json(), { name: "Rami Boutique", language: "ar" });
  const page = await (await call("/")).text();
  assert.match(page, /<meta name="google-site-verification" content="AbC123_xyz-987654321">/);
  assert.equal((await call("/")).headers.get("X-Robots-Tag"), "noindex, nofollow");
  assert.match(await (await call("/robots.txt")).text(), /Allow: \//);

  await upload({ docs: { store: { name: "Rami Boutique", currency: "$", language: "ar" } } });
  assert.doesNotMatch(await (await call("/")).text(), /google-site-verification/);
});

test("serves the fonts and the page's modules", async () => {
  for (const path of ["/pages.js", "/charts.js", "/i18n.js", "/sw.js"]) assert.equal((await call(path)).status, 200, path);
  const font = await call("/fonts/inter.woff2");
  assert.equal(font.headers.get("Content-Type"), "font/woff2");
  assert.match(font.headers.get("Cache-Control"), /immutable/);
  assert.ok((await font.arrayBuffer()).byteLength > 10_000);
  assert.match((await call("/")).headers.get("Content-Security-Policy"), /font-src 'self'/);
});

test("phones check for news cheaply and only reload when the shop sent something", async () => {
  await upload({ users: [account("owner", "secret-pass")] });
  const { cookie } = await signIn("owner", "secret-pass");
  assert.equal((await get("/api/poll")).status, 401);
  const before = await (await get("/api/poll", cookie)).json();
  await upload({}); // a heartbeat: nothing new
  const beat = await (await get("/api/poll", cookie)).json();
  assert.equal(beat.version, before.version);
  assert.ok(beat.lastUpload >= before.lastUpload);
  await upload({ docs: { live: { recentSales: [] } } });
  const after = await (await get("/api/poll", cookie)).json();
  assert.notEqual(after.version, before.version);
  const me = await (await get("/api/me", cookie)).json();
  assert.equal(me.version, after.version);
});

test("a visitor hammering the public pages is slowed down", async () => {
  let limited = 0;
  for (let i = 0; i < 160; i++) if ((await call("/api/public")).status === 429) limited++;
  assert.ok(limited > 0);
});
