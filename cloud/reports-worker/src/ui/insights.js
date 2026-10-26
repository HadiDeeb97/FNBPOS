// Plain-language highlights, worked out from the reports the shop sent. Each is [tone, icon, text]; tone is good, bad,
// warn or info. Only what the numbers really say: nothing is shown when there isn't enough to go on.
import { t } from "./i18n.js";
import { money0, percent, hourLabel, number } from "./format.js";

/** Where the month is heading at its pace so far (null before there is anything to go on). */
export function monthForecast(monthDoc, now = new Date()) {
  if (!monthDoc?.totals) return null;
  const daysInMonth = new Date(now.getFullYear(), now.getMonth() + 1, 0).getDate();
  // Today counts as the part of it that has gone (shops are open roughly 9 to 21).
  const hour = now.getHours() + now.getMinutes() / 60;
  const todayPart = Math.min(1, Math.max(0.1, (hour - 9) / 12));
  const elapsed = now.getDate() - 1 + todayPart;
  const sales = Number(monthDoc.totals.totalSales ?? 0);
  if (sales <= 0 || elapsed <= 0) return null;
  return { forecast: (sales / elapsed) * daysInMonth, daysLeft: daysInMonth - now.getDate(), sales };
}

export function insights({ today, month, live, stock, target }) {
  const list = [];
  const totals = today?.totals;

  if (totals && totals.transactions === 0) list.push(["info", "clock", t("insQuiet")]);

  const change = today?.change?.sales;
  if (change !== null && change !== undefined && totals?.transactions > 0) {
    if (change >= 3) list.push(["good", "trend", t("insSalesUp", percent(change))]);
    else if (change <= -3) list.push(["bad", "trend", t("insSalesDown", percent(Math.abs(change)))]);
  }

  const hours = today?.byHour ?? [];
  if (hours.length >= 2) {
    const best = hours.reduce((a, b) => (b.total > a.total ? b : a));
    list.push(["info", "clock", t("insBestHour", hourLabel(best.hour), money0(best.total))]);
  }

  const forecast = monthForecast(month);
  if (forecast) {
    if (target > 0 && forecast.sales >= target) list.push(["good", "target", t("insTargetReached", money0(forecast.sales))]);
    else if (target > 0) {
      const share = (forecast.forecast / target) * 100;
      list.push([share >= 100 ? "good" : share >= 85 ? "info" : "warn", "target", t("insForecastTarget", money0(forecast.forecast), percent(share))]);
    } else list.push(["info", "target", t("insForecast", money0(forecast.forecast))]);
  }

  const category = (month?.categories ?? today?.categories ?? [])[0];
  if (category && category.share >= 20) list.push(["info", "tag", t("insTopCategory", category.name, percent(category.share))]);

  const product = (today?.products ?? [])[0];
  if (product && product.qty >= 2) list.push(["info", "spark", t("insTopProduct", product.name, number(product.qty))]);

  const urgent = (stock?.lowStock ?? []).filter((r) => r.daysLeft !== undefined && r.daysLeft !== null && r.daysLeft <= 7 && r.sold30 > 0).length;
  if (urgent) list.push(["warn", "alert", t("insRunningOut", number(urgent))]);

  const mt = month?.totals;
  if (mt?.totalSales > 0) {
    const returns = (mt.refunds / mt.totalSales) * 100;
    if (returns >= 6) list.push(["warn", "alert", t("insReturnRate", percent(returns))]);
    if (mt.marginPercent >= 45) list.push(["good", "trend", t("insMarginUp", percent(mt.marginPercent))]);
    else if (mt.marginPercent > 0 && mt.marginPercent < 25) list.push(["warn", "alert", t("insMarginLow", percent(mt.marginPercent))]);
  }

  const customers = month?.customers;
  if (customers?.customers >= 5) list.push(["info", "user", t("insRepeat", percent((customers.returning / customers.customers) * 100))]);

  return list.slice(0, 6);
}
