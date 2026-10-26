// The five pages. Each says which reports it needs (keys) and builds itself from them (render).
import { h, bdi, reducedMotion, downloadCsv } from "./dom.js";
import { t } from "./i18n.js";
import * as f from "./format.js";
import { icon } from "./icons.js";
import { areaChart, barChart, donut, sparkline, heatmap, divergingBars, ring, shareBar } from "./charts.js";
import { insights, monthForecast } from "./insights.js";

const PERIODS = ["today", "yesterday", "week", "lastweek", "month", "lastmonth", "year"];

// ---- Building blocks ----------------------------------------------------------------------------------------------

function card({ title, sub, actions, cls }, ...body) {
  return h("section", { class: `card ${cls ?? ""}` },
    title ? h("div", { class: "card-head" }, h("div", { class: "t" }, h("h2", null, title), sub ? h("div", { class: "s" }, sub) : null),
      actions?.length ? h("div", { class: "card-actions no-print" }, actions) : null) : null,
    ...body);
}

function delta(value, label) {
  if (value === null || value === undefined) return h("span", { class: "delta flat" }, t("noComparison"));
  const cls = value > 0.05 ? "up" : value < -0.05 ? "down" : "flat";
  return [h("span", { class: `delta ${cls}` }, cls === "flat" ? null : icon(cls === "up" ? "up" : "down", 14), f.percent(Math.abs(value))), label ? h("span", null, label) : null];
}

/** Counts a KPI up from zero to its value (skipped when the phone asks for less motion). */
function countUp(el, value, format) {
  el.textContent = format(value);
  if (reducedMotion() || !Number.isFinite(value) || value === 0) return;
  const start = performance.now();
  const step = (now) => {
    const p = Math.min(1, (now - start) / 700);
    el.textContent = format(value * (1 - Math.pow(1 - p, 3)));
    if (p < 1 && el.isConnected) requestAnimationFrame(step);
    else el.textContent = format(value);
  };
  requestAnimationFrame(step);
}

function kpi({ label, value, format = f.money, foot, spark, sparkGood = true, hero, iconName }) {
  const v = h("div", { class: "value num" });
  countUp(v, Number(value ?? 0), format);
  return h("div", { class: `kpi ${hero ? "hero" : ""}` },
    h("div", { class: "label" }, iconName ? icon(iconName, 15) : null, label), v,
    h("div", { class: "foot" }, foot), spark ? sparkline(spark, sparkGood) : null);
}

const csvButton = (name, headers, rows) =>
  h("button", { class: "chip-btn export", type: "button", onclick: () => downloadCsv(name, headers, rows()) }, icon("download", 14), t("exportCsv"));

/** A table whose columns can be sorted by tapping their heading. */
function sortTable(ctx, id, columns, rows, defaultKey, defaultDesc = true) {
  const ui = (ctx.state.ui[id] ??= { key: defaultKey, desc: defaultDesc });
  const col = columns.find((c) => c.key === ui.key);
  const sorted = col?.value ? [...rows].sort((a, b) => {
    const x = col.value(a);
    const y = col.value(b);
    const cmp = typeof x === "string" ? x.localeCompare(y) : (x ?? -Infinity) - (y ?? -Infinity);
    return ui.desc ? -cmp : cmp;
  }) : rows;
  return h("div", { class: "table-wrap" }, h("table", null,
    h("thead", null, h("tr", null, columns.map((c) => h("th", {
      class: [c.end ? "end" : "", c.value ? "sortable" : "", c.key === ui.key ? "sorted" : ""].join(" "),
      onclick: c.value ? () => { ui.desc = ui.key === c.key ? !ui.desc : true; ui.key = c.key; ctx.rerender(); } : null,
    }, c.label, c.key === ui.key && c.value ? (ui.desc ? " ↓" : " ↑") : "")))),
    h("tbody", null, sorted.map((r, i) => h("tr", null, columns.map((c) => h("td", { class: c.end ? "end" : null }, c.render(r, i))))))));
}

function nameRows(list, { meta, amount = (r) => f.money(r.amount), limit } = {}) {
  const rows = (list ?? []).slice(0, limit ?? 99);
  if (!rows.length) return h("div", { class: "empty" }, t("nothing"));
  const max = Math.max(...rows.map((r) => Math.abs(Number(r.amount ?? 0))), 0) || 1;
  return h("div", { class: "rows" }, rows.map((r) =>
    h("div", { class: "row" },
      h("span", { class: "name", title: r.name }, bdi(r.name)),
      h("span", { class: "num", style: "font-weight:600" }, amount(r)),
      meta ? h("span", { class: "meta" }, meta(r)) : null,
      shareBar(Math.abs(Number(r.amount ?? 0)) / max))));
}

const pcsMeta = (r) => [r.qty ? t("pieces", f.number(r.qty)) : null, r.share ? f.percent(r.share) : null].filter(Boolean).join(" · ");
const countMeta = (r) => [r.count ? `${f.number(r.count)}×` : null, r.share ? f.percent(r.share) : null].filter(Boolean).join(" · ");

function trendBadge(now, before) {
  if (before === undefined || before === null) return "";
  if (!before) return now > 0 ? h("span", { class: "trend-new" }, t("newItem")) : "–";
  const change = ((now - before) / Math.abs(before)) * 100;
  return h("span", { class: change >= 0 ? "trend-up" : "trend-down" }, icon(change >= 0 ? "up" : "down", 14), f.percent(Math.abs(change)));
}

const hasDocs = (...docs) => docs.every((d) => d);

function noData(ctx) {
  return ctx.emptyState("analytics", t("noData"), t("noDataHint"));
}

function legend(currentName, compareName) {
  return h("div", { class: "legend-inline" },
    h("span", null, h("i"), currentName),
    compareName ? h("span", null, h("i", { class: "cmp" }), compareName) : null);
}

/** Every hour from the first to the last with sales (today or the comparison), so quiet hours show as gaps. */
function hourSeries(current, previous) {
  const all = [...(current ?? []), ...(previous ?? [])].map((x) => x.hour);
  if (!all.length) return { bars: [], ghost: [] };
  const first = Math.min(...all);
  const last = Math.max(...all);
  const bars = [];
  const ghost = [];
  for (let hr = first; hr <= last; hr++) {
    bars.push({ label: f.hourLabel(hr), short: String(hr), value: Number(current?.find((x) => x.hour === hr)?.total ?? 0) });
    ghost.push({ value: Number(previous?.find((x) => x.hour === hr)?.total ?? 0) });
  }
  return { bars, ghost };
}

