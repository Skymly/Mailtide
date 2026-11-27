# Mailtide

[English](README.md) | **简体中文** | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md)

面向 **Windows**、**Linux** 与 **Android** 的个人、多账户、离线优先电子邮件客户端。

基于 **.NET 10** 与 **Avalonia 12**（Fluent 主题）构建。核心邮件逻辑保持 UI 无关；轻薄的 Desktop 与 Android 宿主提供生命周期管理、安全存储以及 OAuth 系统浏览器登录流程。

[![CI](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml)
[![GitHub Release](https://img.shields.io/github/v/release/Skymly/Mailtide?label=Release)](https://github.com/Skymly/Mailtide/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## 适用人群

适合在同一台设备上管理多个个人邮箱账户、且需要在断网时仍可阅读邮件的用户。Mailtide 是本地客户端（IMAP/SMTP），并非网页邮箱套壳，也不面向企业 / Entra 工作账户。

**v1 平台：** Windows · Linux（Ubuntu 24.04 x64 基线）· Android  
v1 的 Android 版使用共享的桌面 / 平板宽度界面，手机 / 窄屏折叠布局不在范围内。  
**v1 不包含：** macOS · iOS · Microsoft Store / Play Store / Flatpak / deb 分发渠道

## 当前状态

持续开发中——基于本地 IMAP/SMTP、SQLite 存储与本地搜索引擎构建的紧凑型 Fluent 三栏日常界面（Outlook 风格导航栏 | 线程列表 | 阅读/撰写面板）。

可从 [GitHub Releases](https://github.com/Skymly/Mailtide/releases) 下载预构建安装包，或按下方说明从源码构建。除非 Release 打包中已注入公开客户端 ID，否则本地 `dotnet run` 使用 Google/Microsoft 账户时仍需配置 OAuth 环境变量。

## 功能特性

| 领域 | 已交付能力 |
|------|------------|
| 账户 | Google（OAuth）、Microsoft 个人账户 / Outlook.com（OAuth）、QQ 邮箱（预设 + 授权码）、手动 IMAP/SMTP + 密码 / 应用专用密码 |
| 离线 | 全安装共享的 EF Core + SQLite 存储；附件正文落盘；断网状态下可阅读已同步的 Message |
| 同步 | 进程内同步引擎；按 Account 并行同步；草稿 → Outbox → SMTP |
| 界面 | 紧凑型 Fluent 三栏日常界面（左侧 Account/Mailbox 树、Thread 列表、阅读/撰写面板），由 Desktop 与 Android（桌面/平板宽度）共享。支持 Unified Inbox、按 Mailbox 查看 Threads、会话阅读、面板内撰写与草稿、Outbox、HTML 邮件视图、移动/回复与 Mailbox 管理，以及 Outlook 风格 `Ctrl+` 快捷键。 |
| 搜索 | 在当前导航范围内执行本地全文与操作符搜索（状态、附件、邮件头字段、日期、大小、Mailbox 角色与 `-` 取反）。 |
| 安全 | 凭据仅通过操作系统安全存储（Windows DPAPI、Linux libsecret、Android Keystore）保存，无明文回退 |
| 更新 | Desktop 检查 GitHub Releases；Android 通过安装 Releases 中的新版 APK 更新 |

<details>
<summary>支持的本地搜索操作符</summary>

- **状态标记：** `is:unread`、`is:read`、`is:flagged`、`is:starred`、`is:failed`、`has:attachment`（亦支持 `has:attach`、`has:attachments`）
- **邮件字段：** `from:`、`to:`、`cc:`、`bcc:`、`subject:`、`filename:` / `file:`、`in:` / `label:`、`rfc822msgid:` / `msgid:`（`from:me`、`to:me`、`cc:me`、`bcc:me` 匹配当前 Account 地址）
- **日期与大小：** `after:`、`before:`、`on:`、`older_than:`、`newer_than:`、`larger:` / `size:`、`smaller:`
- **Mailbox 范围：** `is:` 与 `in:` 支持角色词（`inbox`、`sent`、`draft` / `drafts`、`trash` / `bin` / `deleted`、`junk` / `spam`、`archive`）以及 `in:anywhere` / `in:all` / `in:any`
- **取反：** 在任意操作符前加 `-`（支持与普通全文关键词混用）

</details>

领域词汇（Account、Person、Message、Mailbox、Unified Inbox、Outbox、Credential）定义于 [`CONTEXT.md`](CONTEXT.md)。

## 仓库结构

```
├── src/
│   ├── Mailtide.Core/       # 存储、同步、IMAP/SMTP、认证编排（无 UI）
│   ├── Mailtide.UI/         # 共享 Avalonia UI（展示与交互意图）
│   ├── Mailtide.Desktop/    # Windows / Linux 宿主
│   └── Mailtide.Android/    # Android 宿主
├── tests/                   # MSTest + Microsoft.Testing.Platform
├── build/                   # NUKE 构建项目
├── packaging/               # Windows Inno Setup · Linux AppImage 打包脚手架
├── docs/adr/                # 架构决策记录
└── Mailtide.slnx
```

参见 [ADR-0001](docs/adr/0001-core-thin-hosts.md)（核心 + 轻薄宿主）、[ADR-0002](docs/adr/0002-ef-core-sqlite-store.md)（SQLite 存储）、[ADR-0003](docs/adr/0003-secure-storage-apis.md)（安全存储）。

## 前置要求

- 与 [`global.json`](global.json) 匹配的 **.NET SDK**（当前为 `10.0.302`，`rollForward: latestFeature`）
- 仅在构建 Android 宿主（`CompileAndroid` / `PublishAndroidApk`）时需要 **Android workload**：

  ```bash
  dotnet workload install android
  ```

  默认的 `Restore` / `Compile` / `Test` 与 Desktop 发布不会还原 Android 宿主。若 `CompileAndroid` 出现 `NETSDK1147`（`wasm-tools`）错误，请运行 `dotnet workload restore` 使 workload 与 `rollForward` 选中的 SDK 保持一致。

- **Linux 桌面凭据存储：** 可用的 Freedesktop Secret Service（libsecret）
- **可选 — OAuth 账户类型：** 公开 OAuth 客户端 ID（参见下方 [OAuth 配置](#oauth-配置)）。QQ 邮箱与手动 IMAP/SMTP 无需配置。

## 克隆、构建与测试

```bash
git clone https://github.com/Skymly/Mailtide.git
cd Mailtide
```

NUKE 入口（推荐；与 CI 使用相同路径）：

```bash
# Linux / bash
./build.sh Test

# Windows
.\build.ps1 Test
# 或: build.cmd Test
```

常用目标：

| 目标 | 用途 |
|------|------|
| `Restore` / `Compile` / `Test` | 还原、构建并运行托管测试项目。不会还原或构建 Android 宿主。默认目标为 `Test`。 |
| `CompileAndroid` | 还原并构建 Android 宿主（需要 Android workload） |
| `PublishDesktopWindows` / `PackWindowsInstaller` | Windows 裁剪单文件发布 + Inno Setup 安装包（仅限 Windows） |
| `PublishDesktopLinux` / `PackAppImage` | Linux 裁剪单文件发布 + AppImage（仅限 Linux） |
| `PublishAndroidApk` | 在 `artifacts/release/` 下生成用于侧载的 APK |
| `Pack` / `Release` | 汇总发布产物 / 通过 `gh` 上传 |

打包时可传入版本号，例如：`./build.sh PackAppImage --Version 0.1.0`。

也可以直接在 IDE 中打开 `Mailtide.slnx`，或使用 `dotnet` 单独还原、构建各个项目。

## 运行（Desktop）

```bash
# 可选 OAuth（Google / Microsoft 个人账户）
export MAILTIDE_GOOGLE_OAUTH_CLIENT_ID="your-google-client-id.apps.googleusercontent.com"
export MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID="your-microsoft-public-client-id"

dotnet run --project src/Mailtide.Desktop
```

在 Windows PowerShell 中：

```powershell
$env:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID = "your-google-client-id.apps.googleusercontent.com"
$env:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID = "your-microsoft-public-client-id"
dotnet run --project src/Mailtide.Desktop
```

应用数据保存在操作系统的本地应用数据目录下的 `Mailtide` 子目录中。

## 运行 / 部署（Android）

```bash
# 在构建期将公开 OAuth 客户端 ID 注入 APK 进程环境
dotnet publish src/Mailtide.Android/Mailtide.Android.csproj \
  -c Release -f net10.0-android \
  -p:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID=your-google-client-id.apps.googleusercontent.com \
  -p:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID=your-microsoft-public-client-id
```

或使用 NUKE：`./build.sh PublishAndroidApk --Version 0.1.0`  
若设置了 `ANDROID_KEYSTORE_PATH`、`ANDROID_KEYSTORE_PASSWORD`、`ANDROID_KEY_ALIAS` 与 `ANDROID_KEY_PASSWORD`，则使用正式签名；否则使用默认调试签名以便侧载测试。

## OAuth 配置

Desktop 与 Android 从环境变量读取**公开** OAuth 客户端 ID（应用中不包含 client secret）：

| 变量 | 用途 |
|------|------|
| `MAILTIDE_GOOGLE_OAUTH_CLIENT_ID` | Google / Gmail OAuth |
| `MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID` | Microsoft 个人账户（Outlook.com / Hotmail / Live）— 不含 Entra 工作/学校账户 |
| `MAILTIDE_GITHUB_TOKEN` | 仅限 Desktop：可选 PAT，用于以认证方式请求 GitHub Releases API（提高速率限制）。切勿提交此 Token。Android 无应用内更新器。 |

请注册与宿主 OAuth 客户端相匹配的环回重定向（Desktop）或自定义 scheme / intent 重定向（Android）的公开 / 原生客户端。未设置这些变量时，添加 Google 与 Microsoft 账户会在授权阶段失败；QQ 邮箱与手动 IMAP/SMTP 仍可正常使用。

## 分发

从 [GitHub Releases](https://github.com/Skymly/Mailtide/releases) 安装：

- Windows：`Mailtide-*-win-x64-setup.exe`（Inno Setup）
- Linux：`Mailtide-*-linux-x64.AppImage`
- Android：`Mailtide-*-android.apk`

由打标签推送或 Release 工作流触发打包（参见 [`.github/workflows/release.yml`](.github/workflows/release.yml)）。

## 贡献者与 Agent 文档

| 文档 | 职责 |
|------|------|
| [`CONTEXT.md`](CONTEXT.md) | 产品一句话介绍与领域词汇表 |
| [`docs/adr/`](docs/adr/) | 架构决策记录 |
| [`AGENTS.md`](AGENTS.md) | Agent 入口：Issue 流程、分诊标签与领域文档 |
| [`docs/agents/`](docs/agents/) | Issue 跟踪器、分诊词汇表与领域文档布局 |

Issue 统一在 GitHub Issues 管理。提交 Issue 与 PR 时请优先使用 `CONTEXT.md` 中的词汇表术语。
