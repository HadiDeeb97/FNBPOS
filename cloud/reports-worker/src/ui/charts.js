// Charts drawn by the page itself (no library): SVG for shapes, HTML for text so labels stay sharp at any width.
// Time always runs left to right, also on the Arabic page.
import { h, s, bdi } from "./dom.js";
import { moneyShort, money, percent } from "./format.js";

export const PALETTE = ["#6366f1", "#14b8a6", "#f59e0b", "#f43f5e", "#0ea5e9", "#8b5cf6", "#84cc16", "#94a3b8"];

/** A round top for an axis: 0, 250, 500… up to a bit above the highest value. */
function niceMax(value) {
  if (!(value > 0)) return 1;
  const exp = Math.pow(10, Math.floor(Math.log10(value)));
  const f = value / exp;
  const nice = f <= 1 ? 1 : f <= 1.5 ? 1.5 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 3 ? 3 : f <= 4 ? 4 : f <= 5 ? 5 : f <= 6 ? 6 : f <= 8 ? 8 : 10;
  return nice * exp;
}

/** The floating box shown while a finger or the mouse is on a chart. */
function tooltipBox(chart) {
  const tip = h("div", { class: "chart-tip", role: "status" });
  chart.append(tip);
  return {
    show(x, lines, alignRight) {
      tip.replaceChildren(...lines.map(([label, value, color, cls]) =>
        h("div", { class: `tip-row ${cls ?? ""}` },
          color ? h("span", { class: "tip-dot", style: `background:${color}` }) : null,
          h("span", { class: "tip-label" }, label), value !== undefined ? h("strong", { class: "num" }, value) : null)));
      tip.style.left = `${x}%`;
      tip.classList.toggle("left", alignRight);
      tip.classList.add("on");
    },
    hide() { tip.classList.remove("on"); },
  };
}

/**
 * Sales over time as an area, with the period before as a dashed line. points: [{ label, value }], compare: [{ label,
 * value }] lined up by position.
 */
export function areaChart({ points, compare = [], height = 240, format = moneyShort, full = money, labels = "auto", currentName, compareName }) {
  if (!points?.length) return h("div", { class: "empty" }, "–");
  const values = points.map((p) => Number(p.value) || 0);
  const others = compare.map((p) => Number(p.value) || 0);
  const max = niceMax(Math.max(...values, ...others, 0));
  const n = points.length;
  const W = 1000;
  const H = 100;
  const x = (i) => (n === 1 ? W / 2 : (i / (n - 1)) * W);
  const y = (v) => H - (v / max) * H;
  const line = (vals) => vals.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(v).toFixed(2)}`).join("");
  const id = `g${Math.random().toString(36).slice(2, 8)}`;

  const svg = s("svg", { class: "chart-svg", viewBox: `0 0 ${W} ${H}`, preserveAspectRatio: "none", "aria-hidden": "true" },
    s("defs", null, s("linearGradient", { id, x1: 0, y1: 0, x2: 0, y2: 1 },
      s("stop", { offset: "0%", "stop-color": "var(--accent)", "stop-opacity": 0.28 }),
      s("stop", { offset: "100%", "stop-color": "var(--accent)", "stop-opacity": 0 }))),
    [0.25, 0.5, 0.75].map((f) => s("line", { class: "grid", x1: 0, x2: W, y1: H * f, y2: H * f })),
    others.length ? s("path", { class: "line-compare", d: line(others.slice(0, Math.max(n, others.length)).map((v, i) => v)) }) : null,
    s("path", { class: "area", d: `${line(values)}L${x(n - 1)},${H}L${x(0)},${H}Z`, fill: `url(#${id})` }),
    s("path", { class: "line-main", d: line(values) }));
  if (others.length && others.length !== n) {
    // The period before can be a day longer or shorter (months): stretch it over the same width.
    const m = others.length;
    svg.querySelector(".line-compare").setAttribute("d", others.map((v, i) => `${i ? "L" : "M"}${(m === 1 ? W / 2 : (i / (m - 1)) * W).toFixed(1)},${y(v).toFixed(2)}`).join(""));
  }

  const yAxis = h("div", { class: "y-axis" }, [1, 0.75, 0.5, 0.25, 0].map((f) => h("span", { style: `top:${(1 - f) * 100}%` }, format(max * f))));
  const every = labels === "auto" ? Math.max(1, Math.ceil(n / 7)) : labels;
  const xAxis = h("div", { class: "x-axis" }, points.map((p, i) =>
    i % every === 0 || i === n - 1 ? h("span", { style: `left:${(x(i) / W) * 100}%` }, p.short ?? p.label) : null));
  const cursor = h("div", { class: "chart-cursor" });
  const dot = h("div", { class: "chart-dot" });
  const plot = h("div", { class: "chart-plot", style: `height:${height}px` }, svg, cursor, dot);
  const chart = h("div", { class: "chart", dir: "ltr" }, yAxis, plot, xAxis);
  const tip = tooltipBox(plot);

  const move = (event) => {
    const rect = plot.getBoundingClientRect();
    const i = Math.max(0, Math.min(n - 1, Math.round(((event.clientX - rect.left) / rect.width) * (n - 1))));
    const px = (x(i) / W) * 100;
    cursor.style.left = `${px}%`;
    dot.style.left = `${px}%`;
    dot.style.top = `${(y(values[i]) / H) * 100}%`;
    cursor.classList.add("on");
    dot.classList.add("on");
    const rows = [[points[i].label], [currentName ?? "", full(values[i]), "var(--accent)"]];
    if (others.length) {
      const j = others.length === n ? i : Math.round((i / Math.max(1, n - 1)) * (others.length - 1));
      const before = others[j];
      rows.push([compare[j]?.label ?? compareName ?? "", full(before), "var(--muted)"]);
      if (before) rows.push(["", percent(((values[i] - before) / Math.abs(before)) * 100, true), null, values[i] >= before ? "good" : "bad"]);
    }
    tip.show(px, rows, px > 60);
  };
  plot.addEventListener("pointermove", move);
  plot.addEventListener("pointerdown", move);
  plot.addEventListener("pointerleave", () => { tip.hide(); cursor.classList.remove("on"); dot.classList.remove("on"); });
  return chart;
}

