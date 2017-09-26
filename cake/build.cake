#tool "nuget:?package=GitVersion.CommandLine";
#tool "nuget:?package=NUnit.ConsoleRunner";
#addin "nuget:?package=Cake.XdtTransform";
#addin "nuget:?package=Cake.SqlServer";
#addin "nuget:?package=Cake.FileHelpers";

public void PrintUsage()
{
	Console.WriteLine(string.Format("Usage: build.cake [options]{0}" +
								"Options:{0}" +
								"\t-target\t\t\t\tCake build entry point.\tDefaults to 'BuildOnCommit'.{0}" +
								"\t-configuration\t\t\tBuild configuration [Debug|Release]. Defaults to 'Release'.{0}" +
								"\t-verbosity\t\t\tVerbosity [Quiet|Minimal|Normal|Verbose|Diagnostic]. Defaults to 'Minimal'.{0}" +
								"\t-nuget-repo\t\t\tThe Nuget repo to publish to. Mandatory for 'BuildOnCommit' target.{0}" +
								"\t-ci-database-host\tThe CI DB connection string to use when setting up the database for tests to run. Defaults to local connection string{0}" +
								"\t-event-store-database\t\tThe SQL database to be created and used by the event store tests. Defaults to 'NDomain'{0}" +
								"\t-event-store-user\t\tThe SQL database user to be created and used by the event store tests. Defaults to 'ndomain'{0}" +
								"\t-event-store-password\t\tThe SQL database user password to be associated with the user. Defaults to 'ndomain'{0}" +
								"\t-branch\t\tThe branch being built. Required{0}"
								, Environment.NewLine));
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
	.IsDependentOn("Set-Up-Test-Database")
	.Does(() =>
	{
		NUnit3(string.Format("../**/bin/{0}/*.Tests.dll",configuration), new NUnit3Settings
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

			if(!string.IsNullOrWhiteSpace(gitVersion.PreReleaseTagWithDash))
			{
				Information("Pre-Release Label: {0}", gitVersion.PreReleaseLabel);
				Information("Pre-Release Number: {0}", gitVersion.PreReleaseNumber);
				Information("Pre-Release Tag: {0}", gitVersion.PreReleaseTag);
				Information("Pre-Release Tag with dash: {0}", gitVersion.PreReleaseTagWithDash);
			}
			else
			{
				Information("No Pre-Release tag found. Versioning as a Release...");
			}

			nugetVersion = string.Format(
				"{0}.{1}{2}",
				gitVersion.MajorMinorPatch,
				string.IsNullOrWhiteSpace(gitVersion.BuildMetaDataPadded)
					? "00000" // this is 5 zeros because GitVersion.yml defines a padding of 5 digits for the BuildMetaDataPadded field
					: gitVersion.BuildMetaDataPadded,
					gitVersion.PreReleaseTagWithDash);

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
				var sourceFileName = String.Format("{0}/bin/{1}/{2}.dll.config", path, configuration, f.GetDirectory().Segments.Last());

				var sourceFile      = File(sourceFileName);
				var transformFile   = File(transformFileName);
				var targetFile      = File(sourceFileName);
				XdtTransformConfig(sourceFile, transformFile, targetFile);

				ReplaceTextInFiles(sourceFileName, "%%event-store-connection-string%%", eventStoreConnectionString);

				Information(String.Format("Transformed {0}{1}", sourceFileName, Environment.NewLine));
			}

			Information(String.Format("Connection strings:{1}EventStore:{0}{1}", eventStoreConnectionString, Environment.NewLine));
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
