// Made-up reports in the format the POS uploads, for trying the site locally (npm run seed), for the preview and for the
// tests. The real ones are built by ClothingStore.Data's OnlineReportsBuilder. The numbers follow a seeded random walk,
// so they look like a real shop's week and are the same every run.

const pad = (n) => String(n).padStart(2, "0");
const isoDate = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const isoTime = (d) => `${isoDate(d)}T${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
const round2 = (n) => Math.round(n * 100) / 100;

function random(seed) {
  let s = seed >>> 0;
  return () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
}

const CATEGORIES = [["Jeans", 0.27, 0.48], ["Dresses", 0.22, 0.55], ["T-shirts", 0.14, 0.6], ["Shirts", 0.12, 0.52], ["Abayas", 0.1, 0.5], ["Jackets", 0.08, 0.45], ["Accessories", 0.07, 0.65]];
const PRODUCTS = [
  ["Slim fit jeans", "Jeans", 45], ["Linen summer dress", "Dresses", 60], ["Essential cotton tee", "T-shirts", 12], ["Oxford shirt", "Shirts", 35],
  ["Classic black abaya", "Abayas", 90], ["Wide-leg jeans", "Jeans", 49], ["Floral midi dress", "Dresses", 55], ["Denim jacket", "Jackets", 75],
  ["Silk scarf", "Accessories", 18], ["Graphic tee", "T-shirts", 15], ["Leather belt", "Accessories", 22], ["Mom jeans", "Jeans", 42],
];
const HOURS = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21];
const HOUR_WEIGHT = [0.4, 0.6, 0.8, 0.7, 0.55, 0.7, 0.95, 1.1, 1.3, 1.2, 0.9, 0.5];
const WEEKDAY_WEIGHT = [0.8, 0.85, 0.9, 0.95, 1.1, 1.35, 0.7]; // Monday first

function dayTotal(date, rnd) {
  const wd = (date.getDay() + 6) % 7;
  return 880 * WEEKDAY_WEIGHT[wd] * (0.85 + rnd() * 0.3);
}

function rows(list, total) {
  const sum = list.reduce((s, r) => s + r.amount, 0) || 1;
  return list.map((r) => ({ ...r, amount: round2((r.amount / sum) * total), profit: r.profit !== undefined ? round2((r.profit / sum) * total) : undefined, share: Math.round((r.amount / sum) * 1000) / 10 }));
}

function period(from, to, now, scale = 1, seed = 1) {
  const rnd = random(seed);
  const days = [];
  const last = to < now ? to : now;
  for (let d = new Date(from); d <= last; d.setDate(d.getDate() + 1)) {
    const isToday = isoDate(d) === isoDate(now);
    const part = isToday ? Math.min(1, Math.max(0.15, (now.getHours() - 9) / 12)) : 1;
    const total = round2(dayTotal(d, rnd) * part * scale);
    const count = Math.max(1, Math.round(total / 48));
    days.push({ date: isoDate(d), count, total, profit: round2(total * (0.44 + rnd() * 0.06)) });
  }
  const totalSales = round2(days.reduce((s, d) => s + d.total, 0));
  const transactions = days.reduce((s, d) => s + d.count, 0);
  const profit = round2(days.reduce((s, d) => s + d.profit, 0));
  const refunds = round2(totalSales * 0.024);
  const netRevenue = round2(totalSales - refunds);
  const single = days.length <= 1;
  const byHour = HOURS.filter((h) => !single || h <= Math.max(10, now.getHours())).map((hour, i) => {
    const total = round2((totalSales / 9.7) * HOUR_WEIGHT[i] * (0.8 + rnd() * 0.4));
    return { hour, count: Math.max(1, Math.round(total / 48)), total };
  });
  const heatmap = [];
  if (!single) {
    for (let wd = 0; wd < 7; wd++) {
      HOURS.forEach((hour, i) => {
        const total = round2((totalSales / 70) * WEEKDAY_WEIGHT[wd] * HOUR_WEIGHT[i] * (0.6 + rnd() * 0.8));
        heatmap.push({ weekday: wd, hour, count: Math.max(1, Math.round(total / 48)), total });
      });
    }
  }
  return {
    from: isoDate(from),
    to: isoDate(new Date(to.getFullYear(), to.getMonth(), to.getDate())),
    totals: {
      totalSales, grossSales: round2(totalSales * 1.06), discounts: round2(totalSales * 0.06), tax: 0, netRevenue,
      costOfGoods: round2(netRevenue - profit), grossProfit: profit, marginPercent: netRevenue ? Math.round((profit / netRevenue) * 1000) / 10 : 0,
      transactions, itemsSold: Math.round(transactions * 1.9), averageBasket: transactions ? round2(totalSales / transactions) : 0,
      refunds, returnsCount: Math.round(transactions * 0.025), voidedCount: Math.round(transactions * 0.01), deliveryFees: round2(transactions * 0.4),
    },
    change: { sales: 8.6, transactions: 4.2, profit: 11.3 },
    byDay: days,
    byHour,
    previousByDay: single ? [] : days.map((d, i) => {
      const span = Math.round((to - from) / 86400000) + 1;
      const date = new Date(from.getFullYear(), from.getMonth(), from.getDate() - span + i);
      return { ...d, date: isoDate(date), total: round2(d.total * (0.82 + rnd() * 0.25)), profit: round2(d.profit * 0.9) };
    }),
    previousByHour: single ? byHour.map((h) => ({ ...h, total: round2(h.total * (0.7 + rnd() * 0.5)) })) : [],
    heatmap,
    customers: { sales: Math.round(transactions * 0.42), customers: Math.round(transactions * 0.36), returning: Math.round(transactions * 0.21) },
    payments: rows([{ name: "Cash", amount: 55, count: Math.round(transactions * 0.6) }, { name: "Card", amount: 31, count: Math.round(transactions * 0.3) }, { name: "Whish Money", amount: 14, count: Math.round(transactions * 0.1) }], totalSales),
    channels: rows([{ name: "In store", amount: 82, count: Math.round(transactions * 0.85) }, { name: "WhatsApp", amount: 12, count: Math.round(transactions * 0.1) }, { name: "Instagram", amount: 6, count: Math.round(transactions * 0.05) }], totalSales),
    categories: rows(CATEGORIES.map(([name, w, m]) => ({ name, qty: Math.round(transactions * 1.9 * w), amount: w, profit: w * m })), netRevenue),
    products: PRODUCTS.map(([name, , price], i) => {
      const qty = Math.max(1, Math.round((transactions * 1.9 * (0.16 - i * 0.011)) * (0.8 + rnd() * 0.4)));
      const amount = round2(qty * price * 0.94);
      return { name, qty, amount, count: qty, profit: round2(amount * 0.48), share: 0, previousQty: Math.round(qty * (0.7 + rnd() * 0.6)), previousAmount: round2(amount * (0.7 + rnd() * 0.6)) };
    }).sort((a, b) => b.qty - a.qty),
    cashiers: rows([{ name: "Maya Haddad", amount: 58, count: Math.round(transactions * 0.58), qty: 0 }, { name: "Karim Saleh", amount: 42, count: Math.round(transactions * 0.42), qty: 0 }], totalSales),
    sizes: rows([{ name: "M", qty: 40, amount: 34 }, { name: "L", qty: 30, amount: 26 }, { name: "S", qty: 22, amount: 19 }, { name: "XL", qty: 12, amount: 11 }, { name: "One size", qty: 10, amount: 10 }], netRevenue),
    returnsByReason: [{ name: "Wrong size", qty: 0, amount: round2(refunds * 0.6), count: 3, profit: 0, share: 60 }, { name: "Changed mind", qty: 0, amount: round2(refunds * 0.4), count: 2, profit: 0, share: 40 }],
    topReturned: [{ name: "Slim fit jeans", qty: 2, amount: 90, count: 2, profit: 0, share: 0 }, { name: "Floral midi dress", qty: 1, amount: 55, count: 1, profit: 0, share: 0 }],
    refundsByMethod: [{ name: "Cash", qty: 0, amount: round2(refunds * 0.7), count: 3, profit: 0, share: 70 }, { name: "Store credit", qty: 0, amount: round2(refunds * 0.3), count: 2, profit: 0, share: 30 }],
    settlements: single ? [] : [{ date: isoTime(new Date(to.getTime() - 86400000)), courier: "Toters", method: "Cash", orders: 6, expected: 214, received: 209 }, { date: isoTime(new Date(to.getTime() - 3 * 86400000)), courier: "Wakilni", method: "Cash", orders: 4, expected: 158, received: 158 }],
    drawerCloses: single ? [] : [{ closedAt: isoTime(new Date(to.getTime() - 86400000 + 21 * 3600000)), cashier: "Maya Haddad", expected: 1240, counted: 1236, expectedLbp: 4500000, countedLbp: 4500000 }],
  };
}

/** About 120 products in their sizes and colours, as the POS's item list (CatalogDoc). */
export function sampleCatalog() {
  const rnd = random(7);
  const styles = [
    ["Slim fit jeans", "Jeans", "Levi's", 45, ["28", "30", "32", "34", "36"], ["Blue", "Black"]],
    ["Wide-leg jeans", "Jeans", "Zara", 49, ["S", "M", "L"], ["Light blue"]],
    ["Mom jeans", "Jeans", "Mango", 42, ["S", "M", "L", "XL"], ["Blue"]],
    ["Linen summer dress", "Dresses", "Mango", 60, ["S", "M", "L"], ["Sand", "White"]],
    ["Floral midi dress", "Dresses", "Zara", 55, ["XS", "S", "M", "L"], ["Rose"]],
    ["Satin evening dress", "Dresses", "", 120, ["S", "M", "L"], ["Emerald", "Black"]],
    ["Essential cotton tee", "T-shirts", "Basics", 12, ["S", "M", "L", "XL"], ["White", "Black", "Grey"]],
    ["Graphic tee", "T-shirts", "", 15, ["S", "M", "L"], ["Black"]],
    ["Oxford shirt", "Shirts", "Massimo Dutti", 35, ["S", "M", "L", "XL"], ["Sky", "White"]],
    ["Classic black abaya", "Abayas", "", 90, ["52", "54", "56", "58"], ["Black"]],
    ["Embroidered abaya", "Abayas", "", 140, ["54", "56"], ["Navy"]],
    ["Denim jacket", "Jackets", "Levi's", 75, ["S", "M", "L"], ["Blue"]],
    ["Silk scarf", "Accessories", "", 18, [""], ["Rose", "Olive", "Ivory"]],
    ["Leather belt", "Accessories", "", 22, ["90", "100", "110"], ["Brown", "Black"]],
    ["جلابية مطرّزة", "Abayas", "", 65, ["M", "L"], ["أسود"]],
  ];
  const products = [];
  for (let i = 0; i < 120; i++) {
    const [name, category, brand, price, sizes, colors] = styles[i % styles.length];
    const round = Math.floor(i / styles.length);
    const v = [];
    let n = 0;
    for (const color of colors) for (const size of sizes) {
      const stock = Math.max(0, Math.round(rnd() * 9) - (rnd() < 0.18 ? 9 : 0));
      v.push([size, color, `${name.slice(0, 3).toUpperCase()}${round}-${size || "OS"}-${color.slice(0, 2).toUpperCase()}`,
        `62${String(1000000000 + i * 97 + n++).slice(-10)}`, stock, 2, size === "XL" || size === "58" ? price + 5 : null, Math.round(rnd() * 12)]);
    }
    products.push({ n: round ? `${name} ${["II", "III", "IV", "V", "VI", "VII", "VIII", "IX"][round - 1]}` : name, c: category, b: brand || undefined, s: `ST-${100 + i}`, p: price, v });
  }
  products.sort((a, b) => a.n.localeCompare(b.n));
  return { part: 0, parts: 1, products };
}

export function sampleDocs(now = new Date()) {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const days = (n) => new Date(today.getFullYear(), today.getMonth(), today.getDate() + n);
  const monday = days(-((7 + today.getDay() - 1) % 7));
  const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
  const lastMonthStart = new Date(today.getFullYear(), today.getMonth() - 1, 1);
  const lastMonthEnd = new Date(today.getFullYear(), today.getMonth(), 0);
  const rnd = random(42);
  const names = PRODUCTS.map((p) => p[0]);

  const trend14 = [];
  for (let i = 13; i >= 0; i--) {
    const d = days(-i);
    const total = round2(dayTotal(d, rnd) * (i === 0 ? Math.min(1, Math.max(0.15, (now.getHours() - 9) / 12)) : 1));
    trend14.push({ date: isoDate(d), count: Math.round(total / 48), total, profit: round2(total * 0.46) });
  }

  const docs = {
    store: { name: "Rami Boutique", currency: "$", lbp: true, lbpRate: 89500, language: "en", monthlyTarget: 30000 },
    live: {
      openDrawers: [
        { till: "Front till", cashier: "Maya Haddad", openedAt: isoTime(new Date(today.getTime() + 9.5 * 3600000)), sales: 23, expectedCash: 412.5, expectedCashLbp: 2300000, totalSales: 1084 },
        { till: "Back till", cashier: "Karim Saleh", openedAt: isoTime(new Date(today.getTime() + 13 * 3600000)), sales: 9, expectedCash: 186, expectedCashLbp: 900000, totalSales: 438 },
      ],
      recentSales: Array.from({ length: 50 }, (_, i) => {
        const lines = Array.from({ length: 1 + (i % 3) }, (_, j) => {
          const p = PRODUCTS[(i * 3 + j) % PRODUCTS.length];
          return { product: p[0], variant: ["S / Black", "M / Blue", "L / White", "One size"][(i + j) % 4], qty: 1 + ((i + j) % 2), amount: p[2] * (1 + ((i + j) % 2)) };
        });
        return {
          receipt: `R-${String(10450 - i).padStart(6, "0")}`, time: isoTime(new Date(now.getTime() - i * 23 * 60000)),
          cashier: i % 3 ? "Maya Haddad" : "Karim Saleh", total: lines.reduce((s, l) => s + l.amount, 0), items: lines.reduce((s, l) => s + l.qty, 0),
          status: i === 4 ? "voided" : "completed", channel: i % 7 === 0 ? "WhatsApp" : i % 11 === 0 ? "Instagram" : undefined, lines,
        };
      }),
      recentCloses: Array.from({ length: 14 }, (_, i) => {
        const expected = round2(900 + rnd() * 600);
        const diff = [0, -5, 0, 2, -12, 0, 0, 5, -3, 0, 0, -1, 10, 0][i];
        return { closedAt: isoTime(new Date(today.getTime() - (i + 1) * 86400000 + 21 * 3600000)), cashier: i % 2 ? "Karim Saleh" : "Maya Haddad", till: i % 2 ? "Back till" : "Front till",
          expected, counted: round2(expected + diff), expectedLbp: 4500000, countedLbp: 4500000 + (i === 4 ? -100000 : 0) };
      }),
      lowStockCount: 9,
      outOfStockCount: 3,
      trend14,
    },
    stock: {
      skus: 842, units: 3126, costValue: 41250.5, retailValue: 88900, lowStockCount: 9, outOfStockCount: 3, slowSinceDays: 60,
      byCategory: rows(CATEGORIES.map(([name, w]) => ({ name, qty: Math.round(3126 * w), amount: w })), 41250.5),
      lowStock: [
        { product: "Slim fit jeans", variant: "32 / Blue", sku: "SFJ-32-BL", stock: 2, reorder: 4, sold30: 18, daysLeft: 3 },
        { product: "Essential cotton tee", variant: "M / White", sku: "ECT-M-WH", stock: 0, reorder: 6, sold30: 25, daysLeft: 0 },
        { product: "Linen summer dress", variant: "S / Sand", sku: "LSD-S-SA", stock: 3, reorder: 3, sold30: 9, daysLeft: 10 },
        { product: "Classic black abaya", variant: "54", sku: "CBA-54", stock: 1, reorder: 2, sold30: 2, daysLeft: 15 },
        { product: "Silk scarf", variant: "Rose", sku: "SS-RO", stock: 2, reorder: 5, sold30: 0 },
        { product: "Oxford shirt", variant: "L / Sky", sku: "OS-L-SK", stock: 2, reorder: 3, sold30: 7, daysLeft: 8 },
      ],
      slowMovers: [
        { product: "Wool winter coat", variant: "L / Grey", stock: 6, value: 330, lastSold: isoTime(days(-121)) },
        { product: "Puffer vest", variant: "M / Olive", stock: 8, value: 248, lastSold: isoTime(days(-94)) },
        { product: "Knit beanie", variant: "One size", stock: 14, value: 84, lastSold: null },
        { product: "Corduroy trousers", variant: "34 / Brown", stock: 5, value: 150, lastSold: isoTime(days(-70)) },
      ],
    },
    "period:today": period(today, today, now, 1, 11),
    "period:yesterday": period(days(-1), days(-1), now, 1, 12),
    "period:week": period(monday, today, now, 1, 13),
    "period:lastweek": period(new Date(monday.getTime() - 7 * 86400000), new Date(monday.getTime() - 86400000), now, 1, 14),
    "period:month": period(monthStart, today, now, 1, 15),
    "period:lastmonth": period(lastMonthStart, lastMonthEnd, now, 1, 16),
    "period:year": period(new Date(today.getFullYear(), 0, 1), today, now, 1, 17),
  };
  docs["catalog:0"] = sampleCatalog();
  for (let i = 1; i <= 20; i++) docs[`day:${isoDate(days(-i))}`] = period(days(-i), days(-i), now, 1, 100 + i);
  for (let i = 1; i <= 3; i++) {
    const start = new Date(today.getFullYear(), today.getMonth() - i, 1);
    docs[`month:${isoDate(start).slice(0, 7)}`] = period(start, new Date(today.getFullYear(), today.getMonth() - i + 1, 0), now, 1, 200 + i);
  }
  void names;
  return docs;
}
