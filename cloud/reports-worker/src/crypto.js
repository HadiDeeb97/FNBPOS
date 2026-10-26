// Small helpers around Web Crypto. All the slow password work happens in the browser (see ui/app.js); here there is
// only hashing and comparing, which fits the free plan's few milliseconds per request.

const encoder = new TextEncoder();

export function toBase64(bytes) {
  let text = "";
  for (const b of new Uint8Array(bytes)) text += String.fromCharCode(b);
  return btoa(text);
}

export function fromBase64(text) {
  try {
    const raw = atob(text);
    const bytes = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    return bytes;
  } catch {
    return null;
  }
}

export async function sha256(data) {
  const bytes = typeof data === "string" ? encoder.encode(data) : data;
  return new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
}

export async function hmac(key, message) {
  const k = await crypto.subtle.importKey("raw", encoder.encode(key), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return new Uint8Array(await crypto.subtle.sign("HMAC", k, encoder.encode(message)));
}

/** Compares two secrets without leaking, through timing, how much of them matched. */
export async function sameSecret(a, b) {
  // Hashing first makes both the same length, which timingSafeEqual needs.
  const [x, y] = await Promise.all([sha256(a), sha256(b)]);
  return crypto.subtle.timingSafeEqual(x, y);
}

export function randomToken(bytes = 32) {
  return toBase64(crypto.getRandomValues(new Uint8Array(bytes))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