function dayPoints(byDay, from, to) {
  // Every day of the period (a day without sales is a zero, not a gap), up to today.
  const map = new Map((byDay ?? []).map((d) => [d.date, d]));
  const start = f.parseLocal(from);
  const end = f.parseLocal(to);
  const today = new Date();
  const points = [];
  for (let d = new Date(start); d <= end && d <= today; d.setDate(d.getDate() + 1)) {
    const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
    const x = map.get(key);
    points.push({ label: f.day(key, { year: false }), short: String(d.getDate()), value: Number(x?.total ?? 0), profit: Number(x?.profit ?? 0), date: key });
  }
  return points;
}

function greeting(name) {
  const hour = new Date().getHours();
  const first = (name ?? "").split(" ")[0];
  return t(hour < 12 ? "greetingMorning" : hour < 18 ? "greetingAfternoon" : "greetingEvening", first);
}

const initials = (name) => (name ?? "").trim().split(/\s+/).slice(0, 2).map((w) => w[0] ?? "").join("").toUpperCase() || "•";

// ---- Overview -----------------------------------------------------------------------------------------------------

const overview = {
  keys: () => ["period:today", "period:month", "live", "stock"],
  render(ctx) {
    const { state } = ctx;
    const today = state.docs["period:today"];
    const month = state.docs["period:month"];
    const live = state.docs.live;
    const stock = state.docs.stock;
    const nodes = [];
    nodes.push(h("div", { class: "hello" },
      h("div", null, h("h2", null, greeting(state.me.user.name)), h("p", null, t("todayAt", f.longDay(new Date())))),
      h("a", { class: "chip-btn no-print", href: "#analytics" }, icon("analytics", 14), t("nav_analytics"))));

    if (live?.lowStockCount) {
      nodes.push(h("div", { class: "banner" }, icon("alert", 18), h("span", { class: "grow" }, t("lowStockBanner", f.number(live.lowStockCount))),
        h("a", { href: "#stock" }, t("review"))));
    }
    if (!today) return { nodes: [...nodes, noData(ctx)] };

    const s = today.totals;
    const trend = live?.trend14 ?? [];
    const basket = s.transactions ? s.itemsSold / s.transactions : 0;
    const returnRate = s.totalSales ? (s.refunds / s.totalSales) * 100 : 0;
    nodes.push(h("div", { class: "kpis" },
      kpi({ label: t("totalSales"), value: s.totalSales, hero: true, iconName: "cart", foot: delta(today.change?.sales, t("vsYesterday")), spark: trend.map((d) => d.total) }),
      kpi({ label: t("profit"), value: s.grossProfit, iconName: "trend", foot: [delta(today.change?.profit), h("span", null, t("marginOf", f.percent(s.marginPercent)))], spark: trend.map((d) => d.profit) }),
      kpi({ label: t("transactions"), value: s.transactions, format: f.number, iconName: "sales", foot: delta(today.change?.transactions, t("vsYesterday")), spark: trend.map((d) => d.count) }),
      kpi({ label: t("avgBasket"), value: s.averageBasket, iconName: "tag", foot: h("span", null, t("pieces", f.number(s.itemsSold))) }),
      kpi({ label: t("itemsPerBasket"), value: basket, format: f.decimal1, iconName: "stock", foot: h("span", null, t("pieces", f.number(s.itemsSold))) }),
      kpi({ label: t("returnRate"), value: returnRate, format: (v) => f.percent(v), iconName: "refresh", foot: h("span", null, f.money(s.refunds)) })));

    // Sales by hour against the same weekday last week, and the month's target.
    const hours = hourSeries(today.byHour, today.previousByHour);
    const forecast = monthForecast(month);
    const target = Number(state.me.store?.monthlyTarget ?? 0);
    const monthSales = Number(month?.totals?.totalSales ?? 0);
    nodes.push(h("div", { class: "grid g-main" },
      card({ title: t("salesByHour"), sub: today.previousByHour?.length ? legend(t("todayLabel"), t("sameDayLastWeek")) : null },
        hours.bars.length ? barChart({ bars: hours.bars, ghost: today.previousByHour?.length ? hours.ghost : [], currentName: t("todayLabel"), ghostName: t("sameDayLastWeek"), every: hours.bars.length > 12 ? 2 : 1 })
          : h("div", { class: "empty" }, t("insQuiet"))),
      card({ title: target ? t("target") : t("forecast"), sub: forecast ? t("daysLeftInMonth", forecast.daysLeft) : null },
        h("div", { class: "target" },
          target ? ring(monthSales / target, f.percent((monthSales / target) * 100), t("targetOf", f.money0(target)))
            : ring(forecast ? forecast.sales / forecast.forecast : 0, f.moneyShort(monthSales), t("period_month")), // how far through the month
          h("div", { class: "target-facts" },
            h("div", { class: "fact" }, h("div", { class: "k" }, t("period_month")), h("div", { class: "v num" }, f.money0(monthSales))),
            forecast ? h("div", { class: "fact" }, h("div", { class: "k" }, t("forecast")), h("div", { class: "v num" }, f.money0(forecast.forecast)), h("div", { class: "k" }, t("forecastNote"))) : null)),
        target ? null : h("p", { class: "muted", style: "font-size:12.5px;margin:14px 0 0" }, t("targetNone")))));

    const notes = insights({ today, month, live, stock, target });
    nodes.push(h("div", { class: "grid g-main" },
      card({ title: t("insights"), sub: null },
        notes.length ? h("div", { class: "insights" }, notes.map(([tone, ic, text]) =>
          h("div", { class: `insight ${tone}` }, h("span", { class: "ic" }, icon(ic, 16)), h("span", null, text)))) : h("div", { class: "empty" }, t("nothing"))),
      card({ title: t("openDrawers"), actions: [h("a", { class: "chip-btn", href: "#drawers" }, t("seeAll"))] }, drawerList(live, true))));

    nodes.push(h("div", { class: "grid g-2" },
      card({ title: t("recentSales"), actions: [h("a", { class: "chip-btn", href: "#sales" }, t("seeAll"))] }, salesFeed(ctx, (live?.recentSales ?? []).slice(0, 6))),
      card({ title: t("topProductsToday") }, nameRows(today.products, { limit: 6, meta: (r) => `${t("pieces", f.number(r.qty))} · ${t("profit")} ${f.money(r.profit)}` }))));
    return { nodes };
  },
};

