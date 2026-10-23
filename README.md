# Clothing Store POS

A Windows desktop point-of-sale system for clothing and fashion retail, built with **C# / .NET 10 (LTS) / WPF**, using MVVM with
**Entity Framework Core + SQL Server**, so several tills can share one database.

## Features

| Area | What it does |
| --- | --- |
| **Register** | Scan barcodes or search by name/SKU/colour/size; size & colour variants; quantity +/-; line and cart discounts (% or amount); cashier discount limit with manager override; hold & resume sales (fitting room); keyboard shortcuts (F2 search, F4 customer, F6 qty, F7/F8 discounts, F9/F10 hold/resume, F12 pay) |
| **Payments** | Split tender across cash (dollars and Lebanese pounds), card, mobile wallet, store credit and loyalty points; quick-cash buttons for both currencies; change in dollars, pounds or both; printable receipts with the LBP total (auto-fits 58/80 mm thermal or A4), number of copies per print |
| **Lebanese pounds** | Prices stay in dollars; LBP is a second cash currency at the store's rate. Managers and admins change the rate from the top bar, every change is logged, and other tills pick it up within a minute. A sale or refund at an old rate is refused. LBP is rounded to a configurable step (up when collecting, down when paying out) |
| **Returns & exchanges** | Look up a receipt; partial returns; restock or write off; refunds go back the way the sale was paid (split payments in proportion), or to store credit; store credit and loyalty points always come back as credit and points, never cash; card refunds in cash need a manager; return window with manager override; refunds reconcile to the cent per tender |
| **Sales history** | Search by date/receipt/customer/product; reprint; void (manager only, restocks and reverses balances); CSV export |
| **Products** | Style + size × colour matrix with presets (XS–XXL, waist, shoe, kids…); auto SKU and in-store EAN-13 barcodes; per-variant price/cost overrides; brand, season, material, department |
| **Inventory** | Stock levels and valuation; low-stock highlighting; adjustments (damaged, lost, received…); physical stock counts; full movement ledger |
| **Price labels** | Select several products or stock rows (Ctrl/Shift+click) and print them in one go, or scan items into the list; copies per item, for all, or from stock; label size presets (A4 sheets such as 21/24/65 per page, label printers, hang tags) or a custom size and sheet position; choose what is printed (name, size/colour, price, LBP price, barcode, SKU, store name); live preview; settings remembered per PC |
| **Purchasing** | Suppliers; purchase orders; "add supplier's low-stock items"; partial and full receiving into stock with cost updates |
| **Customers** | Profiles with address and state (Lebanon's governorates, more can be added), filter by state, total spent / visits / last purchase per customer, purchase history; loyalty points (earn and redeem); store credit |
| **Cash drawer** | Open shift with float; pay-ins/pay-outs; X report; count and close with over/short; Z report; shift history. Dollars and pounds are counted separately |
| **Reports** | Sales, net revenue, gross profit and margin, average basket; breakdowns by product, category, size, payment method, cashier and day; stock valuation; CSV export and printable summary |
| **Online orders** | On the register, mark a sale as an online order (button or F11): where it came from (WhatsApp, Instagram, Facebook, phone, website, other), an optional delivery fee added to the total, and an address/notes line printed on the receipt. Sales history shows the source of every sale (in store or which channel) and filters by it; reports break sales down by channel |
| **Store logo** | Managers and admins pick the store logo (user menu → Store logo, or Settings); it becomes the icon of every window and on the taskbar, and appears on the sign-in screen and sidebar, on all tills. The desktop shortcut keeps the program's own icon, because Windows reads that from the .exe file |
| **Admin** | Users with roles (Cashier / Manager / Admin), PBKDF2-hashed passwords, forced password change; admins manage every account (add, edit, reset password, deactivate, delete unused accounts), managers add and edit cashier accounts only; store, tax (inclusive or exclusive), receipt and loyalty settings; automatic, verified SQL Server backups (every app start, scheduled and at shift close) with a backup log |

## Solution layout

```
ClothingStorePOS.sln
├── src/ClothingStore.Core      Domain entities, pricing engine, barcodes, receipt formatting, security (no dependencies)
├── src/ClothingStore.Data      EF Core DbContext, migrations, business services, demo seed data
├── src/ClothingStore.Desktop   WPF app (MVVM with CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting for DI)
└── tests/ClothingStore.Tests   xUnit tests for pricing, checkout, returns, inventory, purchasing, shifts and reports
```

All business rules (stock checks, discount limits, payment validation, refunds, shift cash) live in the
`ClothingStore.Data` services and are covered by tests. The UI only orchestrates them.

## Getting started

Requirements: Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and SQL Server 2019 or
later. The free [SQL Server Express](https://www.microsoft.com/sql-server/sql-server-downloads) edition is enough for a shop.
The default connection string expects an Express instance named `SQLEXPRESS` on the same PC, signed in with Windows
authentication.

```powershell
cd ClothingStorePOS
dotnet run --project src/ClothingStore.Desktop
```

Or open `ClothingStorePOS.sln` in Visual Studio 2026 (or Rider / VS Code with C# Dev Kit) and set **ClothingStore.Desktop** as the startup project.

On first launch the app creates the `ClothingStorePOS` database and its tables. The login it uses needs permission to create
a database (or create the empty database yourself and make the login its `db_owner`). A sample catalogue is loaded too,
with 15 styles, 125 size/colour variants, customers and suppliers.

| User | Password | Role |
| --- | --- | --- |
| `admin` | `admin123` | Admin (you must change this password on first sign-in) |
| `manager` | `manager123` | Manager (demo data only) |
| `cashier` | `cashier123` | Cashier (demo data only) |

Demo data is loaded only by development (Debug) builds, or when `"SeedDemoData": true` is set in `appsettings.json` before
the first run; a release build starts with an empty catalogue and only the `admin` account. If demo data was loaded,
delete or deactivate the demo users once real accounts exist.

### Configuration (`src/ClothingStore.Desktop/appsettings.json`)

```jsonc
{
  "ConnectionStrings": {
    "Pos": "Server=.\\SQLEXPRESS;Database=ClothingStorePOS;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Pos": {
    "SeedDemoData": true // optional: demo catalogue and users on first run (default: Debug builds only)
  }
}
```

Settings can also be overridden with environment variables, e.g. `ConnectionStrings__Pos=...`.

### Several tills

1. Install SQL Server Express on one PC (the "server", which can also be a till). In SQL Server Configuration Manager, enable
   TCP/IP for the instance, start the SQL Server Browser service and allow SQL Server through Windows Firewall.
2. In a shop without a Windows domain, Windows authentication doesn't work between PCs, so enable mixed-mode authentication
   and create a SQL login for the app (for example `pos`) that owns the `ClothingStorePOS` database.
3. On every till, set `ConnectionStrings:Pos` in `appsettings.json` to point at that PC:
   `Server=SHOP-PC\\SQLEXPRESS;Database=ClothingStorePOS;User Id=pos;Password=...;TrustServerCertificate=True`.

Stock levels and customer balances use optimistic concurrency, so two tills selling the last unit at the same moment can't
both succeed. The second gets a "please try again" message.

Usernames, SKUs, and category and supplier names are always compared without regard to case. Product and customer searches
follow the database's collation, which is case-insensitive in a default SQL Server install.

### Publishing a self-contained build

```powershell
dotnet publish src/ClothingStore.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Copy the `publish` folder to the till PC. It does not need .NET installed.

A release build needs your licensing public key in `src/ClothingStore.Desktop/licensing.json` (see **Licensing** below);
without it the build stops with an explanation. Add `-p:AllowUnlicensedBuild=true` to build one anyway (for testing).

### Licensing (selling the app)

Each PC needs a license key signed by you. The key names the store, the PCs it is valid on and the last valid day.

**License Maker** (`tools/ClothingStore.LicenseMaker`) is a small Windows app for you only (never give it to customers).
Build it once and keep the `publish` folder on your own PC:
```powershell
dotnet publish tools/ClothingStore.LicenseMaker -c Release -r win-x64 --self-contained -o C:\LicenseMaker
```

1. **Once:** open License Maker, click **Create new keys…** and choose a folder (e.g. `Documents\POS Licenses`). Back
   that folder up: it holds `private.pem` (without it you can't renew anyone) and the customer list. Click **Copy public
   key for the app**, paste it into `licensing.json` (`"publicKey"`) with your name, phone and email so customers see
   how to reach you, then publish the POS.
2. **New customer:** install the app; on first start it shows **this PC's ID** (e.g. `7KQ2-M9XD-ABCD-EFGH`). The
   customer sends it to you. In License Maker type the store name, click **Paste** for the PC ID (several tills can share
   one license), pick **1 month / 3 months / 6 months / 1 year / 2 years / Lifetime** or a date, and click **Generate
   key**. The key is copied; **Copy WhatsApp message** gives a ready-to-send message, or **Save .lic file…**. The
   customer pastes it on the activation screen.
3. **Renewing:** the customer list shows every store with its last day, in orange when 30 days or less are left and red
   when expired. Click the store, pick how long, **Generate key**. Renewing early adds the time after the current last
   day, so no days are lost.

Keys made earlier with the command-line tool can be added to the list with **Add old keys…**. The command line still
works too (`pos-license issue`, see `dotnet run --project tools/ClothingStore.LicenseTool`) and records into the same
customer list next to `private.pem`.

**On the customer's PC:** from 30 days before the last day, a banner (and a daily message) says how many days are left
and how to contact you. After the last day the app opens only the activation screen until a renewed key is entered
(user menu → **License…** accepts a new key at any time). A lifetime key never warns.

Copying the program to another PC doesn't copy the license: that PC has a different ID (taken from Windows' install ID),
so it asks for its own key. Setting the PC's date back doesn't help either: the check uses the latest of the PC clock,
the SQL Server clock, the latest sale/return/shift in the database and the last date the app was used (stored signed).

Honest limits: a licensing check that runs on the customer's PC can be removed by someone determined enough to modify
the program (.NET programs are easy to decompile). It stops casual copying and makes expiry enforceable; for stronger
protection, also run the published program through an obfuscator. Reinstalling Windows changes the PC's ID, so that
customer will need a new key.

## Searching and shortcuts

Every search box works the same way:

* Several words match in any order, each anywhere: `blue oxford m` finds "Oxford shirt, Blue, M"; customers by first and
  last name in any order, by state, phone, email or address.
* Arabic spelling variants match each other (أ إ آ ا, ة ه, ى ي ئ, ؤ و); vowel marks and the tatweel are ignored;
  Arabic digits (٠١٢…) work like 0 1 2; accents don't matter (é = e).
* A phone number is found however it is typed or saved (`70 123 456`, `70-123456`, `+961 70…`).
* On the register an exact barcode/SKU comes first, then names starting with the first word.
* The × in a search box (or Esc) clears it.

Drop-downs with many choices (delivery company/driver, state, category, supplier and the page filters) can be typed
into to search, but only accept an item from their list. In the online order window the delivery company or driver must
be one saved under **Deliveries → Companies & drivers** (**Add / edit…** opens that list).

Shortcuts: **F1** shows them all; **Ctrl+F** jumps to the page's search box; **F5** reloads the page; on the register
F2 search, F4 customer, F6 quantity, F7/F8 discounts, F9/F10 hold/resume, F11 online order, F12 pay. The register's
keys work wherever the keyboard focus is, also right after using the sidebar or the top bar.

## Daily workflow

1. **Cash Drawer**: open a shift with your starting float (dollars and pounds). Check the rate in the top bar; a manager
   clicks it to change it.
2. **Register**: scan items, add the customer (F4), then **Pay** (F12). Print or skip the receipt.
3. **Returns**: scan the receipt number. The refund goes back to the original payment method. For an exchange, refund to
   store credit, then sell the new item and pay with store credit.
4. **Cash Drawer**: at closing time, count the drawer, close the shift and print the Z report.
5. **Backups** happen automatically: after every shift close, and whenever the last backup is older than the interval set in
   **Settings** (24 hours by default). See below.

### Cash drawer per PC (changing cashiers)

The cash drawer belongs to the PC (till), not to the person: each PC has at most one open drawer.

* When a cashier signs in on a PC where another cashier's drawer is still open (they signed out or closed the app
  without closing it), the app asks:
  * **Continue this drawer**: their sales go into the same shift. The hand-over (who, when) is recorded and printed on
    the Z report; each sale still shows who made it.
  * **Count and close it**: count the cash and close the previous shift (any difference goes on that shift, and the Z
    report shows who closed it). Then open your own drawer; the counted cash is filled in as the starting float.
  Both choices are also on the **Cash Drawer** page.
* Signing out or closing the app with your drawer open asks whether to **leave it open** for the next cashier or
  **close it now**.
* Managers see open drawers on every PC in the Cash Drawer list (with the PC and who runs it now) and can **Count and
  close…** one that was left open.
* A cashier runs one drawer at a time. Drawers opened before this version attach to the PC their cashier next signs in on.

### Cash in Lebanese pounds

* **Settings → Lebanese pounds** switches LBP on or off and sets the rounding step (1,000 by default). New and existing
  stores start with LBP on at 89,500.
* On the payment screen, type what the customer handed over in the **dollars** box, the **pounds** box, or both. Choose
  how to give change: **Dollars + LBP** (whole dollars, the rest in pounds), **USD** or **LBP**.
* The amount to pay in pounds is rounded **up** to the step; change and refunds in pounds are rounded **down**, so the
  drawer is never short because of rounding.
* Refunds pay pounds back in pounds at **today's** rate (or the cashier picks dollars or pounds for the cash part).
* The X/Z reports and the close-shift screen show what should be in the drawer in each currency.

### Online orders (WhatsApp, Instagram, Facebook, phone)

Orders that come in by message are rung up on the **Register** like any sale:

1. Scan or pick the items.
2. Click **Online order** (or press **F11**). An online order always needs a customer: if none is chosen yet the
   customer picker opens first (create one there with their address and state). Then pick where the order came from, enter the delivery fee (if any; the last one
   used on this PC is suggested) and the address or Instagram name. The register shows "Online order · WhatsApp" and adds
   the delivery fee to the total. The **x** turns it back into an in-store sale.
3. Pay (cash in USD/LBP, card or wallet). The receipt says "Order via WhatsApp" and prints the address/notes.

**Sales history** has a **Source** column (In store / WhatsApp / Instagram ...) and a filter (All / In store / Online
orders); the selected sale shows its source, delivery fee and notes. **Reports → Channels** totals sales per channel.
An online order can be put on hold (F9) like any cart and keeps its details when resumed.

**When the delivery company collects the money** (it doesn't hand you cash straight away):

1. In the online order window, pick the **delivery company / driver**. Save them once under **Deliveries →
   Companies & drivers** (company or driver, contact person, phones, address, usual delivery fee, notes): picking one
   shows its phone and fills in its usual fee. Any other name can still be typed. Renaming a company there renames it on
   its orders; one with orders is set inactive instead of deleted. Companies typed on older orders are added
   automatically on upgrade.
2. When paying, choose **Delivery company (pay later)**. The sale is recorded, but that amount is not cash in the
   drawer: it is **owed** by the company. A tracking number can go in the reference box.
3. **Deliveries** (under Sell) lists those orders as *Awaiting payment* and shows what each company owes.
4. When the company pays, select the orders it paid for (Ctrl/Shift+click) and click **Payment received…**: choose cash
   (USD or LBP, into the open drawer, so it appears in the shift count and Z report) or a bank/wallet transfer, and
   enter what actually arrived. If the company kept its fee or paid short, the difference is recorded.
5. Returning an item from an order not paid yet just lowers what the company owes; after it was paid, the refund is cash.

The card/wallet reference or auth code typed at payment is saved with the payment: it prints on the receipt and shows
next to the payment in Sales history.

### Price labels

* In **Products** or **Inventory**, select the rows you want (Ctrl+click, Shift+click, Ctrl+A) and click **Print labels**.
  With nothing selected, Products opens an empty list you can scan items into; Inventory uses everything listed.
* In the label window, scan or type more items, set copies (one each, a number for all, or the stock on hand), pick a
  label size and what goes on it. The preview is drawn by the same code that prints.
* For sheets, **Position on the sheet** adjusts the margins and gaps if labels come out shifted, and **Skip labels already
  used** starts on a part-used sheet. Turn on **Outline** and print on plain paper to check alignment first.

### Backups

* SQL Server writes each backup on its own PC, into the folder set in **Settings** (empty = the server's default backup
  folder). The SQL Server service account must be able to write there. Point it at a cloud-synced or network folder so a
  copy survives if that PC fails.
* Every time the app starts and the database already exists, it is backed up. When the new version has to upgrade
  the database, the backup is taken first, before anything changes; otherwise it runs in the background while you
  sign in, so opening isn't held up. Several tills opening within 10 minutes make one backup between them. If it
  fails, the app says so and still opens.
* If SQL Server isn't ready yet when the app starts (just after Windows starts), the app waits for it (up to about a
  minute and a half, showing "Waiting for SQL Server…") instead of failing.
* Every backup gets its own file named with the date and time (`ClothingStorePOS_startup_20261007-091502-317.bak`,
  `_shift_...`, `_auto_...`, and `ClothingStorePOS_20261007-...bak` for **Back up now**), so none replaces another.
  Automatic backups older than 30 days are deleted (the newest 10 always stay); **Back up now** files are never deleted.
  Deleting needs SQL Server administrator rights (the default with Windows sign-in on SQL Express); without them old
  files are simply kept.
* Every backup is a full, copy-only backup with checksums, verified straight after it is written. Every till checks whether
  a backup is due, but a lock on the server makes sure only one backs up at a time. After a failure, the next automatic
  attempt waits an hour.
* **Settings** lists recent backups and any errors. If the backup after closing a shift fails, the cashier is told.
* To restore, close the app on every till and use **Restore Database** in SQL Server Management Studio.

## Hardware

* **Barcode scanners**: any USB/Bluetooth scanner in keyboard-wedge mode that sends Enter after each scan.
* **Receipt printers**: any Windows-installed printer. Set *Receipt width* to 42 for 80 mm or 32 for 58 mm paper.
* **Default printers (per PC)**: *Settings → Printers* picks the invoice/receipt printer, how many invoice copies to
  print, and the label printer. With a printer chosen, receipts and labels print straight to it without the Windows print
  window (**Test** prints a short slip). **Choose printer…** in the print window prints elsewhere once. *Ask every time*
  brings the Windows print window back. If a chosen printer is removed, the Windows print window is shown instead.
* **Labels**: 3-across A4/Letter label sheets (labels about 33 mm tall), or one label per page on a label printer. Labels use Code 128.
* **Cash drawer (per PC)**: a drawer plugged into the receipt printer opens on cash sales, refunds, pay-ins/outs and
  shift open/close (each can be switched off), or with the register's *Open drawer* button. Set it up in
  *Settings → Printers* (**Test** opens it).
* **Customer display (per PC)**: a second monitor facing the customer. Set Windows to *Extend these displays*
  (Windows + P), then in *Settings → Customer display* tick the box and pick the screen (**Identify screens** shows each
  screen's number on it). The customer sees the items and total (dollars and pounds), what they handed over and their
  change while paying, then a thank-you; between sales the logo, store name and a welcome message (set for all tills).
  It opens by itself when the app starts and is skipped if that screen is unplugged.

## Development

```bash
dotnet build ClothingStorePOS.sln
dotnet test                                   # needs a SQL Server, see below
dotnet tool restore
dotnet ef migrations add <Name> --project src/ClothingStore.Data --startup-project src/ClothingStore.Data
```

Each test creates and drops its own database on a real SQL Server. On Windows the tests use LocalDB
(`(localdb)\MSSQLLocalDB`, installed with Visual Studio). Elsewhere, set `POS_TEST_SQLSERVER` to a connection string
without a database name, for example with SQL Server in Docker:

```bash
docker run -d -p 1433:1433 -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Your_password1' mcr.microsoft.com/mssql/server:2022-latest
export POS_TEST_SQLSERVER='Server=localhost;User Id=sa;Password=Your_password1;TrustServerCertificate=True'
dotnet test
```

The solution sets `EnableWindowsTargeting=true`, so CI agents on Linux can compile the WPF project and run the tests.
Running the WPF app still needs Windows.

### Notes and limits

* Earlier builds stored data in a SQLite file (`pos.db`). That data is not imported into SQL Server automatically.
* Card payments are recorded, not processed: take the payment on your card terminal and enter the auth code as the reference.
