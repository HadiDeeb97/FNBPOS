// The shop's D1 database: the report summaries the POS sends, the accounts allowed to sign in, and sign-in sessions.
// The tables are created on first use, so a new shop needs no separate database setup step.

const SCHEMA = [
  `CREATE TABLE IF NOT EXISTS docs (key TEXT PRIMARY KEY, body TEXT NOT NULL, updated_at INTEGER NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS users (username TEXT PRIMARY KEY, name TEXT NOT NULL, role TEXT NOT NULL,
     salt TEXT NOT NULL, iterations INTEGER NOT NULL, verifier TEXT NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS sessions (token_hash TEXT PRIMARY KEY, username TEXT NOT NULL, verifier TEXT NOT NULL,
     created_at INTEGER NOT NULL, expires_at INTEGER NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS lockouts (username TEXT PRIMARY KEY, failures INTEGER NOT NULL,
     locked_until INTEGER NOT NULL, updated_at INTEGER NOT NULL)`,
  `CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)`,
];

let ready = null;

/** Creates the tables once per Worker instance (five cheap statements, then nothing). */
export function ensureSchema(db) {
  ready ??= db.batch(SCHEMA.map((sql) => db.prepare(sql))).catch((error) => {
    ready = null; // try again on the next request
    throw error;
  });
  return ready;
}

export async function getMeta(db, key) {
  const row = await db.prepare("SELECT value FROM meta WHERE key = ?").bind(key).first();
  return row?.value ?? null;
}

export function setMeta(db, key, value) {
  return db.prepare("INSERT INTO meta (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value")
    .bind(key, String(value));
}