function drawerList(live, compact) {
  const drawers = live?.openDrawers ?? [];
  if (!drawers.length) return h("div", { class: "empty" }, t("noOpenDrawers"));
  const showLbp = drawers.some((d) => d.expectedCashLbp);
  return h("div", { class: "rows" }, drawers.map((d) => h("div", { class: "row" },
    h("span", { class: "name" }, bdi(d.till ? `${d.till} · ${d.cashier}` : d.cashier)),
    h("span", { class: "num", style: "font-weight:600" }, f.money(d.expectedCash)),
    h("span", { class: "meta" }, `${t("openFor", f.duration(d.openedAt))} · ${f.number(d.sales)}×${compact ? "" : ""}`),
    showLbp ? h("span", { class: "meta num end" }, f.lbp(d.expectedCashLbp)) : null)));
}

function salesFeed(ctx, sales) {
  if (!sales.length) return h("div", { class: "empty" }, t("nothing"));
  return h("div", { class: "feed" }, sales.map((s) => h("div", { class: "feed-item", role: "button", tabindex: 0, onclick: () => saleSheet(ctx, s), onkeydown: (e) => { if (e.key === "Enter") saleSheet(ctx, s); } },
    h("span", { class: "feed-ic" }, icon(s.channel ? "globe" : "sales", 17)),
    h("span", { class: "feed-main" },
      h("strong", null, bdi(s.receipt), " ", s.status === "voided" ? h("span", { class: "tag bad" }, t("voidedTag")) : null, s.channel ? [" ", h("span", { class: "tag" }, bdi(s.channel))] : null),
      h("span", null, [f.when(s.time), s.cashier, s.items ? t("pieces", f.number(s.items)) : null].filter(Boolean).join(" · "))),
    h("span", { class: `feed-amt num ${s.status === "voided" ? "muted" : ""}` }, f.money(s.total)))));
}

function saleSheet(ctx, s) {
  ctx.openSheet(h("div", null,
    h("div", { class: "card-head" },
      h("div", { class: "t" }, h("h2", { style: "font-size:18px" }, t("saleDetails", s.receipt)), h("div", { class: "s" }, [f.when(s.time), s.cashier, s.channel ?? t("inStore")].filter(Boolean).join(" · "))),
      h("button", { class: "icon-btn", type: "button", "aria-label": "close", onclick: ctx.closeSheet }, icon("close", 16))),
    s.status === "voided" ? h("div", { class: "banner", style: "margin-bottom:8px" }, icon("alert", 16), t("voidedTag")) : null,
    h("div", { class: "detail-lines" }, (s.lines ?? []).map((l) => h("div", { class: "detail-line" },
      h("span", { class: "q num" }, `${l.qty}×`),
      h("span", { class: "n" }, bdi(l.product), l.variant ? h("span", null, bdi(l.variant)) : null),
      h("strong", { class: "num" }, f.money(l.amount))))),
    h("div", { class: "detail-total" }, h("span", null, t("total")), h("span", { class: "num" }, f.money(s.total)))));
}

// ---- Analytics ----------------------------------------------------------------------------------------------------

function rangeText(doc) {
  if (!doc) return "";
  return doc.from === doc.to ? f.longDay(doc.from) : `${f.day(doc.from, { year: true })} – ${f.day(doc.to, { year: true })}`;
}

