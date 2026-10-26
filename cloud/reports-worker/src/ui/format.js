// Numbers, money and dates, in the page language. Digits stay Western (as on the POS's receipts) in Arabic too.

let lang = "en";
let symbol = "$";

export function setFormat(language, currencySymbol) {
  lang = language;
  symbol = currencySymbol || "$";
}

export const locale = () => (lang === "ar" ? "ar-u-nu-latn" : "en-GB");

const n2 = new Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const n0 = new Intl.NumberFormat("en-US", { maximumFractionDigits: 0 });
const n1 = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });

export function money(value) {
  const n = Number(value ?? 0);
  return (n < 0 ? "-" : "") + symbol + n2.format(Math.abs(n));
}

/** Whole dollars for big figures ($41,250). */
export function money0(value) {
  const n = Number(value ?? 0);
  return (n < 0 ? "-" : "") + symbol + n0.format(Math.abs(Math.round(n)));
}

/** Short money for chart axes: $950, $1.2k, $15k, $1.3M. */
export function moneyShort(value) {
  const n = Math.abs(Number(value ?? 0));
  const sign = value < 0 ? "-" : "";
  if (n >= 1_000_000) return `${sign}${symbol}${n1.format(n / 1_000_000)}M`;
  if (n >= 10_000) return `${sign}${symbol}${n0.format(n / 1000)}k`;
  if (n >= 1000) return `${sign}${symbol}${n1.format(n / 1000)}k`;
  return `${sign}${symbol}${n0.format(n)}`;
}

export function lbp(value) {
  const text = n0.format(Math.round(Number(value ?? 0)));
  return lang === "ar" ? `${text} ل.ل.` : `${text} LBP`;
}

export const number = (value) => n0.format(Number(value ?? 0));
export const decimal1 = (value) => n1.format(Number(value ?? 0));

export function percent(value, signed = false) {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return "–";
  const n = Number(value);
  return (signed && n > 0 ? "+" : "") + n1.format(n) + "%";
}

/** The POS sends its own local times ("2026-10-23T16:42:00"): shown as they are, in the shop's time. */
export function parseLocal(text) {
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2}))?/.exec(text ?? "");
  return m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]), Number(m[4] ?? 0), Number(m[5] ?? 0)) : null;
}

export function time(text) {
  const d = parseLocal(text);
  return d ? d.toLocaleTimeString(locale(), { hour: "2-digit", minute: "2-digit", hour12: false }) : "";
}

export function day(text, opts = {}) {
  const d = typeof text === "string" ? parseLocal(text) : text;
  if (!d) return "";
  return d.toLocaleDateString(locale(), { weekday: opts.weekday === false ? undefined : "short", day: "numeric", month: "short", ...(opts.year ? { year: "numeric" } : {}) });
}

export function longDay(text) {
  const d = typeof text === "string" ? parseLocal(text) : text;
  return d ? d.toLocaleDateString(locale(), { weekday: "long", day: "numeric", month: "long", year: "numeric" }) : "";
}

export function month(key) {
  const [y, m] = key.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(locale(), { month: "long", year: "numeric" });
}

/** "16:42" today, "Thu 22 Oct 16:42" another day. */
export function when(text) {
  const d = parseLocal(text);
  if (!d) return "";
  return d.toDateString() === new Date().toDateString() ? time(text) : `${day(text)} ${time(text)}`;
}

/** A moment given by the server (milliseconds), on the phone's own clock. */
export function clock(ms) {
  const d = new Date(ms);
  const t = d.toLocaleTimeString(locale(), { hour: "2-digit", minute: "2-digit", hour12: false });
  return d.toDateString() === new Date().toDateString() ? t : `${d.toLocaleDateString(locale(), { day: "numeric", month: "short" })} ${t}`;
}

export const weekdayName = (index) => new Date(2024, 0, 1 + index).toLocaleDateString(locale(), { weekday: "short" }); // 1 Jan 2024 was a Monday

export const hourLabel = (hour) => `${String(hour).padStart(2, "0")}:00`;

/** Days between a POS date and today (0 = today). */
export function daysAgo(text) {
  const d = parseLocal(text);
  if (!d) return null;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  d.setHours(0, 0, 0, 0);
  return Math.round((today - d) / 86_400_000);
}

/** "3h 20m" from a POS time to now. */
export function duration(text) {
  const d = parseLocal(text);
  if (!d) return "";
  const minutes = Math.max(0, Math.round((Date.now() - d.getTime()) / 60_000));
  const hours = Math.floor(minutes / 60);
  return hours ? `${hours}h ${minutes % 60}m` : `${minutes}m`;
}
