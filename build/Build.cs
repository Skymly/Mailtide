using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

sealed class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution("Mailtide.slnx")]
    readonly Solution Solution = null!;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";

    AbsolutePath CoreTestsProject => TestsDirectory / "Mailtide.Core.Tests" / "Mailtide.Core.Tests.csproj";
    AbsolutePath DesktopTestsProject => TestsDirectory / "Mailtide.Desktop.Tests" / "Mailtide.Desktop.Tests.csproj";
    AbsolutePath AndroidTestsProject => TestsDirectory / "Mailtide.Android.Tests" / "Mailtide.Android.Tests.csproj";
    AbsolutePath AndroidHostProject => SourceDirectory / "Mailtide.Android" / "Mailtide.Android.csproj";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
            TestsDirectory.GlobDirectories("**/bin", "**/obj").DeleteDirectories();
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Run net10.0 test projects explicitly. The Android host (net10.0-android) is
            // compiled via Compile but is not itself a test assembly.
            foreach (var project in new[] { CoreTestsProject, DesktopTestsProject, AndroidTestsProject })
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
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(AndroidHostProject)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });
}