/** Columns (sales by hour), with ghost columns behind for the comparison. bars: [{ label, value }], ghost: [{ value }]. */
export function barChart({ bars, ghost = [], height = 200, format = moneyShort, full = money, currentName, ghostName, every = 1 }) {
  if (!bars?.length) return h("div", { class: "empty" }, "–");
  const max = niceMax(Math.max(...bars.map((b) => b.value), ...ghost.map((g) => g?.value ?? 0), 0));
  const plot = h("div", { class: "bars", style: `height:${height}px` },
    [0.25, 0.5, 0.75].map((f) => h("i", { class: "bar-grid", style: `bottom:${f * 100}%` })),
    bars.map((b, i) => {
      const g = ghost[i]?.value ?? 0;
      const col = h("div", { class: "bar-col" },
        ghost.length ? h("span", { class: "bar ghost", style: `height:${(g / max) * 100}%` }) : null,
        h("span", { class: "bar now", style: `height:${Math.max(b.value > 0 ? 1.5 : 0, (b.value / max) * 100)}%` }));
      col.addEventListener("pointerenter", () => {
        const rows = [[b.label], [currentName ?? "", full(b.value), "var(--accent)"]];
        if (ghost.length) rows.push([ghostName ?? "", full(g), "var(--muted)"]);
        tip.show(((i + 0.5) / bars.length) * 100, rows, i > bars.length * 0.6);
      });
      return col;
    }));
  plot.addEventListener("pointerleave", () => tip.hide());
  const tip = tooltipBox(plot);
  const yAxis = h("div", { class: "y-axis" }, [1, 0.5, 0].map((f) => h("span", { style: `top:${(1 - f) * 100}%` }, format(max * f))));
  const xAxis = h("div", { class: "bar-axis" }, bars.map((b, i) => h("span", null, i % every === 0 ? b.short ?? b.label : "")));
  return h("div", { class: "chart", dir: "ltr" }, yAxis, plot, xAxis);
}

/** A ring split by share, with the total in the middle and a legend. rows: [{ name, amount, count }] */
export function donut({ rows, center, centerLabel, format = money }) {
  const list = (rows ?? []).filter((r) => Number(r.amount) > 0);
  if (!list.length) return h("div", { class: "empty" }, "–");
  const total = list.reduce((sum, r) => sum + Number(r.amount), 0);
  const R = 15.915; // circumference 100
  let offset = 25; // start at 12 o'clock
  const arcs = list.map((r, i) => {
    const part = (Number(r.amount) / total) * 100;
    const arc = s("circle", { class: "donut-arc", cx: 21, cy: 21, r: R, stroke: PALETTE[i % PALETTE.length],
      "stroke-dasharray": `${Math.max(0, part - 0.6)} ${100 - Math.max(0, part - 0.6)}`, "stroke-dashoffset": offset });
    offset -= part;
    return arc;
  });
  return h("div", { class: "donut" },
    h("div", { class: "donut-ring" },
      s("svg", { viewBox: "0 0 42 42", "aria-hidden": "true" }, s("circle", { class: "donut-track", cx: 21, cy: 21, r: R }), arcs),
      h("div", { class: "donut-center" }, h("strong", { class: "num" }, center ?? format(total)), h("span", null, centerLabel ?? ""))),
    h("ul", { class: "legend" }, list.map((r, i) =>
      h("li", null,
        h("span", { class: "legend-dot", style: `background:${PALETTE[i % PALETTE.length]}` }),
        h("span", { class: "legend-name" }, bdi(r.name)),
        h("span", { class: "legend-share num" }, percent((Number(r.amount) / total) * 100)),
        h("strong", { class: "legend-value num" }, format(r.amount))))));
}

