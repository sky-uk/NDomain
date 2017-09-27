#tool "nuget:?package=GitVersion.CommandLine";
#tool "nuget:?package=NUnit.ConsoleRunner";
#addin "nuget:?package=Cake.XdtTransform";
#addin "nuget:?package=Cake.SqlServer";
#addin "nuget:?package=Cake.FileHelpers";

public void PrintUsage()
{
	Console.WriteLine($"Usage: build.cake [options]{Environment.NewLine}" +
								$"Options:{Environment.NewLine}" +
								$"\t-target\t\t\t\tCake build entry point.\tDefaults to 'BuildOnCommit'.{Environment.NewLine}" +
								$"\t-configuration\t\t\tBuild configuration [Debug|Release]. Defaults to 'Release'.{Environment.NewLine}" +
								$"\t-verbosity\t\t\tVerbosity [Quiet|Minimal|Normal|Verbose|Diagnostic]. Defaults to 'Minimal'.{Environment.NewLine}" +
								$"\t-nuget-repo\t\t\tThe Nuget repo to publish to. Mandatory for 'BuildOnCommit' target.{Environment.NewLine}" +
								$"\t-ci-database-host\tThe CI DB connection string to use when setting up the database for tests to run. Defaults to local connection string{Environment.NewLine}" +
								$"\t-event-store-database\t\tThe SQL database to be created and used by the event store tests. Defaults to 'NDomain'{Environment.NewLine}" +
								$"\t-event-store-user\t\tThe SQL database user to be created and used by the event store tests. Defaults to 'ndomain'{Environment.NewLine}" +
								$"\t-event-store-password\t\tThe SQL database user password to be associated with the user. Defaults to 'ndomain'{Environment.NewLine}" +
								$"\t-branch\t\tThe branch being built. Required{Environment.NewLine}");
}

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
var eventStoreUser = Argument("event-store-user", "ndomain");
var eventStorePassword = Argument("event-store-pass", "ndomain");
var eventStoreDatabase = Argument("event-store-db", "NDomain");
var ciDatabaseHost = Argument("ci-database-host", ".\\SQL2012");

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////
var ciDatabaseConnectionString = $"Data Source={ciDatabaseHost}; Integrated Security=SSPI";
var eventStoreConnectionString = $"Data Source={ciDatabaseHost}; Initial Catalog={eventStoreDatabase}; User Id={eventStoreUser}; Password={eventStorePassword}";
var solution = "../source/NDomain.sln";
var nugetOutputPath = "../nuget/.output";
var nuspecPattern = "../**/NDomain*.nuspec";
var runningOnBuildServer = TeamCity.IsRunningOnTeamCity;
string nugetVersion = null;
string assemblyVersion = null;
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

Task("Run-Tests")
	.IsDependentOn("Build")
	.IsDependentOn("Transform-Files")
	.IsDependentOn("Set-Up-Test-Database")
	.Does(() =>
	{
		NUnit3($"../**/bin/{configuration}/*.Tests.dll", new NUnit3Settings
		{
			Configuration = configuration,
			TeamCity = TeamCity.IsRunningOnTeamCity
		});
	});

Task("Set-Up-Test-Database")
	.Does(() =>
	{
		CreateDatabaseIfNotExists(ciDatabaseConnectionString, eventStoreDatabase);
		ReplaceTextInFiles("./SetUpTests.sql", "%_USER_NAME_%", eventStoreUser);
		ReplaceTextInFiles("./SetUpTests.sql", "%_USER_PASSWORD_%", eventStorePassword);
		ReplaceTextInFiles("./SetUpTests.sql", "%_EVENT_STORE_DB_%", eventStoreDatabase);
		ExecuteSqlFile(ciDatabaseConnectionString, "./SetUpTests.sql");
	});

Task("Tear-Down-Test-Database")
	.IsDependentOn("Set-Up-Test-Database")
	.IsDependentOn("Run-Tests")
	.Does(() =>
	{
		DropDatabase(ciDatabaseConnectionString, eventStoreDatabase);
		ReplaceTextInFiles("./TearDownTests.sql", "%_USER_NAME_%", eventStoreUser);
		ReplaceTextInFiles("./TearDownTests.sql", "%_EVENT_STORE_DB_%", eventStoreDatabase);
		ExecuteSqlFile(ciDatabaseConnectionString, "./TearDownTests.sql");

		ReplaceTextInFiles("./SetUpTests.sql", eventStoreUser, "%_USER_NAME_%");
		ReplaceTextInFiles("./SetUpTests.sql", eventStorePassword, "%_USER_PASSWORD_%");
		ReplaceTextInFiles("./SetUpTests.sql", eventStoreDatabase, "%_EVENT_STORE_DB_%");
		ReplaceTextInFiles("./TearDownTests.sql", eventStoreUser, "%_USER_NAME_%");
		ReplaceTextInFiles("./TearDownTests.sql", eventStoreDatabase, "%_EVENT_STORE_DB_%");
	});