const analytics = {
  keys: (ctx) => [ctx.state.report],
  render(ctx) {
    const { state } = ctx;
    const doc = state.docs[state.report];
    const pick = (key) => ctx.navigate("analytics", key);
    const days = state.me.days ?? [];
    const months = state.me.months ?? [];
    const toolbar = h("div", { class: "toolbar no-print" },
      h("div", { class: "segmented" }, PERIODS.map((p) =>
        h("button", { type: "button", class: state.report === `period:${p}` ? "active" : null, onclick: () => pick(`period:${p}`) }, t(`period_${p}`)))),
      days.length ? h("select", { class: "select", "aria-label": t("pickDay"), onchange: (e) => e.target.value && pick(`day:${e.target.value}`) },
        h("option", { value: "" }, `${t("pickDay")}…`), days.map((d) => h("option", { value: d, selected: state.report === `day:${d}` }, f.day(d, { year: true })))) : null,
      months.length ? h("select", { class: "select", "aria-label": t("pickMonth"), onchange: (e) => e.target.value && pick(`month:${e.target.value}`) },
        h("option", { value: "" }, `${t("pickMonth")}…`), months.map((m) => h("option", { value: m, selected: state.report === `month:${m}` }, f.month(m)))) : null,
      h("span", { class: "spacer" }),
      h("label", { class: "switch" }, h("input", { type: "checkbox", checked: state.compare, onchange: (e) => { state.compare = e.target.checked; ctx.rerender(); } }), t("compare")),
      h("button", { class: "chip-btn export", type: "button", onclick: () => window.print() }, icon("printer", 14), t("print")));

    const title = state.report.startsWith("period:") ? t(`period_${state.report.slice(7)}`)
      : state.report.startsWith("month:") ? f.month(state.report.slice(6)) : f.day(state.report.slice(4), { year: true });
    if (!doc) return { title: t("nav_analytics"), subtitle: title, nodes: [toolbar, noData(ctx)] };

    const s = doc.totals;
    const multiDay = doc.from !== doc.to;
    const name = `${state.me.store?.name ?? "shop"}-${doc.from}${multiDay ? `-${doc.to}` : ""}`;
    const nodes = [toolbar,
      h("div", { class: "print-head" }, h("div", null, h("h1", null, `${state.me.store?.name ?? ""} · ${title}`), h("div", null, rangeText(doc))),
        h("div", null, t("printedAt", f.clock(Date.now()))))];

    nodes.push(h("div", { class: "kpis" },
      kpi({ label: t("totalSales"), value: s.totalSales, hero: true, iconName: "cart", foot: delta(doc.change?.sales, t("vsPrevious")) }),
      kpi({ label: t("netRevenue"), value: s.netRevenue, iconName: "analytics", foot: h("span", null, `${t("tax")} ${f.money(s.tax)}`) }),
      kpi({ label: t("profit"), value: s.grossProfit, iconName: "trend", foot: [delta(doc.change?.profit), h("span", null, t("marginOf", f.percent(s.marginPercent)))] }),
      kpi({ label: t("transactions"), value: s.transactions, format: f.number, iconName: "sales", foot: delta(doc.change?.transactions, t("vsPrevious")) }),
      kpi({ label: t("avgBasket"), value: s.averageBasket, iconName: "tag", foot: h("span", null, `${f.decimal1(s.transactions ? s.itemsSold / s.transactions : 0)} ${t("itemsPerBasket").toLowerCase()}`) }),
      kpi({ label: t("itemsSold"), value: s.itemsSold, format: f.number, iconName: "stock", foot: h("span", null, `${t("returnRate")} ${f.percent(s.totalSales ? (s.refunds / s.totalSales) * 100 : 0)}`) })));

    // Trend: day by day for a period, hour by hour for a single day.
    if (multiDay) {
      const points = dayPoints(doc.byDay, doc.from, doc.to);
      const previous = state.compare ? dayPoints(doc.previousByDay, shiftDate(doc.from, -spanDays(doc)), shiftDate(doc.from, -1)) : [];
      nodes.push(card({ title: t("trend"), sub: legend(t("currentPeriod"), previous.length ? t("previousPeriod") : null),
        actions: [csvButton(`${name}-days`, [t("time"), t("totalSales"), t("profit")], () => points.map((p) => [p.date, p.value, p.profit]))] },
        areaChart({ points, compare: previous, currentName: t("currentPeriod"), compareName: t("previousPeriod"), height: 260 })));
    } else {
      const hours = hourSeries(doc.byHour, state.compare ? doc.previousByHour : []);
      nodes.push(card({ title: t("salesByHour"), sub: state.compare && doc.previousByHour?.length ? legend(rangeText(doc), t("sameDayLastWeek")) : null },
        hours.bars.length ? barChart({ bars: hours.bars, ghost: state.compare ? hours.ghost : [], currentName: f.day(doc.from), ghostName: t("sameDayLastWeek"), every: hours.bars.length > 12 ? 2 : 1, height: 240 }) : h("div", { class: "empty" }, t("nothing"))));
    }

    const summary = card({ title: t("summary") }, summaryTable(s));
    if (multiDay) {
      const points = dayPoints(doc.byDay, doc.from, doc.to);
      nodes.push(h("div", { class: "grid g-main" },
        card({ title: t("profitTrend"), sub: t("marginOf", f.percent(s.marginPercent)) },
          areaChart({ points: points.map((p) => ({ ...p, value: p.profit })), currentName: t("profit"), height: 220 })),
        summary));
      if (doc.heatmap?.length) {
        nodes.push(card({ title: t("busiestTimes"), sub: t("busiestHint") }, heatmap({ cells: doc.heatmap, weekdayName: f.weekdayName, hourLabel: f.hourLabel })));
      }
    }

    nodes.push(h("div", { class: "grid g-2" },
      card({ title: t("payments") }, donut({ rows: doc.payments, centerLabel: t("totalSales") })),
      multiDay ? card({ title: t("channels") }, donut({ rows: doc.channels, centerLabel: t("transactions"), center: f.number(s.transactions) })) : summary));
    if (!multiDay && doc.channels?.length > 1) nodes.push(card({ title: t("channels") }, donut({ rows: doc.channels })));

    // Categories and products.
    const cats = doc.categories ?? [];
    const catMax = Math.max(...cats.map((c) => c.amount), 1);
    nodes.push(card({ title: t("categories"), actions: [csvButton(`${name}-categories`, [t("category"), t("qty"), t("revenue"), t("profit"), t("share")], () => cats.map((c) => [c.name, c.qty, c.amount, c.profit, c.share]))] },
      cats.length ? sortTable(ctx, "cats", [
        { key: "rank", label: t("rank"), render: (r, i) => h("span", { class: "rank" }, i + 1) },
        { key: "name", label: t("category"), value: (r) => r.name, render: (r) => h("strong", null, bdi(r.name)) },
        { key: "qty", label: t("qty"), end: true, value: (r) => r.qty, render: (r) => h("span", { class: "num" }, f.number(r.qty)) },
        { key: "amount", label: t("revenue"), value: (r) => r.amount, render: (r) => h("div", { class: "cell-share" }, h("span", { class: "num", style: "font-weight:600" }, f.money(r.amount)), shareBar(r.amount / catMax)) },
        { key: "share", label: t("share"), end: true, value: (r) => r.share, render: (r) => h("span", { class: "num muted" }, f.percent(r.share)) },
        { key: "profit", label: t("profit"), end: true, value: (r) => r.profit, render: (r) => h("span", { class: "num" }, f.money(r.profit)) },
        { key: "margin", label: t("margin"), end: true, value: (r) => (r.amount ? r.profit / r.amount : 0), render: (r) => h("span", { class: "num muted" }, f.percent(r.amount ? (r.profit / r.amount) * 100 : 0)) },
      ], cats, "amount") : h("div", { class: "empty" }, t("nothing"))));

    const products = doc.products ?? [];
    nodes.push(card({ title: t("topProducts"), sub: state.compare ? t("vsPrevious") : null,
      actions: [csvButton(`${name}-products`, [t("product"), t("qty"), t("revenue"), t("profit")], () => products.map((p) => [p.name, p.qty, p.amount, p.profit]))] },
      products.length ? sortTable(ctx, "products", [
        { key: "rank", label: t("rank"), render: (r, i) => h("span", { class: "rank" }, i + 1) },
        { key: "name", label: t("product"), value: (r) => r.name, render: (r) => h("strong", null, bdi(r.name)) },
        { key: "qty", label: t("qty"), end: true, value: (r) => r.qty, render: (r) => h("span", { class: "num" }, f.number(r.qty)) },
        { key: "amount", label: t("revenue"), end: true, value: (r) => r.amount, render: (r) => h("span", { class: "num", style: "font-weight:600" }, f.money(r.amount)) },
        { key: "profit", label: t("profit"), end: true, value: (r) => r.profit, render: (r) => h("span", { class: "num" }, f.money(r.profit)) },
        state.compare ? { key: "trend", label: t("trendCol"), end: true, value: (r) => (r.previousAmount ? (r.amount - r.previousAmount) / Math.abs(r.previousAmount) : Infinity), render: (r) => trendBadge(r.amount, r.previousAmount) } : null,
      ].filter(Boolean), products, "qty") : h("div", { class: "empty" }, t("nothing"))));

    // People: cashiers and customers.
    const cashiers = doc.cashiers ?? [];
    const customers = doc.customers;
    nodes.push(h("div", { class: "grid g-main" },
      card({ title: t("cashiers") }, cashiers.length ? sortTable(ctx, "cashiers", [
        { key: "name", label: t("cashier"), value: (r) => r.name, render: (r) => h("span", { style: "display:flex;gap:10px;align-items:center" }, h("span", { class: "avatar" }, initials(r.name)), h("strong", null, bdi(r.name))) },
        { key: "count", label: t("salesCol"), end: true, value: (r) => r.count, render: (r) => h("span", { class: "num" }, f.number(r.count)) },
        { key: "amount", label: t("revenue"), end: true, value: (r) => r.amount, render: (r) => h("span", { class: "num", style: "font-weight:600" }, f.money(r.amount)) },
        { key: "avg", label: t("avgCol"), end: true, value: (r) => (r.count ? r.amount / r.count : 0), render: (r) => h("span", { class: "num" }, f.money(r.count ? r.amount / r.count : 0)) },
        { key: "share", label: t("share"), end: true, value: (r) => r.share, render: (r) => h("span", { class: "num muted" }, f.percent(r.share)) },
      ], cashiers, "amount") : h("div", { class: "empty" }, t("nothing"))),
      card({ title: t("customers"), sub: t("customersHint") }, customers ? h("div", { class: "till-stats" },
        stat(t("identifiedSales"), f.number(customers.sales)),
        stat(t("distinctCustomers"), f.number(customers.customers)),
        stat(t("returningCustomers"), f.number(customers.returning)),
        stat(t("newCustomers"), f.number(Math.max(0, customers.customers - customers.returning)))) : h("div", { class: "empty" }, t("nothing")),
        h("div", { style: "margin-top:18px" }, h("h2", { style: "font-size:14px;margin:0 0 12px" }, t("sizes")), nameRows(doc.sizes, { limit: 8, meta: pcsMeta })))));

    // Returns.
    if (doc.returnsByReason?.length || doc.topReturned?.length) {
      nodes.push(h("div", { class: "grid g-3" },
        card({ title: t("returnsByReason") }, nameRows(doc.returnsByReason, { meta: countMeta })),
        card({ title: t("topReturned") }, nameRows(doc.topReturned, { limit: 8, meta: pcsMeta })),
        card({ title: t("refundsByMethod") }, nameRows(doc.refundsByMethod, { meta: countMeta }))));
    }

    if (doc.settlements?.length) {
      nodes.push(card({ title: t("settlements") }, sortTable(ctx, "settle", [
        { key: "date", label: t("time"), value: (r) => r.date, render: (r) => h("span", { class: "num" }, f.when(r.date)) },
        { key: "courier", label: t("courier"), value: (r) => r.courier ?? "", render: (r) => bdi(r.courier ?? r.method) },
        { key: "orders", label: t("orders"), end: true, value: (r) => r.orders, render: (r) => h("span", { class: "num" }, f.number(r.orders)) },
        { key: "expected", label: t("expected"), end: true, value: (r) => r.expected, render: (r) => h("span", { class: "num" }, f.money(r.expected)) },
        { key: "received", label: t("received"), end: true, value: (r) => r.received, render: (r) => h("span", { class: "num" }, f.money(r.received)) },
        { key: "diff", label: t("difference"), end: true, value: (r) => r.received - r.expected, render: (r) => signedMoney(r.received - r.expected) },
      ], doc.settlements, "date")));
    }
    if (doc.drawerCloses?.length) nodes.push(card({ title: t("drawerCloses") }, closesTable(ctx, doc.drawerCloses, "closesA")));
    return { title: t("nav_analytics"), subtitle: `${title} · ${rangeText(doc)}`, nodes };
  },
};

