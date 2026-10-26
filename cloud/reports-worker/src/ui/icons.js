// Line icons (24×24, drawn with the current text colour).
import { s } from "./dom.js";

const PATHS = {
  overview: ["M3 13h8V3H3z", "M13 21h8V11h-8z", "M3 21h8v-6H3z", "M13 3v6h8V3z"],
  analytics: ["M3 3v18h18", "M7 15l4-4 3 3 5-6"],
  sales: ["M6 2h12v20l-3-2-3 2-3-2-3 2z", "M9 7h6", "M9 11h6", "M9 15h4"],
  drawers: ["M3 7h18v13H3z", "M3 7l2-4h14l2 4", "M9 12h6"],
  stock: ["M21 8l-9-5-9 5 9 5 9-5z", "M3 8v8l9 5 9-5V8", "M12 13v8"],
  refresh: ["M21 12a9 9 0 1 1-3-6.7L21 8", "M21 3v5h-5"],
  sun: ["M12 4V2", "M12 22v-2", "M4.9 4.9 3.5 3.5", "M20.5 20.5l-1.4-1.4", "M4 12H2", "M22 12h-2", "M4.9 19.1l-1.4 1.4", "M20.5 3.5l-1.4 1.4", "M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z"],
  moon: ["M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"],
  globe: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z", "M2 12h20", "M12 2a15 15 0 0 1 0 20", "M12 2a15 15 0 0 0 0 20"],
  logout: ["M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "M16 17l5-5-5-5", "M21 12H9"],
  download: ["M12 3v12", "M7 10l5 5 5-5", "M5 21h14"],
  printer: ["M6 9V2h12v7", "M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2", "M6 14h12v8H6z"],
  search: ["M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16z", "M21 21l-4.3-4.3"],
  close: ["M18 6 6 18", "M6 6l12 12"],
  up: ["M7 14l5-5 5 5"],
  down: ["M7 10l5 5 5-5"],
  alert: ["M12 9v4", "M12 17h.01", "M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"],
  spark: ["M12 3l1.9 5.6L19.5 10l-5.6 1.9L12 17.5l-1.9-5.6L4.5 10l5.6-1.4z", "M19 17l.8 2.2L22 20l-2.2.8L19 23l-.8-2.2L16 20l2.2-.8z"],
  clock: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z", "M12 6v6l4 2"],
  trend: ["M3 17l6-6 4 4 8-8", "M14 7h7v7"],
  target: ["M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z", "M12 18a6 6 0 1 0 0-12 6 6 0 0 0 0 12z", "M12 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4z"],
  eye: ["M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"],
  user: ["M20 21a8 8 0 0 0-16 0", "M12 13a5 5 0 1 0 0-10 5 5 0 0 0 0 10z"],
  tag: ["M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8z", "M7.5 7.5h.01"],
  cart: ["M3 3h2l2.4 12.2a2 2 0 0 0 2 1.6h8.8a2 2 0 0 0 2-1.6L22 7H6", "M9 21h.01", "M18 21h.01"],
  wifi: ["M5 12.6a10 10 0 0 1 14 0", "M8.5 16.1a5 5 0 0 1 7 0", "M2 9a15 15 0 0 1 20 0", "M12 20h.01"],
};

export function icon(name, size = 18) {
  return s("svg", { class: "icon", width: size, height: size, viewBox: "0 0 24 24", fill: "none", stroke: "currentColor",
    "stroke-width": 1.8, "stroke-linecap": "round", "stroke-linejoin": "round", "aria-hidden": "true" },
    (PATHS[name] ?? []).map((d) => s("path", { d })));
}