Task("Pack-NuGet-Packages")
	.WithCriteria(() => runningOnBuildServer)
	.IsDependentOn("Tear-Down-Test-Database")
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
			Version = nugetVersion,

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
	.IsDependentOn("Tear-Down-Test-Database")
	.IsDependentOn("Get-GitVersion")
	.Does(() =>
	{
		var version = "1.0.0.0";
		var nugetLocalDir = Argument<string>("nuget-local-dir", "D:\\BSkyB\\LocalNuGetPackages");

		Information($"Using version {version} for nuget packages");
		Information($"Deploying packages to {nugetLocalDir}");

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

			Information($"AssemblySemVer: {gitVersion.AssemblySemVer}{Environment.NewLine}"+
									$"SemVer: {gitVersion.AssemblySemVer}{Environment.NewLine}" +
									$"FullSemVer: {gitVersion.FullSemVer}{Environment.NewLine}" +
									$"MajorMinorPatch: {gitVersion.MajorMinorPatch}{Environment.NewLine}" +
									$"NuGetVersionV2: {gitVersion.NuGetVersionV2}{Environment.NewLine}" +
									$"NuGetVersion: {gitVersion.NuGetVersion}{Environment.NewLine}" +
									$"BranchName: {gitVersion.BranchName}{Environment.NewLine}" +
									$"Sha: {gitVersion.Sha}{Environment.NewLine}" +
									$"Pre-Release Label: {gitVersion.PreReleaseLabel}{Environment.NewLine}" +
									$"Pre-Release Number: {gitVersion.PreReleaseNumber}{Environment.NewLine}" +
									$"Pre-Release Tag: {gitVersion.PreReleaseTag}{Environment.NewLine}" +
									$"Pre-Release Tag with dash: {gitVersion.PreReleaseTagWithDash}{Environment.NewLine}" +
									$"Build MetaData: {gitVersion.BuildMetaData}{Environment.NewLine}" +
									$"Build MetaData Padded: {gitVersion.BuildMetaDataPadded}{Environment.NewLine}" +
									$"Full Build MetaData: {gitVersion.FullBuildMetaData}{Environment.NewLine}");

			if(string.IsNullOrWhiteSpace(gitVersion.PreReleaseTagWithDash))
			{
				Information("No Pre-Release tag found. Versioning as a Release...");
			}

			nugetVersion = $"{gitVersion.MajorMinorPatch}.{gitVersion.BuildMetaDataPadded}";
			assemblyVersion = gitVersion.AssemblySemVer;

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
			Information($"Found assembly info in {assemblyInfoPath.FullPath}");
			var assemblyInfo = ParseAssemblyInfo(assemblyInfoPath);

			var assemblyInfoSettings = new AssemblyInfoSettings {
				Title = assemblyInfo.Title,
				Description = assemblyInfo.Description,
				Company = assemblyInfo.Company,
				Product = assemblyInfo.Product,
				Version = assemblyVersion,
				FileVersion = assemblyVersion,
				InformationalVersion = nugetVersion,
				Guid = assemblyInfo.Guid,
				ComVisible = assemblyInfo.ComVisible,
				Trademark = assemblyInfo.Trademark,
				Copyright = $"Copyright © BSkyB {DateTime.Now.Year}"
			};

			if(assemblyInfo.InternalsVisibleTo != null && assemblyInfo.InternalsVisibleTo.Any()){
				assemblyInfoSettings.InternalsVisibleTo = assemblyInfo.InternalsVisibleTo;
			}

			CreateAssemblyInfo(assemblyInfoPath, assemblyInfoSettings);
		}
	});

Task("Transform-Files")
		.IsDependentOn("Build")
		.WithCriteria(() => runningOnBuildServer)
		.Does(() =>
		{
			var files = GetFiles("../**/TeamCityTransform.config");
			foreach(var f in files)
			{
				var path = f.GetDirectory().FullPath;
				var transformFileName = f.FullPath;
				var sourceFileName = $"{path}/bin/{configuration}/{f.GetDirectory().Segments.Last()}.dll.config";

				var sourceFile      = File(sourceFileName);
				var transformFile   = File(transformFileName);
				var targetFile      = File(sourceFileName);
				XdtTransformConfig(sourceFile, transformFile, targetFile);

				ReplaceTextInFiles(sourceFileName, "%%event-store-connection-string%%", eventStoreConnectionString);

				Information($"Transformed {sourceFileName}{Environment.NewLine}");
			}

			Information($"Connection strings:{Environment.NewLine}EventStore:{eventStoreConnectionString}{Environment.NewLine}");
		});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("BuildOnCommit")
	.IsDependentOn("Pack-Local-NuGet-Packages")
	.IsDependentOn("Publish-NuGet-Packages")
	.OnError(exception =>
	{
		PrintUsage();
	});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////
if (target=="Help")
{
	PrintUsage();
}
else
{
	RunTarget(target);
}
