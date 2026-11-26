# Mailtide

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文** | [日本語](README.ja.md)

面向 **Windows**、**Linux** 與 **Android** 的個人、多帳戶、離線優先電子郵件用戶端。

採用 **.NET 10** 與 **Avalonia 12**（Fluent 佈景主題）建置。核心郵件邏輯維持與 UI 無關；輕薄的 Desktop 與 Android 主機提供生命週期管理、安全儲存與 OAuth 系統瀏覽器登入流程。

[![CI](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml)

## 適用對象

適合在同一部裝置上管理多個個人郵件帳戶，並希望在斷線時仍能閱讀信件的使用者。Mailtide 是本機用戶端（IMAP/SMTP），並非網頁郵件外殼，也不針對企業 / Entra 工作帳戶。

**v1 平台：** Windows · Linux（Ubuntu 24.04 x64 基線）· Android  
v1 的 Android 版使用共用的桌面 / 平板寬度介面，手機 / 窄螢幕折疊版面不在範圍內。  
**v1 不包含：** macOS · iOS · Microsoft Store / Play Store / Flatpak / deb 分發管道

## 目前狀態

目前產品為 **0.21**：在 0.20 的 IMAP / 儲存 / 搜尋能力之上打造的精簡 Fluent 三欄式日常介面（Outlook 風格導覽列 | 執行緒清單 | 閱讀/撰寫窗格）。分發標籤：**v0.2.0**。

目前目標：無。

開發時請依照下方說明從原始碼建置。除非 Release 封裝中已嵌入公開用戶端 ID，否則本機 `dotnet run` 仍需設定 OAuth 環境變數。

## 功能與能力

| 領域 | 已提供功能 |
|------|------------|
| 帳戶 | Google（OAuth）、Microsoft 個人帳戶 / Outlook.com（OAuth）、QQ 郵箱（預設 + 授權碼）、手動 IMAP/SMTP + 密碼 / 應用程式密碼 |
| 離線 | 跨應用程式共用的 EF Core + SQLite 儲存區；附件內容寫入磁碟；無網路時仍可閱讀已同步的 Message |
| 同步 | 同處理序同步引擎；依 Account 平行同步；草稿 → Outbox → SMTP |
| UI | 精簡 Fluent 三欄式日常介面（左側 Account/Mailbox 樹狀檢視、Thread 清單、閱讀/撰寫窗格），由 Desktop 與 Android（桌面/平板寬度）共用。支援 Unified Inbox 與依 Mailbox 檢視 Threads、對話閱讀窗格、窗格內撰寫與本機草稿、依 Account 區分的 Outbox、HTML 郵件檢視、本機搜尋、移動 Message 或回覆執行緒、Mailbox 建立/重新命名/刪除。支援 Outlook 風格 `Ctrl+` 快速鍵。 |
| 搜尋 | 於目前導覽範圍內執行本機搜尋。公開運算子：`is:unread`、`is:read`、`is:flagged`、`is:starred`、`is:failed`、`has:attachment`（亦支援 `has:attach`、`has:attachments`）、`from:`、`to:`、`cc:`、`bcc:`、`subject:`、`filename:` / `file:`、`in:` / `label:`、`after:`、`before:`、`on:`、`older_than:`、`newer_than:`、`larger:` / `size:`、`smaller:`、`rfc822msgid:` / `msgid:`，加上關鍵字搜尋。`from:me`、`to:me`、`cc:me` 與 `bcc:me` 會比對 Account 位址。`is:` 與 `in:` 接受 Mailbox 角色字詞（`inbox`、`sent`、`draft` / `drafts`、`trash` / `bin` / `deleted`、`junk` / `spam`、`archive`）以及 `in:anywhere` / `in:all` / `in:any`。前導 `-` 可否定運算子。 |
| 安全 | 憑證僅透過作業系統層級安全儲存（Windows DPAPI、Linux libsecret、Android Keystore）保存，無明文備援 |
| 更新 | Desktop 會檢查 GitHub Releases（私人儲存庫請設定 `MAILTIDE_GITHUB_TOKEN`）；Android 則透過安裝 Releases 中的新版 APK 更新 |

領域詞彙（Account、Person、Message、Mailbox、Unified Inbox、Outbox、Credential）定義於 [`CONTEXT.md`](CONTEXT.md)。

## 儲存庫結構

```
├── src/
│   ├── Mailtide.Core/       # 儲存、同步、IMAP/SMTP、驗證協調（無 UI）
│   ├── Mailtide.UI/         # 共用 Avalonia UI（呈現與意圖）
│   ├── Mailtide.Desktop/    # Windows / Linux 主機
│   └── Mailtide.Android/    # Android 主機
├── tests/                   # MSTest + Microsoft.Testing.Platform
├── build/                   # NUKE 建置專案
├── packaging/               # Windows Inno Setup · Linux AppImage 封裝腳手架
├── docs/adr/                # 架構決策紀錄
└── Mailtide.slnx
```

請參閱 [ADR-0001](docs/adr/0001-core-thin-hosts.md)（核心 + 輕薄主機）、[ADR-0002](docs/adr/0002-ef-core-sqlite-store.md)（SQLite 儲存）、[ADR-0003](docs/adr/0003-secure-storage-apis.md)（安全儲存）。

## 前置需求

- 與 [`global.json`](global.json) 相符的 **.NET SDK**（目前為 `10.0.302`，`rollForward: latestFeature`）
- 僅在建置 Android 主機（`CompileAndroid` / `PublishAndroidApk`）時才需要 **Android workload**：

  ```bash
  dotnet workload install android
  ```

  預設的 `Restore` / `Compile` / `Test` 與 Desktop 發布不會還原 Android 主機。若執行 `CompileAndroid` 時出現 `NETSDK1147`（`wasm-tools`），請執行 `dotnet workload restore` 使 workload 與 `rollForward` 選取的 SDK 一致。

- **Linux 桌面憑證：** 可運作的 Freedesktop Secret Service（libsecret）
- **選用 — OAuth 帳戶類型：** 公開 OAuth 用戶端 ID（參閱下方 [OAuth 組態](#oauth-組態)）。QQ 郵箱與手動 IMAP/SMTP 不需要這些設定。

## 複製、建置與測試

```bash
git clone https://github.com/Skymly/Mailtide.git
cd Mailtide
```

NUKE 進入點（建議使用；與 CI 使用相同路徑）：

```bash
# Linux / bash
./build.sh Test

# Windows
.\build.ps1 Test
# 或: build.cmd Test
```

實用目標：

| 目標 | 用途 |
|------|------|
| `Restore` / `Compile` / `Test` | 還原、建置並執行受控測試專案。不會還原或建置 Android 主機。預設目標為 `Test`。 |
| `CompileAndroid` | 還原並建置 Android 主機（需要 Android workload） |
| `PublishDesktopWindows` / `PackWindowsInstaller` | Windows 裁剪獨立式發布 + Inno Setup 安裝程式（僅限 Windows） |
| `PublishDesktopLinux` / `PackAppImage` | Linux 裁剪獨立式發布 + AppImage（僅限 Linux） |
| `PublishAndroidApk` | 於 `artifacts/release/` 產生可供側載的 APK |
| `Pack` / `Release` | 彙整發布成品 / 透過 `gh` 上傳 |

封裝時可傳入版本號碼，例如 `./build.sh PackAppImage --Version 0.1.0`。

您也可以在 IDE 中開啟 `Mailtide.slnx`，或直接使用 `dotnet` 還原與建置個別專案。

## 執行（Desktop）

```bash
# 選用 OAuth（Google / Microsoft 個人帳戶）
export MAILTIDE_GOOGLE_OAUTH_CLIENT_ID="your-google-client-id.apps.googleusercontent.com"
export MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID="your-microsoft-public-client-id"

dotnet run --project src/Mailtide.Desktop
```

於 Windows PowerShell 中：

```powershell
$env:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID = "your-google-client-id.apps.googleusercontent.com"
$env:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID = "your-microsoft-public-client-id"
dotnet run --project src/Mailtide.Desktop
```

應用程式資料儲存於作業系統本機應用程式資料目錄下的 `Mailtide` 子目錄。

## 執行 / 部署（Android）

```bash
# 在建置期將公開 OAuth 用戶端 ID 寫入 APK 處理序環境
dotnet publish src/Mailtide.Android/Mailtide.Android.csproj \
  -c Release -f net10.0-android \
  -p:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID=your-google-client-id.apps.googleusercontent.com \
  -p:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID=your-microsoft-public-client-id
```

或使用 NUKE：`./build.sh PublishAndroidApk --Version 0.1.0`  
當設定了 `ANDROID_KEYSTORE_PATH`、`ANDROID_KEYSTORE_PASSWORD`、`ANDROID_KEY_ALIAS` 與 `ANDROID_KEY_PASSWORD` 時，發布會使用正式簽章；否則使用預設/偵錯簽章供側載使用。

## OAuth 組態

Desktop 與 Android 會從環境變數讀取**公開** OAuth 用戶端 ID（應用程式內不含 client secret）：

| 變數 | 用途 |
|------|------|
| `MAILTIDE_GOOGLE_OAUTH_CLIENT_ID` | Google / Gmail OAuth |
| `MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID` | Microsoft 個人帳戶（Outlook.com / Hotmail / Live）— 非 Entra 工作/學校帳戶 |
| `MAILTIDE_GITHUB_TOKEN` | 僅限 Desktop：選用 PAT，讓私人儲存庫的 GitHub Releases 更新檢查可運作。切勿提交此 Token。Android 無應用程式內更新器。 |

請註冊符合主機 OAuth 用戶端的迴圈重新導向（Desktop）或自訂 scheme / intent 重新導向（Android）之公開 / 原生用戶端。若未設定這些變數，新增 Google 與 Microsoft 帳戶流程會在授權時失敗；QQ 郵箱與手動 IMAP/SMTP 仍可正常運作。

## 分發

從 [GitHub Releases](https://github.com/Skymly/Mailtide/releases) 安裝：

- Windows：`Mailtide-*-win-x64-setup.exe`（Inno Setup）
- Linux：`Mailtide-*-linux-x64.AppImage`
- Android：`Mailtide-*-android.apk`

由標籤推送或 Release 工作流程驅動封裝（參閱 [`.github/workflows/release.yml`](.github/workflows/release.yml)）。

## 貢獻者與 Agent 文件

| 文件 | 職責 |
|------|------|
| [`CONTEXT.md`](CONTEXT.md) | 產品一句話簡介與領域詞彙表 |
| [`docs/adr/`](docs/adr/) | 架構決策紀錄 |
| [`AGENTS.md`](AGENTS.md) | Agent 入口：Issue、分診標籤與領域文件 |
| [`docs/agents/`](docs/agents/) | Issue 追蹤器、分診詞彙表與領域文件配置 |

Issue 統一於 GitHub Issues 維護。建立工單與 PR 時請優先使用 `CONTEXT.md` 中的詞彙表術語。
