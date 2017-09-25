#tool "nuget:?package=GitVersion.CommandLine"

private Verbosity ParseVerbosity(string verbosity)
{
	Verbosity typedVerbosity;
	if(Enum.TryParse<Verbosity>(verbosity, out typedVerbosity)){
		return typedVerbosity;
	}
	return Verbosity.Minimal;
}

private NuGetVerbosity MapVerbosityToNuGetVerbosity(Verbosity verbosity)
{
	switch(verbosity)
	{
		case Verbosity.Diagnostic:
		case Verbosity.Verbose:
			return NuGetVerbosity.Detailed;
		case Verbosity.Quiet:
			return NuGetVerbosity.Quiet;
		default:
			return NuGetVerbosity.Normal;
	}
}

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "BuildOnCommit");
var configuration = Argument("configuration", "Release");
var verbosity = ParseVerbosity(Argument("verbosity", "Verbose"));
var solution = "../source/NDomain.sln";

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////
var nugetOutputPath = "../nuget/.output";
var nuspecPattern = "../**/NDomain*.nuspec";
var runningOnBuildServer = TeamCity.IsRunningOnTeamCity;
string nugetVersion = null;
GitVersion gitVersion = null;

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
	.Does(() =>
	{
		DotNetBuild(solution,
			settings =>
						settings.SetConfiguration(configuration)
							.SetVerbosity(verbosity)
							.WithTarget("Clean"));
	});

Task("Restore-NuGet-Packages")
	.Does(() =>
	{
    NuGetRestore(solution, new NuGetRestoreSettings
		{
			Verbosity = MapVerbosityToNuGetVerbosity(verbosity)
		});
	});

Task("Build")
  .IsDependentOn("Clean")
  .IsDependentOn("Restore-NuGet-Packages")
	.IsDependentOn("Set-Assembly-Information-Files")
	.Does(() =>
	{
		DotNetBuild(solution,
			settings =>
						settings.SetConfiguration(configuration)
							.SetVerbosity(verbosity)
							.WithTarget("Build"));
	});

Task("Run-Unit-Tests")
	.IsDependentOn("Build")
	.Does(() =>
	{
		NUnit3(string.Format("../**/bin/{0}/*.Tests.dll",configuration), new NUnit3Settings
		{
			Configuration = configuration,
			TeamCity = TeamCity.IsRunningOnTeamCity
		});
	});

Task("Pack-NuGet-Packages")
	.WithCriteria(() => runningOnBuildServer)
	.IsDependentOn("Run-Unit-Tests")
	.IsDependentOn("Get-GitVersion")
	.Does(() =>
	{
		var settings = new NuGetPackSettings
		{
			OutputDirectory = nugetOutputPath,
			Verbosity = MapVerbosityToNuGetVerbosity(verbosity),
			Properties = new Dictionary<string, string>()
			{
				{"Configuration", configuration}
			},
			Version = nugetVersion
		};

		EnsureDirectoryExists(Directory(nugetOutputPath).Path);

		var nuspecs = GetFiles(nuspecPattern);
		foreach(var file in nuspecs)
		{
			NuGetPack(file.ToString(), settings);
		}
	});

Task("Publish-NuGet-Packages")
	.WithCriteria(() => runningOnBuildServer)
	.IsDependentOn("Pack-NuGet-Packages")
	.Does(() =>
	{
		var nugetRepo = Argument<string>("nuget-repo");

		var nugets = GetFiles(nugetOutputPath + "/*.nupkg");
		NuGetPush(nugets, new NuGetPushSettings
		{
			Source = nugetRepo,
			Verbosity = MapVerbosityToNuGetVerbosity(verbosity)
		});
	});

Task("Pack-Local-NuGet-Packages")
	.WithCriteria(() => !runningOnBuildServer)
	.IsDependentOn("Run-Unit-Tests")
	.IsDependentOn("Get-GitVersion")
	.Does(() =>
	{
		var version = "1.0.0.0";
		var nugetLocalDir = Argument<string>("nuget-local-dir", "D:\\BSkyB\\LocalNuGetPackages");

		Information(string.Format("Using version {0} for nuget packages", version));
		Information(string.Format("Deploying packages to {0}", nugetLocalDir));

		var settings = new NuGetPackSettings
		{
			OutputDirectory = nugetLocalDir,
			Verbosity = MapVerbosityToNuGetVerbosity(verbosity),
			Properties = new Dictionary<string, string>()
			{
				{"Configuration", configuration}
			},
			Dependencies = new List<NuSpecDependency>(),
			Version = version,
			DevelopmentDependency = true,
			Symbols = true,
		};

		EnsureDirectoryExists(Directory(nugetLocalDir).Path);

		var nuspecs = GetFiles(nuspecPattern);
		foreach(var file in nuspecs)
		{
			NuGetPack(file.ToString(), settings);
		}
	});

Task("Get-GitVersion")
		.WithCriteria(() => runningOnBuildServer)
		.Does(() => {
			gitVersion = GitVersion(new GitVersionSettings
			{
				UpdateAssemblyInfo = false,
				NoFetch = true,
				WorkingDirectory = "../"
			});

			Information("AssemblySemVer: {0}", gitVersion.AssemblySemVer);
			Information("MajorMinorPatch: {0}", gitVersion.MajorMinorPatch);
			Information("NuGetVersionV2: {0}", gitVersion.NuGetVersionV2);
			Information("FullSemVer: {0}", gitVersion.FullSemVer);
			Information("BranchName: {0}", gitVersion.BranchName);
			Information("Sha: {0}", gitVersion.Sha);

			nugetVersion = string.Format(
				"{0}.{1}",
				gitVersion.MajorMinorPatch,
				string.IsNullOrWhiteSpace(gitVersion.BuildMetaDataPadded)
					? "00000" // this is 5 zeros because GitVersion.yml defines a padding of 5 digits for the BuildMetaDataPadded field
					: gitVersion.BuildMetaDataPadded);

			if(runningOnBuildServer)
			{
				TeamCity.SetBuildNumber(nugetVersion);
			}
		});

Task("Set-Assembly-Information-Files")
	.WithCriteria(() => runningOnBuildServer)
	.IsDependentOn("Get-GitVersion")
	.Does(() => {

		var assemblyInfos = GetFiles("../**/Properties/AssemblyInfo.cs");
		foreach(var assemblyInfoPath in assemblyInfos)
		{
			Information(string.Format("Found assembly info in {0}", assemblyInfoPath.FullPath));
			var assemblyInfo = ParseAssemblyInfo(assemblyInfoPath);

			var assemblyInfoSettings = new AssemblyInfoSettings {
				Title = assemblyInfo.Title,
				Description = assemblyInfo.Description,
				Company = assemblyInfo.Company,
				Product = assemblyInfo.Product,
				Version = nugetVersion,
				FileVersion = nugetVersion,
				Guid = assemblyInfo.Guid,
				ComVisible = assemblyInfo.ComVisible,
				Trademark = assemblyInfo.Trademark,
				Copyright = string.Format("Copyright © BSkyB {0}", DateTime.Now.Year)
			};

			if(assemblyInfo.InternalsVisibleTo != null && assemblyInfo.InternalsVisibleTo.Any()){
				assemblyInfoSettings.InternalsVisibleTo = assemblyInfo.InternalsVisibleTo;
			}

			CreateAssemblyInfo(assemblyInfoPath, assemblyInfoSettings);
		}
	});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("BuildOnCommit")
	.IsDependentOn("Pack-Local-NuGet-Packages")
	.IsDependentOn("Publish-NuGet-Packages");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