function spanDays(doc) {
  return Math.round((f.parseLocal(doc.to) - f.parseLocal(doc.from)) / 86_400_000) + 1;
}

function shiftDate(text, days) {
  const d = f.parseLocal(text);
  d.setDate(d.getDate() + days);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function summaryTable(s) {
  const line = (label, value, cls) => h("tr", null, h("td", null, label), h("td", { class: `end num ${cls ?? ""}` }, value));
  return h("table", null, h("tbody", null,
    line(t("grossSales"), f.money(s.grossSales)),
    line(t("discounts"), `-${f.money(s.discounts)}`),
    line(t("totalSales"), f.money(s.totalSales)),
    line(t("tax"), f.money(s.tax)),
    line(`${t("refunds")} (${f.number(s.returnsCount)})`, `-${f.money(s.refunds)}`),
    line(t("netRevenue"), f.money(s.netRevenue)),
    line(t("costOfGoods"), `-${f.money(s.costOfGoods)}`),
    h("tr", null, h("td", null, h("strong", null, t("profit"))), h("td", { class: "end num" }, h("strong", { class: s.grossProfit < 0 ? "bad" : "" }, f.money(s.grossProfit)), h("span", { class: "sub" }, t("marginOf", f.percent(s.marginPercent))))),
    s.deliveryFees ? line(t("deliveryFees"), f.money(s.deliveryFees)) : null,
    s.voidedCount ? line(t("voided"), f.number(s.voidedCount), "muted") : null));
}

const stat = (k, v) => h("div", { class: "stat" }, h("div", { class: "k" }, k), h("div", { class: "v num" }, v));

function signedMoney(value, format = f.money) {
  const cls = value < 0 ? "bad" : value > 0 ? "good" : "muted";
  return h("span", { class: `num ${cls}`, style: "font-weight:600" }, (value > 0 ? "+" : "") + format(value));
}

function closesTable(ctx, closes, id) {
  const showLbp = closes.some((c) => c.expectedLbp || c.countedLbp);
  return sortTable(ctx, id, [
    { key: "closedAt", label: t("closed"), value: (r) => r.closedAt, render: (r) => h("span", { class: "num" }, f.when(r.closedAt)) },
    { key: "cashier", label: t("cashier"), value: (r) => r.cashier, render: (r) => [h("strong", null, bdi(r.cashier)), r.till ? h("span", { class: "sub" }, bdi(r.till)) : null] },
    { key: "expected", label: t("expected"), end: true, value: (r) => r.expected, render: (r) => h("span", { class: "num" }, f.money(r.expected)) },
    { key: "diff", label: t("overShort"), end: true, value: (r) => (r.counted === undefined ? null : r.counted - r.expected),
      render: (r) => (r.counted === undefined || r.counted === null ? h("span", { class: "tag plain" }, t("notCounted")) : signedMoney(r.counted - r.expected)) },
    showLbp ? { key: "lbp", label: `${t("overShort")} LBP`, end: true, value: (r) => (r.countedLbp === undefined ? null : r.countedLbp - r.expectedLbp),
      render: (r) => (r.countedLbp === undefined || r.countedLbp === null ? "–" : signedMoney(r.countedLbp - r.expectedLbp, f.lbp)) } : null,
  ].filter(Boolean), closes, "closedAt");
}

// ---- Sales --------------------------------------------------------------------------------------------------------

const sales = {
  keys: () => ["live"],
  render(ctx) {
    const all = ctx.state.docs.live?.recentSales ?? [];
    const ui = (ctx.state.ui.sales ??= { q: "", filter: "all" });
    const list = h("div");
    const draw = () => {
      const q = ui.q.trim().toLowerCase();
      const shown = all.filter((s) =>
        (ui.filter === "all" || (ui.filter === "voided" ? s.status === "voided" : ui.filter === "completed" ? s.status !== "voided" : !!s.channel)) &&
        (!q || [s.receipt, s.cashier, s.channel, ...(s.lines ?? []).map((l) => `${l.product} ${l.variant}`)].some((x) => (x ?? "").toLowerCase().includes(q))));
      list.replaceChildren(shown.length ? salesFeed(ctx, shown) : h("div", { class: "empty" }, t("noMatches")));
    };
    draw();
    const search = h("input", { class: "input", type: "search", placeholder: t("salesSearch"), value: ui.q, oninput: (e) => { ui.q = e.target.value; draw(); } });
    const toolbar = h("div", { class: "toolbar" },
      h("div", { class: "search" }, icon("search", 17), search),
      h("div", { class: "segmented" }, ["all", "completed", "voided", "online"].map((k) =>
        h("button", { type: "button", class: ui.filter === k ? "active" : null, onclick: () => { ui.filter = k; ctx.rerender(); } }, t(`filter_${k}`)))));
    const total = all.filter((s) => s.status !== "voided").reduce((sum, s) => sum + s.total, 0);
    return {
      subtitle: t("lastN", all.length),
      nodes: [toolbar,
        h("div", { class: "kpis k4" },
          kpi({ label: t("transactions"), value: all.filter((s) => s.status !== "voided").length, format: f.number, iconName: "sales" }),
          kpi({ label: t("totalSales"), value: total, iconName: "cart" }),
          kpi({ label: t("voided"), value: all.filter((s) => s.status === "voided").length, format: f.number, iconName: "alert" }),
          kpi({ label: t("filter_online"), value: all.filter((s) => s.channel).length, format: f.number, iconName: "globe" })),
        card({ title: t("recentSales"), sub: t("lastN", all.length), actions: [csvButton("sales", [t("receipt"), t("time"), t("cashier"), t("items"), t("total"), "status"], () => all.map((s) => [s.receipt, s.time, s.cashier, s.items, s.total, s.status]))] }, list)],
    };
  },
};

// ---- Drawers ------------------------------------------------------------------------------------------------------

const drawers = {
  keys: () => ["live"],
  render(ctx) {
    const live = ctx.state.docs.live;
    const open = live?.openDrawers ?? [];
    const closes = live?.recentCloses ?? [];
    const nodes = [];
    nodes.push(open.length ? h("div", { class: "grid g-3" }, open.map((d) => h("section", { class: "card till" },
      h("div", { class: "till-head" }, h("span", { class: "avatar" }, initials(d.cashier)),
        h("div", { style: "min-width:0" }, h("strong", null, bdi(d.cashier)), h("span", null, d.till ? bdi(d.till) : "")),
        h("span", { class: "spacer" }), h("span", { class: "tag good" }, t("openFor", f.duration(d.openedAt)))),
      h("div", { class: "till-stats" },
        stat(t("drawerSales"), `${f.number(d.sales)}× · ${f.money(d.totalSales ?? 0)}`),
        stat(t("since"), f.when(d.openedAt)),
        stat(t("expectedCash"), f.money(d.expectedCash)),
        stat(t("expectedLbp"), f.lbp(d.expectedCashLbp)))))) : ctx.emptyState("drawers", t("noOpenDrawers")));

    if (closes.length) {
      const counted = closes.filter((c) => c.counted !== undefined && c.counted !== null).slice(0, 20).reverse();
      nodes.push(card({ title: t("overShortHistory"), sub: t("overShortHint", counted.length) },
        divergingBars({ items: counted.map((c) => ({ label: `${f.when(c.closedAt)} · ${c.cashier}`, value: c.counted - c.expected })) })));
      nodes.push(card({ title: t("recentCloses"), actions: [csvButton("drawer-closes", [t("closed"), t("cashier"), t("till"), t("expected"), t("counted")], () => closes.map((c) => [c.closedAt, c.cashier, c.till, c.expected, c.counted]))] },
        closesTable(ctx, closes, "closesD")));
    }
    return { nodes };
  },
};

// ---- Stock --------------------------------------------------------------------------------------------------------

function daysLeftTag(r) {
  if (r.stock <= 0) return h("span", { class: "tag bad" }, t("runsOut"));
  if (r.daysLeft === undefined || r.daysLeft === null) return h("span", { class: "tag plain" }, t("noPace"));
  return h("span", { class: `tag ${r.daysLeft <= 7 ? "bad" : r.daysLeft <= 14 ? "warn" : "good"}` }, t("daysLeftN", f.number(r.daysLeft)));
}

/** Lower case, Arabic letter variants and marks folded, Arabic digits as Western: what a search compares. */
function fold(text) {
  return String(text ?? "").toLowerCase()
    .replace(/[\u064B-\u065F\u0670]/g, "")
    .replace(/[أإآ]/g, "ا").replace(/ة/g, "ه").replace(/ى/g, "ي")
    .replace(/[\u0660-\u0669]/g, (d) => String(d.charCodeAt(0) - 0x0660));
}

/** The item list from its parts, with what a search needs, worked out once per list the shop sends. */
let catalogCache = { key: null, products: [] };

function catalog(ctx) {
  const first = ctx.state.docs["catalog:0"];
  if (!first) return null;
  const parts = Array.from({ length: first.parts ?? 1 }, (_, i) => ctx.state.docs[`catalog:${i}`]);
  if (parts.some((p) => !p)) return null;
  const key = parts.map((p) => p.products?.length ?? 0).join(",") + ":" + ctx.state.version;
  if (catalogCache.key === key && catalogCache.parts === parts[0]) return catalogCache.products;
  const products = parts.flatMap((p) => p.products ?? []).map((p) => {
    const variants = (p.v ?? []).map(([size, color, sku, barcode, stock, reorder, price, sold30]) =>
      ({ size, color, sku, barcode, stock, reorder, price: price ?? p.p, sold30: sold30 ?? 0 }));
    const total = variants.reduce((sum, v) => sum + Math.max(0, v.stock), 0);
    return {
      name: p.n, category: p.c, brand: p.b, style: p.s, price: p.p, variants, total,
      text: fold([p.n, p.c, p.b, p.s, ...variants.flatMap((v) => [v.size, v.color, v.sku, v.barcode])].filter(Boolean).join(" ")),
    };
  });
  catalogCache = { key, parts: parts[0], products };
  return products;
}

const variantState = (v) => (v.stock <= 0 ? "out" : v.stock <= v.reorder ? "low" : "in");

function productCard(ctx, p, codeMatch) {
  const store = ctx.state.me.store ?? {};
  const prices = [...new Set(p.variants.map((v) => v.price))];
  const priceText = prices.length > 1 ? `${f.money(Math.min(...prices))} – ${f.money(Math.max(...prices))}` : f.money(prices[0] ?? p.price);
  const lbpText = store.lbp && store.lbpRate ? `≈ ${f.lbp(Math.round(((prices[0] ?? p.price) * store.lbpRate) / 1000) * 1000)}` : null;
  const status = p.total <= 0 ? "out" : p.variants.some((v) => variantState(v) !== "in") ? "low" : "in";
  return h("article", { class: "item-card" },
    h("div", { class: "item-head" },
      h("div", { class: "item-title" },
        h("strong", null, bdi(p.name)),
        h("span", { class: "sub" }, [p.category, p.brand, p.style].filter(Boolean).map((x, i) => [i ? " · " : "", bdi(x)]))),
      h("div", { class: "item-price" }, h("strong", { class: "num" }, priceText), lbpText ? h("span", { class: "sub num" }, lbpText) : null)),
    h("div", { class: "item-total" },
      h("span", { class: `tag ${status === "out" ? "bad" : status === "low" ? "warn" : "good"}` },
        status === "out" ? t("outOfStock") : t("inStockN", f.number(p.total))),
      h("span", { class: "muted" }, t("variantsN", f.number(p.variants.length)))),
    h("div", { class: "chips-grid" }, p.variants.map((v) => {
      const state = variantState(v);
      const label = [v.size, v.color].filter(Boolean).join(" · ") || "–";
      const match = codeMatch && (fold(v.sku) === codeMatch || fold(v.barcode) === codeMatch);
      return h("div", {
        class: `stock-chip ${state} ${match ? "match" : ""}`,
        title: [`${t("skuLabel")}: ${v.sku}`, v.barcode ? `${t("barcodeLabel")}: ${v.barcode}` : null, t("soldN", f.number(v.sold30)), f.money(v.price)].filter(Boolean).join("\n"),
      },
        h("span", { class: "chip-label" }, bdi(label)),
        h("strong", { class: "num" }, state === "out" ? t("outShort") : f.number(v.stock)),
        h("span", { class: "chip-sub" }, v.sold30 ? t("soldShort", f.number(v.sold30)) : bdi(v.sku)));
    })));
}

function stockItems(ctx) {
  const products = catalog(ctx);
  if (!products) return [ctx.emptyState("search", t("catalogMissing"), t("catalogMissingHint"))];
  const ui = (ctx.state.ui.items ??= { q: "", filter: "all", shown: 30 });
  const list = h("div", { class: "items" });
  const summary = h("div", { class: "range-label" });
  const draw = () => {
    const words = fold(ui.q).split(/\s+/).filter(Boolean);
    const code = words.length === 1 ? words[0] : null;
    const matches = products.filter((p) => words.every((w) => p.text.includes(w)) &&
      (ui.filter === "all" || p.variants.some((v) => variantState(v) === ui.filter || (ui.filter === "in" && v.stock > 0))));
    // An exact SKU or barcode first.
    if (code) matches.sort((a, b) => Number(b.variants.some((v) => fold(v.sku) === code || fold(v.barcode) === code)) - Number(a.variants.some((v) => fold(v.sku) === code || fold(v.barcode) === code)));
    summary.textContent = t("itemsFound", f.number(matches.length), f.number(products.length));
    const shown = matches.slice(0, ui.shown);
    // replaceChildren would write a null as the text "null": only real elements go in.
    list.replaceChildren(
      ...(shown.length ? shown.map((p) => productCard(ctx, p, code)) : [h("div", { class: "card empty" }, t("noItems"))]),
      ...(matches.length > shown.length
        ? [h("button", { class: "tool more", type: "button", onclick: () => { ui.shown += 30; draw(); } }, t("showMore", f.number(matches.length - shown.length)))]
        : []));
  };
  draw();
  const search = h("input", { class: "input", id: "stock-search", type: "search", inputmode: "search", autocomplete: "off", placeholder: t("stockSearch"), value: ui.q,
    oninput: (e) => { ui.q = e.target.value; ui.shown = 30; draw(); } });
  const variants = products.reduce((n, p) => n + p.variants.length, 0);
  const out = products.reduce((n, p) => n + p.variants.filter((v) => v.stock <= 0).length, 0);
  const low = products.reduce((n, p) => n + p.variants.filter((v) => variantState(v) === "low").length, 0);
  return [
    h("div", { class: "toolbar" },
      h("div", { class: "search" }, icon("search", 17), search),
      h("div", { class: "segmented" }, ["all", "in", "low", "out"].map((k) =>
        h("button", { type: "button", class: ui.filter === k ? "active" : null, onclick: () => { ui.filter = k; ui.shown = 30; ctx.rerender(); } }, t(`stockFilter_${k}`))))),
    h("div", { class: "kpis k4" },
      kpi({ label: t("productsLabel"), value: products.length, format: f.number, iconName: "tag" }),
      kpi({ label: t("skus"), value: variants, format: f.number, iconName: "overview" }),
      kpi({ label: t("lowStock"), value: low, format: f.number, iconName: "alert" }),
      kpi({ label: t("outOfStock"), value: out, format: f.number, iconName: "stock" })),
    summary,
    list,
  ];
}

const stock = {
  keys: (ctx) => {
    const parts = ctx.state.docs["catalog:0"]?.parts ?? 1;
    return ["stock", ...Array.from({ length: Math.min(parts, 10) }, (_, i) => `catalog:${i}`)];
  },
  render(ctx) {
    const ui = (ctx.state.ui.stockView ??= { view: "items" });
    const tabs = h("div", { class: "segmented" }, ["items", "summary"].map((k) =>
      h("button", { type: "button", class: ui.view === k ? "active" : null, onclick: () => { ui.view = k; ctx.rerender(); } }, t(`stockView_${k}`))));
    return { nodes: [tabs, ...(ui.view === "items" ? stockItems(ctx) : stockSummary(ctx))] };
  },
};

function stockSummary(ctx) {
    const s = ctx.state.docs.stock;
    if (!s) return [ctx.emptyState("stock", t("noData"), t("noDataHint"))];
    const nodes = [h("div", { class: "kpis" },
      kpi({ label: t("stockValue"), value: s.costValue, format: f.money0, hero: true, iconName: "stock" }),
      kpi({ label: t("retailValue"), value: s.retailValue, format: f.money0, iconName: "tag" }),
      kpi({ label: t("potentialProfit"), value: s.retailValue - s.costValue, format: f.money0, iconName: "trend", foot: h("span", null, t("marginOf", f.percent(s.retailValue ? ((s.retailValue - s.costValue) / s.retailValue) * 100 : 0))) }),
      kpi({ label: t("units"), value: s.units, format: f.number, iconName: "cart" }),
      kpi({ label: t("skus"), value: s.skus, format: f.number, iconName: "overview" }),
      kpi({ label: t("outOfStock"), value: s.outOfStockCount, format: f.number, iconName: "alert", foot: h("span", null, `${t("lowStock")}: ${f.number(s.lowStockCount)}`) }))];

    nodes.push(h("div", { class: "grid g-main" },
      card({ title: t("lowStock"), sub: t("lowStockHint"), actions: [csvButton("low-stock", [t("product"), "variant", "sku", t("inStock"), t("reorderAt"), t("sold30"), t("daysLeft")], () => (s.lowStock ?? []).map((r) => [r.product, r.variant, r.sku, r.stock, r.reorder, r.sold30, r.daysLeft]))] },
        s.lowStock?.length ? sortTable(ctx, "low", [
          { key: "product", label: t("product"), value: (r) => r.product, render: (r) => [h("strong", null, bdi(r.product)), r.variant ? h("span", { class: "sub" }, bdi(r.variant)) : null] },
          { key: "stock", label: t("inStock"), end: true, value: (r) => r.stock, render: (r) => h("span", { class: `num ${r.stock <= 0 ? "bad" : ""}`, style: "font-weight:600" }, f.number(r.stock)) },
          { key: "reorder", label: t("reorderAt"), end: true, value: (r) => r.reorder, render: (r) => h("span", { class: "num muted" }, f.number(r.reorder)) },
          { key: "sold30", label: t("sold30"), end: true, value: (r) => r.sold30 ?? 0, render: (r) => h("span", { class: "num" }, f.number(r.sold30 ?? 0)) },
          { key: "days", label: t("daysLeft"), end: true, value: (r) => (r.stock <= 0 ? -1 : r.daysLeft ?? 99999), render: daysLeftTag },
        ], s.lowStock, "days", false) : h("div", { class: "empty" }, t("nothing"))), // most urgent first
      card({ title: t("byCategoryStock") }, nameRows(s.byCategory, { meta: pcsMeta, amount: (r) => f.money0(r.amount) }))));

    nodes.push(card({ title: t("slowMovers"), sub: t("slowMoversHint", s.slowSinceDays ?? 60), actions: [csvButton("slow-movers", [t("product"), "variant", t("inStock"), t("value"), t("lastSold")], () => (s.slowMovers ?? []).map((r) => [r.product, r.variant, r.stock, r.value, r.lastSold ?? ""]))] },
      s.slowMovers?.length ? sortTable(ctx, "slow", [
        { key: "product", label: t("product"), value: (r) => r.product, render: (r) => [h("strong", null, bdi(r.product)), r.variant ? h("span", { class: "sub" }, bdi(r.variant)) : null] },
        { key: "stock", label: t("inStock"), end: true, value: (r) => r.stock, render: (r) => h("span", { class: "num" }, f.number(r.stock)) },
        { key: "value", label: t("value"), end: true, value: (r) => r.value, render: (r) => h("span", { class: "num", style: "font-weight:600" }, f.money(r.value)) },
        { key: "last", label: t("lastSold"), end: true, value: (r) => (r.lastSold ? -f.daysAgo(r.lastSold) : -Infinity),
          render: (r) => (r.lastSold ? h("span", { class: "muted" }, t("daysAgoN", f.number(f.daysAgo(r.lastSold)))) : h("span", { class: "tag plain" }, t("never"))) },
      ], s.slowMovers, "value") : h("div", { class: "empty" }, t("nothing"))));
    return nodes;
}

export const PAGES = { overview, analytics, sales, drawers, stock };