/** A small trend line for a KPI card. */
export function sparkline(values, positive = true) {
  const list = (values ?? []).map((v) => Number(v) || 0);
  if (list.length < 2) return null;
  const max = Math.max(...list, 0);
  const min = Math.min(...list, 0);
  const span = max - min || 1;
  const W = 100;
  const H = 30;
  const pts = list.map((v, i) => [(i / (list.length - 1)) * W, H - ((v - min) / span) * (H - 2) - 1]);
  const d = pts.map(([px, py], i) => `${i ? "L" : "M"}${px.toFixed(1)},${py.toFixed(1)}`).join("");
  return s("svg", { class: `spark ${positive ? "" : "neg"}`, viewBox: `0 0 ${W} ${H}`, preserveAspectRatio: "none", "aria-hidden": "true" },
    s("path", { class: "spark-area", d: `${d}L${W},${H}L0,${H}Z` }),
    s("path", { class: "spark-line", d }));
}

/** When the shop sells: weekdays down, hours across, darker = more. cells: [{ weekday, hour, total, count }] */
export function heatmap({ cells, weekdayName, hourLabel, format = money }) {
  if (!cells?.length) return h("div", { class: "empty" }, "–");
  const hours = cells.map((c) => c.hour);
  const first = Math.min(...hours);
  const last = Math.max(...hours);
  const max = Math.max(...cells.map((c) => c.total), 1);
  const map = new Map(cells.map((c) => [`${c.weekday}:${c.hour}`, c]));
  const cols = last - first + 1;
  const grid = h("div", { class: "heat", dir: "ltr", style: `grid-template-columns: 44px repeat(${cols}, minmax(0, 1fr))` });
  const tip = tooltipBox(grid);
  grid.append(h("span", null));
  for (let hr = first; hr <= last; hr++) grid.append(h("span", { class: "heat-hour" }, (hr - first) % 2 === 0 || cols <= 10 ? String(hr) : ""));
  for (let wd = 0; wd < 7; wd++) {
    grid.append(h("span", { class: "heat-day" }, weekdayName(wd)));
    for (let hr = first; hr <= last; hr++) {
      const c = map.get(`${wd}:${hr}`);
      const level = c ? 0.12 + (c.total / max) * 0.88 : 0;
      const cell = h("span", { class: "heat-cell", style: `--a:${level.toFixed(3)}` });
      cell.addEventListener("pointerenter", () =>
        tip.show(((hr - first + 0.5) / cols) * 100, [[`${weekdayName(wd)} ${hourLabel(hr)}`], ["", c ? format(c.total) : "–", "var(--accent)"], c ? ["", `${c.count}×`] : null].filter(Boolean), hr - first > cols * 0.6));
      grid.append(cell);
    }
  }
  grid.addEventListener("pointerleave", () => tip.hide());
  return grid;
}

/** Over (up, green) or short (down, red) at each drawer close. items: [{ label, value }] */
export function divergingBars({ items, format = money, height = 140 }) {
  if (!items?.length) return h("div", { class: "empty" }, "–");
  const max = Math.max(...items.map((i) => Math.abs(i.value)), 1);
  const plot = h("div", { class: "diverge", dir: "ltr", style: `height:${height}px` },
    h("i", { class: "diverge-zero" }),
    items.map((it, i) => {
      const size = (Math.abs(it.value) / max) * 50;
      const col = h("div", { class: "diverge-col" },
        h("span", { class: `diverge-bar ${it.value >= 0 ? "up" : "down"}`, style: it.value >= 0 ? `bottom:50%;height:${size}%` : `top:50%;height:${size}%` }));
      col.addEventListener("pointerenter", () => tip.show(((i + 0.5) / items.length) * 100, [[it.label], ["", (it.value > 0 ? "+" : "") + format(it.value), null, it.value < 0 ? "bad" : it.value > 0 ? "good" : ""]], i > items.length * 0.6));
      return col;
    }));
  const tip = tooltipBox(plot);
  plot.addEventListener("pointerleave", () => tip.hide());
  return plot;
}

/** Progress towards a target, as a ring. */
export function ring(fraction, label, sub) {
  const f = Math.max(0, Math.min(1, fraction));
  const R = 15.915;
  return h("div", { class: "ring" },
    s("svg", { viewBox: "0 0 42 42", "aria-hidden": "true" },
      s("circle", { class: "ring-track", cx: 21, cy: 21, r: R }),
      s("circle", { class: `ring-fill ${fraction >= 1 ? "done" : ""}`, cx: 21, cy: 21, r: R, "stroke-dasharray": `${f * 100} ${100 - f * 100}`, "stroke-dashoffset": 25 })),
    h("div", { class: "ring-center" }, h("strong", { class: "num" }, label), sub ? h("span", null, sub) : null));
}

/** A thin bar showing a row's share. */
export const shareBar = (fraction, color) =>
  h("span", { class: "share-bar" }, h("span", { style: `width:${Math.max(0, Math.min(100, fraction * 100)).toFixed(1)}%${color ? `;background:${color}` : ""}` }));
