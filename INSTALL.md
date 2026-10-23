# Installing Clothing Store POS for a customer

## 0. System requirements

### Main PC (runs the POS **and** SQL Server Express): one-till shops, or the first till of several

|                | Minimum                                   | Recommended                              |
|----------------|-------------------------------------------|------------------------------------------|
| Windows        | Windows 10 64-bit (version 1809 or newer) | Windows 11 64-bit                        |
| Processor      | 2 cores, 2 GHz (Intel Core i3 / Ryzen 3)  | 4 cores (Intel Core i5 / Ryzen 5)        |
| Memory (RAM)   | 8 GB                                      | 16 GB                                    |
| Disk           | SSD with 20 GB free                       | SSD with 50 GB or more free              |
| Screen         | 1366 × 768                                | 1920 × 1080 (touch screen optional)      |

* **Use an SSD.** On a hard disk (HDD), SQL Server and Windows start slowly and searches can take seconds.
* **4 GB of RAM works but is slow**, especially right after Windows starts or while Windows Update runs. The POS
  limits SQL Server to a quarter of the PC's memory (between 1 and 4 GB) so the rest stays usable, but 8 GB is the
  real minimum for a PC that also runs SQL Server.
* Don't run Visual Studio, games or other heavy programs on the till. They take the memory SQL Server needs.
* 32-bit Windows, Windows 7/8 and Windows S mode are not supported. SQL Server **2016 or newer** is required
  (2008/2012/2014 don't work).

### Extra tills (POS only, connected to the main PC's SQL Server)

|                | Minimum                    | Recommended                |
|----------------|----------------------------|----------------------------|
| Windows        | Windows 10 64-bit          | Windows 11 64-bit          |
| Processor      | 2 cores                    | 4 cores                    |
| Memory (RAM)   | 4 GB                       | 8 GB                       |
| Disk           | 1 GB free                  | SSD                        |
| Network        | Same network as the main PC (**wired** network recommended; Wi-Fi works but can drop) |

The main PC must stay on while the other tills are working, and its firewall must allow SQL Server (see "Several
tills" in README.md).

### Software

* **.NET is not needed**: the published app includes it.
* **SQL Server 2022 Express** (free): see step 2. Express stores up to 10 GB per database, which is many years of
  sales for a clothing store.
* **No internet needed** to run the POS. The license and the trial work offline. Internet is only used to send you
  the PC ID and receive the license key.

### Devices (all optional)

| Device                 | What works                                                                                   |
|------------------------|----------------------------------------------------------------------------------------------|
| Barcode scanner        | Any USB or Bluetooth scanner in keyboard mode that sends Enter after each scan               |
| Receipt printer        | Any printer with a Windows driver; 80 mm (receipt width 42) or 58 mm (width 32) thermal paper |
| Cash drawer            | A drawer plugged into the receipt printer's drawer port (RJ11/RJ12), opened by the POS        |
| Customer display       | A second monitor (HDMI/VGA/DisplayPort), set to "Extend these displays" in Windows            |
| Label printer          | A label printer with a Windows driver, or an office printer with A4 label sheets             |
| Backup disk            | A USB disk or a second drive for backup copies (a backup on the same disk doesn't survive a dead disk) |

## 1. Build a release (on your PC)

**Once:** open **License Maker**, click **Copy public key for the app** and paste it into
`src/ClothingStore.Desktop/licensing.json`, with your name, phone and email (customers see them on the license screens):

```json
{
  "publicKey": "-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----",
  "vendorName": "Your name",
  "vendorPhone": "+961 ...",
  "vendorEmail": "you@example.com"
}
```

Keep notes out of this file (put commands in this guide instead).

**Every release:** pull `main`, then in a terminal in the project folder (Visual Studio: **View → Terminal**):

```powershell
dotnet publish src/ClothingStore.Desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

The `publish` folder holds `ClothingStorePOS.exe` and `appsettings.json` (the `.pdb` files are only for diagnosing
crashes; they can be left out). The customer's PC doesn't need .NET.

A release build starts a **new customer with an empty database** (no demo products, customers or demo users). For a
demo PC only, add `"SeedDemoData": true` under `"Pos"` in `publish\appsettings.json` before the first start.

## 2. Install on the customer's PC

1. Install **SQL Server 2022 Express** (free; 2016 or newer is required, 2008/2012/2014 don't work). Choose **Basic**,
   keep the instance name `SQLEXPRESS`.
2. Copy the `publish` folder to e.g. `C:\ClothingStorePOS\` and start `ClothingStorePOS.exe` (the first start puts a
   shortcut on the desktop by itself).
3. Start it. The first start creates the empty `ClothingStorePOS` database with one user: `admin` / `admin123`
   (a new password is required at first sign-in).
4. **License:** without a key the app runs a **7-day free trial** (orange banner with the days left and your contact).
   To license it, the customer clicks **Enter new license key**, copies **this PC's ID** and sends it to you; make the
   key in License Maker and send it back.
5. Set up the shop: **Settings** (store name, address, phone, tax number, tax rate 11% or 0, currency, LBP rate,
   receipt and label printers, backup folder), **Users** (cashiers, managers), then categories, suppliers, products
   and delivery companies.

**Several tills:** install SQL Server Express on one PC only and point the other tills' `appsettings.json` at it
(see "Several tills" in README.md).

## 3. Updating a customer

Build a new `publish` folder and replace every file on the customer's PC **except `appsettings.json`**. On the next
start the app backs up the database and upgrades it. The license stays (it's kept in `C:\ProgramData\ClothingStorePOS`).

## 4. Check before handing over (about 10 minutes per shop)

What depends on the shop's own PC and devices can only be tested there. Sign in as admin and go through this once:

1. **Sign-in:** the first sign-in asks for a new admin password. Write it down for the owner.
2. **Settings → Printers:** choose the receipt printer and click **Test**; it prints a short slip. With a cash drawer,
   tick it and click **Test drawer**: the drawer opens (else try another *Drawer signal*).
3. **Settings → Backup:** set the backup folder (ideally another drive or a USB disk), click **Back up now**, then
   double-click the backup in the list: Explorer opens with the file.
4. **Cash drawer:** open a shift with a float.
5. **Register:** scan (or search) an item, take **cash** with change, and check the receipt prints and the drawer opens.
   Do a second sale paid by **card**. With a customer display, check the customer sees the items, the total and the
   change, and that scanning keeps working on the till.
6. **Returns:** return one item of the first sale (refund cash).
7. **Sales history:** both sales are there, the first shows the return.
8. **Cash drawer:** count and close the shift; the Z report prints and the expected cash is right.
9. **Reports:** today's figures match the two sales and the return.
10. The test sales stay in the history and today's report. To leave fewer, void the card sale in **Sales history**
    *before* step 8 (a sale can only be voided while its shift is open, and not once items were returned).

If anything fails, `%LocalAppData%\ClothingStorePOS\startup-errors.log` (start-up problems) and `slow-searches.log`
(slow searches) in the same folder help find out why (type `%LocalAppData%\ClothingStorePOS` in Explorer's address bar).
