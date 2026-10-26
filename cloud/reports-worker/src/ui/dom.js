// Building the page. Everything is made with textContent and attributes, never innerHTML, so product names and other
// text from the POS can't inject anything. Styles go through the style object (the page allows no inline style attributes).

const SVG_NS = "http://www.w3.org/2000/svg";

function apply(el, attrs) {
  for (const [key, value] of Object.entries(attrs ?? {})) {
    if (value === null || value === undefined || value === false) continue;
    if (key === "class") el.setAttribute("class", value);
    else if (key === "style") el.style.cssText = value;
    else if (key.startsWith("on") && typeof value === "function") el.addEventListener(key.slice(2), value);
    else if (key === "dataset") Object.assign(el.dataset, value);
    else el.setAttribute(key, value === true ? "" : String(value));
  }
}

function append(el, children) {
  for (const child of children.flat(Infinity)) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

/** An HTML element: h("div", { class: "card" }, "text", h("span", null, "more")). */
export function h(tag, attrs, ...children) {
  const el = document.createElement(tag);
  apply(el, attrs);
  append(el, children);
  return el;
}

/** An SVG element, for charts and icons. */
export function s(tag, attrs, ...children) {
  const el = document.createElementNS(SVG_NS, tag);
  apply(el, attrs);
  append(el, children);
  return el;
}

export const $ = (id) => document.getElementById(id);

/** A name from the POS keeps its own direction on a page in the other language (and its punctuation in place). */
export const bdi = (text) => h("bdi", null, text ?? "");

export const reducedMotion = () => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;

/** Saves a table as a CSV file (with a byte-order mark, so Excel opens Arabic correctly). */
export function downloadCsv(name, headers, rows) {
  const cell = (v) => {
    const text = v === null || v === undefined ? "" : String(v);
    return /[",\n;]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  };
  const csv = "﻿" + [headers, ...rows].map((r) => r.map(cell).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const a = h("a", { href: url, download: `${name}.csv` });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
