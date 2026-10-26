# Online reports website

The shop's reports on any phone or computer, at no cost. The POS sends its reports here every 5 minutes. Owners and
managers sign in with their **POS username and password** and see them, even when the shop PC is off.

It runs on the shop's own **free Cloudflare account**: a Worker (the website) and a D1 database (the reports). Normally
the POS sets all of this up by itself (**Settings → Online reports → Set up**, see INSTALL.md). This folder is the
website's source code. The POS carries a copy and publishes it.

## How it works

- **The POS works out the reports** with the same code as its Reports page and uploads them as small JSON documents
  (`POST /api/upload`, with the shop's secret upload key). The website only stores and displays them, so its numbers
  are the POS's numbers.
- **Nothing on the website can change the shop's data.** It never connects to the shop.
- **Sign-in uses the POS accounts.** Only active managers and admins can sign in; cashiers can't.
  - **No passwords are uploaded.** For each account the POS sends its password salt and a SHA-256 of its stored PBKDF2
    key.
  - **The phone does the slow PBKDF2 step** (100,000 rounds, the same as the POS). The Worker only compares hashes, so
    it stays within the free plan's CPU limit.
  - **Lockout:** 5 wrong passwords lock an account for 15 minutes.
  - **Sessions:** last 30 days. They end when the password changes or the account is removed in the POS, and the POS
    can sign out all devices at once.
- **Pages:**
  - **Overview:** KPIs with trends, sales by hour against last week, the monthly target and forecast, smart insights, open drawers, recent sales.
  - **Analytics:** any period, day or month, with a comparison line, profit and margin, a busiest-times heatmap, payments, channels, categories, top products with trends, cashiers, customers, sizes, returns, delivery companies and drawer closes. CSV export and Print/PDF.
  - **Sales:** the last 50 sales with search, filters and each sale's items.
  - **Drawers:** each till's open drawer and the over/short history.
  - **Stock:** search every item by name, code, SKU or barcode (Arabic too) and see each size and colour's stock (in, low or out), price and sales this month. A summary tab has the stock value, low stock with "days left" at the current pace, and slow movers.

  All pages are in English or Arabic, in light or dark, on a phone or a computer. The site installs to the home screen and shows the last numbers offline.

  The code is plain ES modules with no libraries and no build step:
  - `src/ui/app.js`: the shell
  - `pages.js`: the pages
  - `charts.js`: hand-made SVG charts
  - `insights.js`, `i18n.js`, `format.js`, `dom.js`, `icons.js`: helpers
  - `sw.js`: offline use

  The fonts (Inter, IBM Plex Sans Arabic) are bundled under the SIL Open Font License; see `src/ui/fonts`.

## Documents the POS uploads

| Key | Contents |
|---|---|
| `store` | Name, currency symbol, LBP on/off, language, monthly target, Google verification code |
| `live` | Open drawers, the last 50 sales with their items, recent drawer closes, low-stock count, last 14 days |
| `period:today`, `period:yesterday`, `period:week`, `period:lastweek`, `period:month`, `period:lastmonth`, `period:year` | A full report for that period |
| `day:YYYY-MM-DD` | A finished day's report (kept 13 months) |
| `month:YYYY-MM` | A finished month's report (kept) |
| `stock` | Stock value, low stock, slow movers |
| `catalog:0` … `catalog:9` | Every active item with its sizes and colours: SKU, barcode, stock, reorder level, price and sold in the last 30 days (no costs). Up to 4,000 variants per part; looked at every 10 minutes and sent only when something changed |

The exact shapes are in `test/sample.js`, and the POS builds them in `OnlineReportsBuilder.cs`.

## Trying it locally

You need Node.js 20 or later.

```
cd cloud/reports-worker
npm install
echo UPLOAD_KEY="local-dev-key" > .dev.vars
npx wrangler dev          # serves http://localhost:8787 with a local database
npm run seed              # in a second window: sample reports + account demo / demo-pass
```

Then open http://localhost:8787 and sign in as **demo / demo-pass**.

## Tests

```
npm test
```

The tests start the Worker locally (workerd with a local D1) and check:
- uploads and the upload key
- sign-in, lockout and sessions
- password changes, removed accounts and sign out all devices
- old-day cleanup

## Deploying by hand (for testing with your own account)

The POS does this for each shop. To test it by hand:

```
npx wrangler login
npx wrangler d1 create pos-reports            # copy the database_id it prints into wrangler.toml
npx wrangler secret put UPLOAD_KEY            # type a long random key
npx wrangler deploy                           # prints https://pos-reports.<your-subdomain>.workers.dev
npm run seed -- https://pos-reports.<your-subdomain>.workers.dev <the same key>
```

## Staying within the free plan

The free plan never charges anything. If a daily limit were reached, the site would only pause until midnight UTC.
The site and the POS are built to stay far below the limits:

| Limit (per day, free plan) | One shop, a normal day | How it's kept low |
|---|---|---|
| 100,000 requests | About 300 uploads, plus about 30 an hour per phone with the site open | Phones ask "anything new?" every 2 minutes while the page is visible (every 10 minutes when the shop is quiet), never while hidden |
| 5,000,000 rows read | A few thousand | The poll reads 2 to 3 rows. The list of past days is one row. Reports are downloaded only when the shop sent something new |
| 100,000 rows written | Under 3,000 | Unchanged reports aren't sent again. A heartbeat writes 1 row. Clean-up runs every half hour only |
| 50 queries per request | At most about 40 | The POS sends at most 25 documents per upload |
| 5 GB stored | About 25 MB | Days are kept 13 months |

**Safety nets:**
- **The POS's own caps:** it stops itself at 400 uploads or 5,000 documents a day.
- **Pause on a limit:** if Cloudflare ever answers "limit reached" (429 or error 1027), the POS pauses until midnight UTC and Settings says so.
- **Public pages are limited per visitor:** sign-in and the shop name allow 30 requests a minute per visitor, so a bot can't use up the day's requests.

## Chrome says the site is dangerous

Brand-new `workers.dev` sites with a password box are sometimes wrongly flagged by Google Safe Browsing. The sign-in
page names the shop and says what the site is, which helps. To clear a warning for good:

1. **Report the mistake:** https://safebrowsing.google.com/safebrowsing/report_error/ (no account needed).
2. **Prove the site is yours and ask for a review:**
   1. Open https://search.google.com/search-console and add the site's link as a **URL prefix** property.
   2. Choose **HTML tag** and copy the tag.
   3. In the POS, go to **Settings → Online reports → Google verification code**, paste the tag and press **Save**. The website shows the tag within a minute.
   4. Press **Verify** in Search Console.
   5. Under **Security issues**, press **Request review**. Google usually clears it within a few days.
3. **A custom domain** (about $10 a year, added to the Worker in Cloudflare) avoids the shared workers.dev reputation altogether.
