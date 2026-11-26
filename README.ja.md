# Mailtide

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | **日本語**

**Windows**、**Linux**、**Android** 向けの個人用・マルチアカウント・オフラインファーストのメールクライアントです。

**.NET 10** と **Avalonia 12**（Fluent テーマ）で構築されています。コアのメールロジックは UI に依存せず、薄い Desktop ホストおよび Android ホストがライフサイクル、セキュアストレージ、システムブラウザを使った OAuth フローを提供します。

[![CI](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml)

## 対象ユーザー

1 台のデバイスで複数の個人メールアカウントを運用し、ネットワーク接続が途切れてもメールを閲覧できる状態を維持したい方向けです。Mailtide はローカルクライアント（IMAP/SMTP）であり、Web メールのラッパーでも、企業／Entra 職場アカウント向け製品でもありません。

**v1 プラットフォーム:** Windows · Linux（Ubuntu 24.04 x64 ベースライン）· Android  
v1 の Android 版は共有のデスクトップ／タブレット幅シェルを使用します。スマートフォン／狭幅の折りたたみレイアウトは対象外です。  
**v1 対象外:** macOS · iOS · 配布チャネルとしての Microsoft Store / Play Store / Flatpak / deb

## 現在のステータス

現在の製品バージョンは **0.21** です。0.20 の IMAP・ストア・検索基盤の上に、コンパクトな Fluent 3 ペインの日常シェル（Outlook 風ナビゲーション | スレッド一覧 | 閲覧/作成ペイン）を備えています。配布タグ：**v0.2.0**。

現在のターゲット：なし。

開発時は下記のソースからのビルド手順を使用してください。Release パックに公開クライアント ID が焼き込まれていない限り、ローカルの `dotnet run` には OAuth 環境変数が必要です。

## 機能

| 領域 | 提供内容 |
|------|----------|
| アカウント | Google（OAuth）、Microsoft 個人向け / Outlook.com（OAuth）、QQ Mail（プリセット + 認証コード）、手動 IMAP/SMTP + パスワード / アプリパスワード |
| オフライン | インストール単位で共有する EF Core + SQLite ストア、添付ファイルのディスク保存、ネットワークなしでの同期済み Message 閲覧 |
| 同期 | インプロセス同期エンジン、Account ごとの並列同期、下書き → Outbox → SMTP |
| UI | Desktop と Android（デスクトップ／タブレット幅）で共有されるコンパクトな Fluent 3 ペインシェル（左の Account/Mailbox ツリー、Thread 一覧、閲覧/作成ペイン）。Unified Inbox と Mailbox 別 Threads、スレッド閲覧ペイン、ペイン内での作成とローカル下書き、Account 別 Outbox、HTML Message 表示、ローカル検索、Message 移動や返信スレッド操作、Mailbox 作成・名前変更・削除に対応。Outlook 風の `Ctrl+` ショートカットも利用可能。 |
| 検索 | 現在のナビゲーション範囲内でのローカル検索。公開演算子：`is:unread`、`is:read`、`is:flagged`、`is:starred`、`is:failed`、`has:attachment`（`has:attach`、`has:attachments` も可）、`from:`、`to:`、`cc:`、`bcc:`、`subject:`、`filename:` / `file:`、`in:` / `label:`、`after:`、`before:`、`on:`、`older_than:`、`newer_than:`、`larger:` / `size:`、`smaller:`、`rfc822msgid:` / `msgid:`、および本文テキスト。`from:me`、`to:me`、`cc:me`、`bcc:me` は Account アドレスに一致します。`is:` と `in:` は Mailbox ロール語（`inbox`、`sent`、`draft` / `drafts`、`trash` / `bin` / `deleted`、`junk` / `spam`、`archive`）と `in:anywhere` / `in:all` / `in:any` を受け付けます。先頭の `-` で演算子を否定します。 |
| セキュリティ | 認証情報は OS 標準のセキュアストレージ（Windows DPAPI、Linux libsecret、Android Keystore）のみに保存し、平文フォールバックはありません |
| 更新 | Desktop は GitHub Releases を確認（プライベートリポジトリでは `MAILTIDE_GITHUB_TOKEN` を設定）；Android は Releases から新しい APK をインストールして更新 |

ドメイン用語（Account、Person、Message、Mailbox、Unified Inbox、Outbox、Credential）は [`CONTEXT.md`](CONTEXT.md) で定義されています。

## リポジトリ構成

```
├── src/
│   ├── Mailtide.Core/       # ストア、同期、IMAP/SMTP、認証オーケストレーション（UI なし）
│   ├── Mailtide.UI/         # 共有 Avalonia UI（プレゼンテーション + インテント）
│   ├── Mailtide.Desktop/    # Windows / Linux ホスト
│   └── Mailtide.Android/    # Android ホスト
├── tests/                   # MSTest + Microsoft.Testing.Platform
├── build/                   # NUKE ビルドプロジェクト
├── packaging/               # Windows Inno Setup · Linux AppImage パッケージング基盤
├── docs/adr/                # アーキテクチャ決定記録
└── Mailtide.slnx
```

[ADR-0001](docs/adr/0001-core-thin-hosts.md)（Core + 薄いホスト）、[ADR-0002](docs/adr/0002-ef-core-sqlite-store.md)（SQLite ストア）、[ADR-0003](docs/adr/0003-secure-storage-apis.md)（セキュアストレージ）を参照してください。

## 前提条件

- [`global.json`](global.json) に一致する **.NET SDK**（現在は `10.0.302`、`rollForward: latestFeature`）
- Android ホストをビルドする場合（`CompileAndroid` / `PublishAndroidApk`）のみ **Android ワークロード**：

  ```bash
  dotnet workload install android
  ```

  既定の `Restore` / `Compile` / `Test` および Desktop パブリッシュは Android ホストをリストアしません。`CompileAndroid` が `NETSDK1147`（`wasm-tools`）で失敗する場合は、`dotnet workload restore` を実行してワークロードを `rollForward` で選択された SDK に合わせてください。

- **Linux Desktop 認証情報：** 動作する Freedesktop Secret Service（libsecret）
- **任意 — OAuth アカウント種別：** 公開 OAuth クライアント ID（下の [OAuth 設定](#oauth-設定) を参照）。QQ Mail と手動 IMAP/SMTP には不要です。

## クローン・ビルド・テスト

```bash
git clone https://github.com/Skymly/Mailtide.git
cd Mailtide
```

NUKE エントリポイント（推奨；CI と同じ経路を使用）：

```bash
# Linux / bash
./build.sh Test

# Windows
.\build.ps1 Test
# または: build.cmd Test
```

主なターゲット：

| ターゲット | 目的 |
|------------|------|
| `Restore` / `Compile` / `Test` | マネージドテストプロジェクトのリストア、ビルド、実行。Android ホストのリストア／ビルドは行いません。デフォルトターゲットは `Test`。 |
| `CompileAndroid` | Android ホストのリストアおよびビルド（Android ワークロードが必要） |
| `PublishDesktopWindows` / `PackWindowsInstaller` | Windows 向けトリミング自己完結パブリッシュ + Inno Setup インストーラー（Windows のみ） |
| `PublishDesktopLinux` / `PackAppImage` | Linux 向けトリミング自己完結パブリッシュ + AppImage（Linux のみ） |
| `PublishAndroidApk` | `artifacts/release/` 配下にサイドロード用 APK を出力 |
| `Pack` / `Release` | リリース成果物の集約 / `gh` 経由でのアップロード |

パッケージング時にはバージョンを指定できます（例：`./build.sh PackAppImage --Version 0.1.0`）。

IDE で `Mailtide.slnx` を開くか、`dotnet` で個別プロジェクトをリストア・ビルドすることも可能です。

## 実行（Desktop）

```bash
# 任意 OAuth（Google / Microsoft 個人アカウント）
export MAILTIDE_GOOGLE_OAUTH_CLIENT_ID="your-google-client-id.apps.googleusercontent.com"
export MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID="your-microsoft-public-client-id"

dotnet run --project src/Mailtide.Desktop
```

Windows PowerShell の場合：

```powershell
$env:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID = "your-google-client-id.apps.googleusercontent.com"
$env:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID = "your-microsoft-public-client-id"
dotnet run --project src/Mailtide.Desktop
```

アプリデータは OS のローカルアプリケーションデータフォルダ内の `Mailtide` サブディレクトリに保存されます。

## 実行 / デプロイ（Android）

```bash
# ビルド時に公開 OAuth クライアント ID を APK プロセス環境へ埋め込む
dotnet publish src/Mailtide.Android/Mailtide.Android.csproj \
  -c Release -f net10.0-android \
  -p:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID=your-google-client-id.apps.googleusercontent.com \
  -p:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID=your-microsoft-public-client-id
```

または NUKE を使用：`./build.sh PublishAndroidApk --Version 0.1.0`  
`ANDROID_KEYSTORE_PATH`、`ANDROID_KEYSTORE_PASSWORD`、`ANDROID_KEY_ALIAS`、`ANDROID_KEY_PASSWORD` が設定されている場合はリリース署名を使用し、未設定時はサイドロード用のデフォルト／デバッグ署名を使用します。

## OAuth 設定

Desktop と Android は環境変数から**公開** OAuth クライアント ID を読み取ります（アプリ内に client secret は含めません）。

| 変数 | 用途 |
|------|------|
| `MAILTIDE_GOOGLE_OAUTH_CLIENT_ID` | Google / Gmail OAuth |
| `MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID` | Microsoft 個人向け（Outlook.com / Hotmail / Live）— Entra 職場/学校アカウントは対象外 |
| `MAILTIDE_GITHUB_TOKEN` | Desktop 専用：プライベートリポジトリでも GitHub Releases の更新チェックを動作させるための任意 PAT。このトークンは絶対にコミットしないでください。Android にアプリ内アップデーターはありません。 |

ホスト側の OAuth クライアントに一致するループバック（Desktop）またはカスタムスキーム／インテント（Android）のリダイレクトを持つ公開／ネイティブクライアントを登録してください。これらの変数が未設定の場合、Google および Microsoft アカウントの追加フローは認可時に失敗しますが、QQ Mail と手動 IMAP/SMTP は引き続き動作します。

## 配布

[GitHub Releases](https://github.com/Skymly/Mailtide/releases) からインストールします：

- Windows：`Mailtide-*-win-x64-setup.exe`（Inno Setup）
- Linux：`Mailtide-*-linux-x64.AppImage`
- Android：`Mailtide-*-android.apk`

タグプッシュまたは Release ワークフローによってパッケージングが実行されます（[`.github/workflows/release.yml`](.github/workflows/release.yml) を参照）。

## コントリビューター・エージェント向けドキュメント

| ドキュメント | 役割 |
|--------------|------|
| [`CONTEXT.md`](CONTEXT.md) | 製品の一行説明とドメイン用語集 |
| [`docs/adr/`](docs/adr/) | アーキテクチャ決定記録 |
| [`AGENTS.md`](AGENTS.md) | エージェント入口：Issue、トリアージラベル、ドメインドキュメント |
| [`docs/agents/`](docs/agents/) | Issue トラッカー、トリアージ語彙、ドメインドキュメント構成 |

Issue は GitHub Issues で管理しています。チケットや PR では `CONTEXT.md` の用語集にある表記を優先してください。
