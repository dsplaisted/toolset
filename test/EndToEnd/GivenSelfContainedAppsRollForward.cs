﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.DotNet.TestFramework;
using Microsoft.DotNet.Tools.Test.Utilities;
using System.Runtime.InteropServices;
using NuGet.ProjectModel;
using NuGet.Versioning;
using Xunit;

namespace EndToEnd
{
    public class GivenSelfContainedAppsRollForward : TestBase
    {

        [Theory(Skip = "Runtime 1.1 support for openSUSE and Fedora 27 needed")]
        //  MemberData is used instead of InlineData here so we can access it in another test to
        //  verify that we are covering the latest release of .NET Core
        [MemberData(nameof(SupportedNetCoreAppVersions))]
        public void ItRollsForwardToTheLatestVersion(string minorVersion)
        {
            var _testInstance = TestAssets.Get("TestAppSimple")
                .CreateInstance(identifier: minorVersion)
                .WithSourceFiles();

            string projectDirectory = _testInstance.Root.FullName;

            string projectPath = Path.Combine(projectDirectory, "TestAppSimple.csproj");

            var project = XDocument.Load(projectPath);
            var ns = project.Root.Name.Namespace;

            //  Update TargetFramework to the right version of .NET Core
            project.Root.Element(ns + "PropertyGroup")
                .Element(ns + "TargetFramework")
                .Value = "netcoreapp" + minorVersion;

            var rid = Microsoft.DotNet.PlatformAbstractions.RuntimeEnvironment.GetRuntimeIdentifier();

            //  Set RuntimeIdentifier to opt in to roll-forward behavior
            project.Root.Element(ns + "PropertyGroup")
                .Add(new XElement(ns + "RuntimeIdentifier", rid));

            project.Save(projectPath);

            //  Get the version rolled forward to
            new RestoreCommand()
                    .WithWorkingDirectory(projectDirectory)
                    .Execute()
                    .Should().Pass();

            string assetsFilePath = Path.Combine(projectDirectory, "obj", "project.assets.json");
            var assetsFile = new LockFileFormat().Read(assetsFilePath);

            var rolledForwardVersion = GetNetCoreAppVersion(assetsFile);
            rolledForwardVersion.Should().NotBeNull();

            if (rolledForwardVersion.IsPrerelease)
            {
                //  If this version of .NET Core is still prerelease, then:
                //  - Floating the patch by adding ".*" to the major.minor version won't work, but
                //  - There aren't any patches to roll-forward to, so we skip testing this until the version
                //    leaves prerelease.
                return;
            }

            //  Float the RuntimeFrameworkVersion to get the latest version of the runtime available from feeds
            Directory.Delete(Path.Combine(projectDirectory, "obj"), true);
            project.Root.Element(ns + "PropertyGroup")
                .Add(new XElement(ns + "RuntimeFrameworkVersion", $"{minorVersion}.*"));
            project.Save(projectPath);

            new RestoreCommand()
                    .WithWorkingDirectory(projectDirectory)
                    .Execute()
                    .Should().Pass();

            var floatedAssetsFile = new LockFileFormat().Read(assetsFilePath);

            var floatedVersion = GetNetCoreAppVersion(floatedAssetsFile);
            floatedVersion.Should().NotBeNull();

            rolledForwardVersion.ToNormalizedString().Should().BeEquivalentTo(floatedVersion.ToNormalizedString(),
                "the latest patch version properties in Microsoft.NETCoreSdk.BundledVersions.props need to be updated " + 
                "(see MSBuildExtensions.targets in this repo)");
        }

        private NuGetVersion GetNetCoreAppVersion(LockFile lockFile)
        {
            return lockFile?.Targets?.SingleOrDefault(t => t.RuntimeIdentifier != null)
                ?.Libraries?.SingleOrDefault(l =>
                    string.Compare(l.Name, "Microsoft.NETCore.App", StringComparison.CurrentCultureIgnoreCase) == 0)
                ?.Version;
        }

        [Fact(Skip="No templates")]
        public void WeCoverLatestNetCoreAppRollForward()
        {
            //  Run "dotnet new console", get TargetFramework property, and make sure it's covered in SupportedNetCoreAppVersions
            using (DisposableDirectory directory = Temp.CreateDirectory())
            {
                string projectDirectory = directory.Path;

                new NewCommandShim()
                    .WithWorkingDirectory(projectDirectory)
                    .Execute("console --no-restore")
                    .Should().Pass();

                string projectPath = Path.Combine(projectDirectory, Path.GetFileName(projectDirectory) + ".csproj");

                var project = XDocument.Load(projectPath);
                var ns = project.Root.Name.Namespace;

                string targetFramework = project.Root.Element(ns + "PropertyGroup")
                    .Element(ns + "TargetFramework")
                    .Value;

                SupportedNetCoreAppVersions.Select(v => $"netcoreapp{v[0]}")
                    .Should().Contain(targetFramework, $"the {nameof(SupportedNetCoreAppVersions)} property should include the default version " +
                    "of .NET Core created by \"dotnet new\"");
                
            }
        }

        public static IEnumerable<object[]> SupportedNetCoreAppVersions
        {
            get
            {
                var versions = new List<string>();

                // Runtime 1.x deosn't support openSUSE and Fedora 27, so skip testing those versions on Linux
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {                    
                    versions.AddRange(new[]
                    {
                        "1.0",
                        "1.1",
                    });
                }

                versions.AddRange(new[]
                    {
                        "2.0",
                        "2.1",
                        "3.0"
                    });

                return versions.Select(version => new object[] { version });
            }
        }
    }
}
