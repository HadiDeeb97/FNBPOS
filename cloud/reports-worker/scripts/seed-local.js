// Fills a site with made-up reports and one account, to try it without a POS:
//   npx wrangler dev           (in another window; reads UPLOAD_KEY from .dev.vars)
//   npm run seed               (then open http://localhost:8787 and sign in as demo / demo-pass)
// Pass a URL and key to fill a deployed test site instead: npm run seed -- https://x.workers.dev <upload key>
import { readFileSync } from "node:fs";
import { createHash, pbkdf2Sync, randomBytes } from "node:crypto";
import { sampleDocs } from "../test/sample.js";

const url = process.argv[2] ?? "http://localhost:8787";
const key = process.argv[3] ?? /UPLOAD_KEY\s*=\s*"?([^"\n]+)"?/.exec(readFileSync(new URL("../.dev.vars", import.meta.url), "utf8"))?.[1];
if (!key) throw new Error("No upload key: put UPLOAD_KEY=... in .dev.vars or pass it after the URL.");

const salt = randomBytes(16);
const proofKey = pbkdf2Sync("demo-pass", salt, 100_000, 32, "sha256");
const users = [{
  username: "demo", name: "Demo owner", role: "admin", iterations: 100_000,
  salt: salt.toString("base64"), verifier: createHash("sha256").update(proofKey).digest("base64"),
}];

// D1's free plan allows 50 queries per request, so the documents go up in small batches, as the POS sends them.
const entries = Object.entries(sampleDocs());
for (let i = 0; i < entries.length; i += 20) {
  const body = { v: 1, docs: Object.fromEntries(entries.slice(i, i + 20)), ...(i === 0 ? { users } : {}) };
  const response = await fetch(`${url}/api/upload`, {
    method: "POST",
    headers: { "Content-Type": "application/json", Authorization: `Bearer ${key}` },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`Upload failed: ${response.status} ${await response.text()}`);
}
console.log(`Done. Open ${url} and sign in as demo / demo-pass.`);
