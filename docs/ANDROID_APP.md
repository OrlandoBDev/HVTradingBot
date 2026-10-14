# Android App (self-contained)

Status: **implemented**. The code under `src/HVTradingBot.Mobile.Core` is covered by tests on Linux. The Android
project (`apps/HVTradingBot.Android`) is built by the **Android app** CI workflow, and its behaviour on a phone still
needs the [manual acceptance test](#manual-acceptance-test).

## Goal

The whole trading bot on an Android phone, **without a server**: no hosted API, no Docker, no PostgreSQL. The engine,
the dashboard and the database live in the app and keep trading in the background. The phone only goes online to reach
Deriv (prices and demo orders) and, optionally, the SMTP server for email notifications.

## How it works

```
HVTradingBot app (one Android process)
 ├─ TradingService (foreground service, "HVTradingBot is trading" notification, partial wake lock)
 │    └─ MobileRuntime (src/HVTradingBot.Mobile.Core)
 │         ├─ Trading side: the worker's hosted services (TradingWorker, BrokerSettingsWatcher, test trades,
 │         │   close requests) in their own host, restarted by EngineSupervisor when they stop
 │         ├─ Dashboard side: DashboardQueries + DashboardActions (the API's logic, no HTTP)
 │         ├─ LocalApi: the API's routes and JSON, answered in-process
 │         ├─ LiveUpdates + EventFeed: status every 2 s, learning pulse when a setup is added or resolved
 │         └─ SQLite database + Data Protection keys in the app's private storage
 └─ MainActivity: WebView with the React dashboard (the same build as the web version)
      https://appassets.androidplatform.net/        → dashboard files from the APK's assets
      https://appassets.androidplatform.net/api/... → LocalApi (intercepted, never leaves the phone)
```

- **Same engine, same rules.** The trading side runs exactly the worker's code: risk engine, kill switch, idempotent
  orders, learning, Deriv reconciliation. The dashboard side mirrors the API. The two share only the database, as the
  API and worker processes do on a server, so "only the worker talks to the broker" still holds (architecture test
  `Dashboard_cannot_place_orders`).
- **Always running.** Opening the app starts `TradingService`, a foreground service that Android does not stop. It
  keeps running with the app closed and the screen off, until **Stop trading** in its notification. It starts again
  after a phone restart or app update if it was running before (`BootReceiver`), and the app asks once to be exempt
  from battery optimization so Doze does not delay the 5-minute evaluation.
- **Restarts.** `EngineSupervisor` does what Docker and run.sh do for the worker process: a new market selection
  restarts the engine at once; failures (e.g. no network while loading history) restart it with a back-off from 5 s
  to 1 min. The dashboard keeps working meanwhile.
- **Live updates.** The permanent notification shows equity, open positions and today's P&L. Trades opened and closed
  and kill-switch changes arrive as separate notifications (channel "Trades"). In the dashboard, status updates every
  2 s, and pages (including **Learning**) reload whenever a bar is processed or a learning setup is added or resolved.
- **No login.** The phone's lock screen protects the app; the dashboard's login, sign-out and password settings are
  hidden. **Settings → Engine & log** shows the engine's state and recent log lines instead (there is no terminal).
- **Database.** SQLite (`Database:Provider=Sqlite`, migrations in `src/HVTradingBot.Infrastructure.Sqlite`). Decimals
  are stored as REAL (15 significant digits) and the system-state row is serialized with a process-wide lock instead
  of `SELECT … FOR UPDATE`. The persistence integration tests run against both PostgreSQL and SQLite.

## Install

1. Open the latest **Android app** workflow run on GitHub (Actions tab) and download the **HVTradingBot-android**
   artifact (a zip containing `HVTradingBot.apk`).
2. Copy the APK to the phone and open it. Android asks to allow installing apps from that source (browser or file
   manager) the first time.
3. Open **HVTradingBot**. Allow notifications and "unrestricted" battery use when asked.
4. In the dashboard go to **Settings → Broker account** and enter your Deriv App ID and **demo** account token (from
   [developers.deriv.com](https://developers.deriv.com)). Pick markets under **Settings → Markets**.

Requires Android 8.0 (API 26) or newer.

### Updates and the signing key

Android installs an update only over an app signed with **the same key**. Without a key configured, each CI build is
signed with a new throwaway debug key: fine for trying the app, but updating then means uninstalling first, which
**deletes the app's database** (trading history, learning, settings; open positions stay protected at Deriv).

Create a key once and store it as repository secrets:

```bash
keytool -genkeypair -v -keystore hvtradingbot.keystore -alias hvtradingbot -keyalg RSA -keysize 4096 -validity 10000
base64 -w0 hvtradingbot.keystore   # macOS: base64 -i hvtradingbot.keystore
```

**The key of the app already on your phone.** The builds sent during development are signed with the key in
`hvtradingbot-signing.keystore` (alias `androiddebugkey`, password `android`, SHA-256 fingerprint
`54:01:04:53:9B:FB:B5:1B:7F:01:1A:E8:39:88:87:D3:EB:59:A9:BF:47:C8:1C:12:2A:CE:7A:1F:BF:D8:EA:8C`). Use that key, not a new
one, so updates install over that app and keep its database: `ANDROID_KEYSTORE_BASE64` = the file's base64 text,
`ANDROID_KEYSTORE_PASSWORD` = `android`, `ANDROID_KEY_ALIAS` = `androiddebugkey`. A new key only makes sense for a fresh
install.

GitHub → Settings → Secrets and variables → Actions:
`ANDROID_KEYSTORE_BASE64` (the base64 text), `ANDROID_KEYSTORE_PASSWORD` (store and key password), optionally
`ANDROID_KEY_ALIAS` (default `hvtradingbot`). Keep the keystore file somewhere safe: without it no later build can update
the installed app.

### Your data across updates

- An update signed with the same key installs over the app and keeps its private storage: the SQLite database
  (trades, decisions, learning, settings) and the keys that decrypt the Deriv token.
- New versions only add to the database: each migration runs once when the new version starts, and
  `SqliteUpgradeTests` checks that a database from the first release keeps its data after every newer migration. Nothing
  in the app deletes the database.
- What does delete it: **uninstalling** the app, or Android's "Clear storage". Android refuses an update signed with a
  different key, and the only way around that is uninstalling. Android backup is off (`allowBackup="false"`), so an
  uninstall cannot be undone.

## Backup and restore

Everything the app knows (trades, decisions, learning, signals, settings) is in one SQLite file on the phone. Settings ›
Engine & backup › **Save backup…** writes a zip to a place you choose (Google Drive, Downloads); the Overview reminds you
when there is no backup from the last 7 days. The copy is made while the engine keeps running.

**Restore from backup…** checks the file first (it must be an HVTradingBot backup, intact, and not from a newer app
version) and shows what it holds before replacing anything. The current data is first saved inside the app
(`backups/before-restore-*.zip`, never deleted by the app), then trading restarts on the restored data.

A backup leaves out the encryption keys, so the file never exposes the Deriv token or email password. Restoring on the
same phone keeps them working; on a new phone, enter them again in Settings.

The database is too large for Android's automatic Google backup (25 MB limit), which stays off.

## Build locally

Needs .NET 10, Node.js 22, JDK 17+, the `android` workload and Android SDK platform 36:

```bash
dotnet workload install android
dotnet build apps/HVTradingBot.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="$HOME/Android/Sdk" -p:AcceptAndroidSDKLicenses=True
apps/HVTradingBot.Android/build-apk.sh -p:AndroidSdkDirectory="$HOME/Android/Sdk"   # → apps/HVTradingBot.Android/bin/apk/HVTradingBot.apk
adb install -r apps/HVTradingBot.Android/bin/apk/HVTradingBot.apk
```

`ANDROID_KEYSTORE`, `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_ALIAS` sign with your key (see above). Engine log:
`adb logcat -s HVTradingBot`.

## Limits

- **The phone is the server.** Trading pauses while the phone is off or offline; the stale-data rule then blocks new
  trades and open positions rely on their broker-side stop loss and take profit, as on a server. Some manufacturers
  (Xiaomi, Huawei, Samsung "deep sleep") stop background apps regardless; add the app to their "never sleeping" list.
- **One device, one database.** The phone's database is independent of a server or Mac installation. Do not run the
  phone and a server worker on the same Deriv account at the same time: each would manage the account's positions on
  its own.
- **Demo accounts only**, as everywhere else (ADR-008).
- The APK is about 60 MB: the .NET runtime and EF Core ship inside, untrimmed (trimming breaks reflection-based code at
  runtime, on the phone).

## Manual acceptance test

1. Install, open, allow notifications and battery exemption. The "HVTradingBot is starting" notification appears and
   turns into "Trading on Deriv demo" once Deriv settings are saved and history is loaded.
2. Dashboard: overview, markets, decisions, trades, learning, performance, backtest (simulated, 20 days), risk, audit,
   all settings pages load; saving invalid risk limits shows the field errors.
3. Kill switch on/off from the dashboard: banner, notification and audit entry.
4. Change the market selection: **Settings → Engine & log** shows a restart; the new markets appear.
5. Test trade on an open market: "Opened …" and "… closed" notifications, trade in history.
6. Close the app, turn the screen off for 30 minutes: decisions keep arriving (Decisions page, audit).
7. Restart the phone: the engine notification comes back without opening the app.
8. **Stop trading** in the notification: the notification disappears and nothing trades; opening the app starts again.
