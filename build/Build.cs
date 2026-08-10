using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Release version (tag like v1.2.3 or plain 1.2.3). Defaults to 0.0.0-local.")]
    readonly string Version = ReleaseArtifacts.LocalFallbackVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath PackagingDirectory => RootDirectory / "packaging";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath DesktopPublishWin => ArtifactsDirectory / "desktop" / "win-x64";
    AbsolutePath DesktopPublishLinux => ArtifactsDirectory / "desktop" / "linux-x64";
    AbsolutePath ReleaseDirectory => ArtifactsDirectory / "release";
    AbsolutePath AppDir => ArtifactsDirectory / "appdir";

    AbsolutePath CoreProject => SourceDirectory / "Mailtide.Core" / "Mailtide.Core.csproj";
    AbsolutePath UiProject => SourceDirectory / "Mailtide.UI" / "Mailtide.UI.csproj";
    AbsolutePath CoreTestsProject => TestsDirectory / "Mailtide.Core.Tests" / "Mailtide.Core.Tests.csproj";
    AbsolutePath DesktopTestsProject => TestsDirectory / "Mailtide.Desktop.Tests" / "Mailtide.Desktop.Tests.csproj";
    AbsolutePath AndroidTestsProject => TestsDirectory / "Mailtide.Android.Tests" / "Mailtide.Android.Tests.csproj";
    AbsolutePath BuildTestsProject => TestsDirectory / "Mailtide.Build.Tests" / "Mailtide.Build.Tests.csproj";
    AbsolutePath DesktopHostProject => SourceDirectory / "Mailtide.Desktop" / "Mailtide.Desktop.csproj";
    AbsolutePath AndroidHostProject => SourceDirectory / "Mailtide.Android" / "Mailtide.Android.csproj";

    // Solution restore of net10.0-android on Linux requests the deprecated
    // Microsoft.NETCore.App.Runtime.Mono.linux-x64 pack (NU1102). Pin an Android RID
    // when restoring/building the host on Linux so NuGet resolves Mono.android-* packs.
    const string AndroidLinuxRuntimeIdentifier = "android-arm64";

    AbsolutePath[] ManagedProjectsExceptAndroidHost =>
    [
        CoreProject,
        UiProject,
        DesktopHostProject,
        CoreTestsProject,
        DesktopTestsProject,
        AndroidTestsProject,
        BuildTestsProject
    ];

    string NormalizedVersion => ReleaseArtifacts.NormalizeVersion(Version);

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            TestsDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            ArtifactsDirectory.DeleteDirectory();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            foreach (var project in ManagedProjectsExceptAndroidHost)
            {
                DotNetRestore(s => s.SetProjectFile(project));
            }

            RestoreAndroidHost();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            foreach (var project in ManagedProjectsExceptAndroidHost)
            {
                DotNetBuild(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore());
            }

            BuildAndroidHost();
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Run net10.0 test projects explicitly. The Android host (net10.0-android) is
            // compiled via Compile but is not itself a test assembly.
            foreach (var project in new[]
                     {
                         CoreTestsProject,
                         DesktopTestsProject,
                         AndroidTestsProject,
                         BuildTestsProject
                     })
            {
                DotNetTest(s => s
                    .SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .EnableNoRestore()
                    .EnableNoBuild());
            }
        });

    Target CompileAndroid => _ => _
        .DependsOn(Restore)
        .Executes(BuildAndroidHost);

    Target PublishDesktopWindows => _ => _
        .DependsOn(Restore)
        .OnlyWhenStatic(() => OperatingSystem.IsWindows())
        .Executes(() =>
        {
            DesktopPublishWin.CreateDirectory();
            DotNetPublish(s => s
                .SetProject(DesktopHostProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("win-x64")
                .SetSelfContained(true)
                .SetOutput(DesktopPublishWin)
                .SetProperty("PublishTrimmed", true)
                .SetProperty("Version", NormalizedVersion)
                .SetProperty("InformationalVersion", NormalizedVersion));
        });

    Target PackWindowsInstaller => _ => _
        .DependsOn(PublishDesktopWindows)
        .OnlyWhenStatic(() => OperatingSystem.IsWindows())
        .Executes(() =>
        {
            ReleaseDirectory.CreateDirectory();
            var iss = PackagingDirectory / "windows" / "Mailtide.iss";
            Assert.FileExists(iss);

            var iscc = FindInnoSetupCompiler();
            var outputName = ReleaseArtifacts.WindowsInstallerFileName(NormalizedVersion);
            ProcessTasks.StartProcess(
                    iscc,
                    $"\"{iss}\" /DMyAppVersion={NormalizedVersion} /DPublishDir=\"{DesktopPublishWin}\" /DOutputDir=\"{ReleaseDirectory}\" /DOutputName=\"{Path.GetFileNameWithoutExtension(outputName)}\"",
                    logOutput: true)
                .AssertZeroExitCode();

            var produced = ReleaseDirectory / outputName;
            Assert.FileExists(produced);
        });

    Target PublishDesktopLinux => _ => _
        .DependsOn(Restore)
        .OnlyWhenStatic(() => OperatingSystem.IsLinux())
        .Executes(() =>
        {
            DesktopPublishLinux.CreateDirectory();
            DotNetPublish(s => s
                .SetProject(DesktopHostProject)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("linux-x64")
                .SetSelfContained(true)
                .SetOutput(DesktopPublishLinux)
                .SetProperty("PublishTrimmed", true)
                .SetProperty("Version", NormalizedVersion)
                .SetProperty("InformationalVersion", NormalizedVersion));
        });

    Target PackAppImage => _ => _
        .DependsOn(PublishDesktopLinux)
        .OnlyWhenStatic(() => OperatingSystem.IsLinux())
        .Executes(() =>
        {
            ReleaseDirectory.CreateDirectory();
            AppDir.DeleteDirectory();
            var appBin = AppDir / "usr" / "bin";
            appBin.CreateDirectory();
            (AppDir / "usr" / "share" / "icons" / "hicolor" / "256x256" / "apps").CreateDirectory();

            foreach (var file in DesktopPublishLinux.GetFiles("*", 10))
            {
                var relative = Path.GetRelativePath(DesktopPublishLinux, file);
                var destination = appBin / relative;
                destination.Parent.CreateDirectory();
                File.Copy(file, destination, overwrite: true);
            }

            var binary = appBin / "Mailtide.Desktop";
            if (!binary.FileExists())
            {
                // Fallback: first executable without extension in publish output.
                var fallback = appBin.GetFiles("Mailtide*", 1).FirstOrDefault(f => !f.ToString().EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                         ?? throw new FileNotFoundException("Published Desktop binary not found for AppImage.");
                // AppRun and mailtide.desktop always exec usr/bin/Mailtide.Desktop.
                File.Copy(fallback, binary, overwrite: true);
            }

            File.Copy(PackagingDirectory / "linux" / "AppRun", AppDir / "AppRun", overwrite: true);
            File.Copy(PackagingDirectory / "linux" / "mailtide.desktop", AppDir / "mailtide.desktop", overwrite: true);

            var iconSource = SourceDirectory / "Mailtide.Android" / "Icon.png";
            if (iconSource.FileExists())
            {
                File.Copy(iconSource, AppDir / "mailtide.png", overwrite: true);
                File.Copy(iconSource, AppDir / "usr" / "share" / "icons" / "hicolor" / "256x256" / "apps" / "mailtide.png", overwrite: true);
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                ProcessTasks.StartProcess("chmod", $"+x \"{AppDir / "AppRun"}\" \"{binary}\"", logOutput: true)
                    .AssertZeroExitCode();
            }

            var tool = EnsureAppImageTool();
            var output = ReleaseDirectory / ReleaseArtifacts.AppImageFileName(NormalizedVersion);
            if (output.FileExists())
            {
                output.DeleteFile();
            }

            Environment.SetEnvironmentVariable("ARCH", "x86_64");
            Environment.SetEnvironmentVariable("APPIMAGE_EXTRACT_AND_RUN", "1");
            ProcessTasks.StartProcess(
                    tool,
                    $"--appimage-extract-and-run \"{AppDir}\" \"{output}\"",
                    logOutput: true)
                .AssertZeroExitCode();

            Assert.FileExists(output);
        });

    Target PublishAndroidApk => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            ReleaseDirectory.CreateDirectory();
            var publishOut = ArtifactsDirectory / "android";
            publishOut.CreateDirectory();

            var publishSettings = new DotNetPublishSettings()
                .SetProject(AndroidHostProject)
                .SetConfiguration(Configuration.Release)
                .SetFramework("net10.0-android")
                .SetOutput(publishOut)
                .SetProperty("AndroidPackageFormat", "apk")
                .SetProperty("ApplicationDisplayVersion", NormalizedVersion)
                .SetProperty("ApplicationVersion", ReleaseArtifacts.AndroidVersionCode(NormalizedVersion).ToString());

            if (OperatingSystem.IsLinux())
            {
                publishSettings = publishSettings.SetProperty("RuntimeIdentifier", AndroidLinuxRuntimeIdentifier);
            }

            publishSettings = ApplyAndroidSigning(publishSettings);

            DotNetPublish(publishSettings);

            var apk = publishOut.GlobFiles("**/*.apk").OrderByDescending(f => f.ToString().Length).FirstOrDefault()
                      ?? throw new FileNotFoundException("Published Android APK not found.");

            var destination = ReleaseDirectory / ReleaseArtifacts.AndroidApkFileName(NormalizedVersion);
            File.Copy(apk, destination, overwrite: true);
            Assert.FileExists(destination);
        });

    Target Pack => _ => _
        .DependsOn(PackWindowsInstaller, PackAppImage, PublishAndroidApk)
        .Executes(() =>
        {
            Serilog.Log.Information("Release artifacts for {Version} in {Dir}", NormalizedVersion, ReleaseDirectory);
            foreach (var file in ReleaseDirectory.GetFiles("*"))
            {
                Serilog.Log.Information("  {File}", file.Name);
            }
        });

    Target Release => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
            var tag = NormalizedVersion.StartsWith('v') ? NormalizedVersion : $"v{NormalizedVersion}";
            var assets = ReleaseDirectory.GetFiles("*").Select(f => f.ToString()).ToArray();
            if (assets.Length == 0)
            {
                throw new InvalidOperationException("No release assets to upload.");
            }

            var gh = ToolPathResolver.GetPathExecutable("gh");
            var existing = ProcessTasks.StartProcess(gh, $"release view {tag}", logOutput: false, logInvocation: false);
            existing.WaitForExit();

            if (existing.ExitCode != 0)
            {
                ProcessTasks.StartProcess(
                        gh,
                        $"release create {tag} {string.Join(' ', assets.Select(a => $"\"{a}\""))} --title \"Mailtide {NormalizedVersion}\" --generate-notes",
                        logOutput: true)
                    .AssertZeroExitCode();
            }
            else
            {
                ProcessTasks.StartProcess(
                        gh,
                        $"release upload {tag} {string.Join(' ', assets.Select(a => $"\"{a}\""))} --clobber",
                        logOutput: true)
                    .AssertZeroExitCode();
            }
        });

    void RestoreAndroidHost()
    {
        DotNetRestore(s =>
        {
            s = s.SetProjectFile(AndroidHostProject);
            if (OperatingSystem.IsLinux())
            {
                s = s.SetProperty("RuntimeIdentifier", AndroidLinuxRuntimeIdentifier);
            }

            return s;
        });
    }

    void BuildAndroidHost()
    {
        DotNetBuild(s =>
        {
            s = s
                .SetProjectFile(AndroidHostProject)
                .SetConfiguration(Configuration)
                .EnableNoRestore();
            if (OperatingSystem.IsLinux())
            {
                s = s.SetProperty("RuntimeIdentifier", AndroidLinuxRuntimeIdentifier);
            }

            return s;
        });
    }

    DotNetPublishSettings ApplyAndroidSigning(DotNetPublishSettings settings)
    {
        var keystore = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
        var keystorePassword = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASSWORD");
        var keyAlias = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS");
        var keyPassword = Environment.GetEnvironmentVariable("ANDROID_KEY_PASSWORD");

        if (string.IsNullOrWhiteSpace(keystore) ||
            string.IsNullOrWhiteSpace(keystorePassword) ||
            string.IsNullOrWhiteSpace(keyAlias) ||
            string.IsNullOrWhiteSpace(keyPassword))
        {
            Serilog.Log.Warning("Android signing secrets not set; publishing with default/debug signing for sideload.");
            return settings;
        }

        return settings
            .SetProperty("AndroidKeyStore", "true")
            .SetProperty("AndroidSigningKeyStore", keystore)
            .SetProperty("AndroidSigningStorePass", keystorePassword)
            .SetProperty("AndroidSigningKeyAlias", keyAlias)
            .SetProperty("AndroidSigningKeyPass", keyPassword);
    }

    static string FindInnoSetupCompiler()
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Inno Setup 6",
            "ISCC.exe");

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("INNO_SETUP_ISCC"),
            localPrograms,
            @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            @"C:\Program Files\Inno Setup 6\ISCC.exe"
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        try
        {
            return ToolPathResolver.GetPathExecutable("ISCC");
        }
        catch
        {
            throw new FileNotFoundException(
                "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or set INNO_SETUP_ISCC.");
        }
    }

    AbsolutePath EnsureAppImageTool()
    {
        var tools = ArtifactsDirectory / "tools";
        tools.CreateDirectory();
        var tool = tools / "appimagetool-x86_64.AppImage";
        if (!tool.FileExists())
        {
            var url = "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage";
            HttpTasks.HttpDownloadFile(url, tool);
            ProcessTasks.StartProcess("chmod", $"+x \"{tool}\"", logOutput: true).AssertZeroExitCode();
        }

        return tool;
    }
}
