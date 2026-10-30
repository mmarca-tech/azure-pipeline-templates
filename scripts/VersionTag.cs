#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../src/ReleaseTools/ReleaseTools.csproj

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using PipelineTools.ReleaseTools;

return await VersionTagProgram.RunAsync(args);

static class VersionTagProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = CliOptions.Parse(args.Skip(1));

        try
        {
            return command switch
            {
                "validate" => await ValidateRepositoryAsync(options),
                "create-version-tag" => await CreateVersionTagAsync(options),
                "detect-bump" => await DetectBumpAsync(options),
                _ => throw new CliException($"Unknown version-tag command '{args[0]}'.")
            };
        }
        catch (CliException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> DetectBumpAsync(CliOptions options)
    {
        var tagPrefix = options.Get("TagPrefix", "v")!;
        var defaultBump = options.Get("DefaultBump", "patch")!;
        var remoteName = options.Get("RemoteName", "origin")!;
        var (bumpType, latestTag, commitCount) = await ResolveBumpFromHistoryAsync(tagPrefix, defaultBump, remoteName);
        Console.WriteLine($"Latest tag: {latestTag ?? "<none>"}");
        Console.WriteLine($"Commits considered: {commitCount}");
        Console.WriteLine($"Detected bump: {bumpType}");
        SetPipelineVariable("DetectedBump", bumpType);
        return 0;
    }

    private static async Task<int> CreateVersionTagAsync(CliOptions options)
    {
        var tagPrefix = options.Get("TagPrefix", "v")!;
        var defaultBump = options.Get("DefaultBump", "patch")!;
        var remoteName = options.Get("RemoteName", "origin")!;

        if (!new[] { "patch", "minor", "major" }.Contains(defaultBump, StringComparer.OrdinalIgnoreCase))
        {
            throw new CliException("DefaultBump must be one of: patch, minor, major.");
        }

        var sourceVersion = Environment.GetEnvironmentVariable("BUILD_SOURCEVERSION");
        if (string.IsNullOrWhiteSpace(sourceVersion))
        {
            sourceVersion = (await GitAsync(["rev-parse", "HEAD"])).StandardOutput
                .SplitLines()
                .FirstOrDefault();
        }

        var buildNumber = Environment.GetEnvironmentVariable("BUILD_BUILDNUMBER") ?? "local";
        Console.WriteLine($"Fetching tags from '{remoteName}'.");
        await GitAsync(["fetch", remoteName, "--tags", "--force"]);

        var tagPattern = $"{tagPrefix}[0-9]*.[0-9]*.[0-9]*";
        var currentTag = (await GitAsync(["tag", "--points-at", "HEAD", "--list", tagPattern])).StandardOutput
            .SplitLines()
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(currentTag))
        {
            Console.WriteLine($"Commit already has semantic version tag '{currentTag}'. Nothing to do.");
            SetPipelineVariable("CreatedTag", currentTag);
            SetPipelineVariable("DetectedBump", "none");
            return 0;
        }

        var (bumpType, latestTag, commitCount) = await ResolveBumpFromHistoryAsync(tagPrefix, defaultBump, remoteName, alreadyFetched: true);
        if (string.IsNullOrWhiteSpace(latestTag))
        {
            throw new CliException($"No semantic version tags matching '{tagPattern}' were found. Create the first tag manually, for example '{tagPrefix}1.0.0'.");
        }

        var versionMatch = Regex.Match(latestTag, $"^{Regex.Escape(tagPrefix)}(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)$");
        if (!versionMatch.Success)
        {
            throw new CliException($"Latest tag '{latestTag}' does not match the expected format '{tagPrefix}X.Y.Z'.");
        }

        var major = int.Parse(versionMatch.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = int.Parse(versionMatch.Groups["minor"].Value, CultureInfo.InvariantCulture);
        var patch = int.Parse(versionMatch.Groups["patch"].Value, CultureInfo.InvariantCulture);
        Console.WriteLine($"Latest tag is '{latestTag}'. Considered {commitCount} commit(s). Selected bump type is '{bumpType}'.");
        SetPipelineVariable("DetectedBump", bumpType);

        switch (bumpType)
        {
            case "major":
                major++;
                minor = 0;
                patch = 0;
                break;
            case "minor":
                minor++;
                patch = 0;
                break;
            default:
                patch++;
                break;
        }

        var nextTag = $"{tagPrefix}{major}.{minor}.{patch}";
        Console.WriteLine($"Creating tag '{nextTag}' on commit {sourceVersion}.");
        var tagMessage = $"Release {nextTag}{Environment.NewLine}{Environment.NewLine}Created by Azure DevOps build {buildNumber} from commit {sourceVersion}.";

        await GitAsync(["config", "user.name", "Azure DevOps"]);
        await GitAsync(["config", "user.email", "azure-devops@local"]);
        await GitAsync(["tag", "-a", nextTag, "-m", tagMessage, "HEAD"]);
        await GitAsync(["push", remoteName, $"refs/tags/{nextTag}"]);

        Console.WriteLine($"Pushed tag '{nextTag}'.");
        SetPipelineVariable("CreatedTag", nextTag);
        return 0;
    }

    private static async Task<(string BumpType, string? LatestTag, int CommitCount)> ResolveBumpFromHistoryAsync(
        string tagPrefix,
        string defaultBump,
        string remoteName,
        bool alreadyFetched = false)
    {
        if (!alreadyFetched)
        {
            await GitAsync(["fetch", remoteName, "--tags", "--force"]);
        }

        var tagPattern = $"{tagPrefix}[0-9]*.[0-9]*.[0-9]*";
        var latestTag = (await GitAsync(["tag", "--list", tagPattern, "--sort=-version:refname"])).StandardOutput
            .SplitLines()
            .FirstOrDefault();

        var messages = new List<string>();
        if (!string.IsNullOrWhiteSpace(latestTag))
        {
            var log = await GitAsync(["log", $"{latestTag}..HEAD", "--pretty=format:%s%n%b%n---"], throwOnError: false);
            if (log.ExitCode == 0 && !string.IsNullOrWhiteSpace(log.StandardOutput))
            {
                messages.AddRange(
                    Regex.Split(log.StandardOutput, @"\r?\n---\r?\n?")
                        .Select(chunk => chunk.Trim())
                        .Where(chunk => chunk.Length > 0));
            }
        }

        // Also include the ADO-provided source version message when available (merge/squash commit).
        var buildMessage = Environment.GetEnvironmentVariable("BUILD_SOURCEVERSIONMESSAGE");
        if (!string.IsNullOrWhiteSpace(buildMessage))
        {
            messages.Add(buildMessage);
        }

        // Squash merges drop individual commits from git history; recover them from the associated PR.
        var prMessages = await AzureDevOpsPullRequestCommits.TryGetAssociatedCommitMessagesAsync();
        if (prMessages.Count > 0)
        {
            messages.AddRange(prMessages);
        }

        var bumpType = SemVerBump.Resolve(messages, defaultBump);
        return (bumpType, latestTag, messages.Count);
    }

    private static async Task<int> ValidateRepositoryAsync(CliOptions options)
    {
        var repoRoot = Path.GetFullPath(options.Get("RepoRoot", Directory.GetCurrentDirectory())!);
        var validationErrors = new List<string>();

        void AddError(string message)
        {
            validationErrors.Add(message);
            Console.WriteLine($"ERROR: {message}");
        }

        Console.WriteLine($"Validating repository: {repoRoot}");

        var releaseProcessScript = Path.Combine(repoRoot, "scripts", "ReleaseProcess.cs");
        if (File.Exists(releaseProcessScript))
        {
            Console.WriteLine("Checking ReleaseProcess launcher build...");
            var buildOutputPath = Path.Combine(Path.GetTempPath(), $"version-tag-validate-{Guid.NewGuid():N}");
            var buildResult = await RunProcessAsync(
                "dotnet",
                ["build", "--nologo", releaseProcessScript, "--output", buildOutputPath],
                repoRoot,
                throwOnError: false);
            if (buildResult.ExitCode != 0)
            {
                AddError($"dotnet build failed for '{releaseProcessScript}'. {buildResult.StandardError}{buildResult.StandardOutput}");
            }

            if (Directory.Exists(buildOutputPath))
            {
                try
                {
                    Directory.Delete(buildOutputPath, recursive: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Failed to remove validation build output '{buildOutputPath}': {ex.Message}");
                }
            }
        }

        var testProject = Path.Combine(repoRoot, "tests", "PipelineTools.ReleaseTools.Tests", "ReleaseTools.Tests.csproj");
        if (File.Exists(testProject))
        {
            Console.WriteLine("Checking test project build...");
            var testBuild = await RunProcessAsync(
                "dotnet",
                ["build", "--nologo", testProject],
                repoRoot,
                throwOnError: false);
            if (testBuild.ExitCode != 0)
            {
                AddError($"dotnet build failed for '{testProject}'. {testBuild.StandardError}{testBuild.StandardOutput}");
            }
        }

        var yamlFiles = Directory.GetFiles(repoRoot, "*.yml")
            .Concat(Directory.GetFiles(repoRoot, "*.yaml"))
            .Concat(Directory.Exists(Path.Combine(repoRoot, "templates")) ? Directory.GetFiles(Path.Combine(repoRoot, "templates"), "*.yml") : [])
            .Concat(Directory.Exists(Path.Combine(repoRoot, "templates")) ? Directory.GetFiles(Path.Combine(repoRoot, "templates"), "*.yaml") : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"Checking YAML script references in {yamlFiles.Length} YAML file(s)...");
        foreach (var yamlFile in yamlFiles)
        {
            var content = await File.ReadAllTextAsync(yamlFile);
            if (Regex.IsMatch(content, @"scripts/[^'""\s)]+\.ps1", RegexOptions.IgnoreCase))
            {
                AddError($"PowerShell script reference found in '{yamlFile}'.");
            }

            foreach (Match match in Regex.Matches(content, @"scripts/(?<relativePath>[^'""\s)]+\.cs)", RegexOptions.IgnoreCase))
            {
                var relativePath = match.Groups["relativePath"].Value.Replace('/', Path.DirectorySeparatorChar);
                var scriptPath = Path.Combine(repoRoot, "scripts", relativePath);
                if (!File.Exists(scriptPath))
                {
                    AddError($"Missing script reference in '{yamlFile}' - scripts/{match.Groups["relativePath"].Value}");
                }
            }
        }

        var textFiles = Directory.GetFiles(repoRoot, "*.md")
            .Concat(Directory.Exists(Path.Combine(repoRoot, "docs")) ? Directory.GetFiles(Path.Combine(repoRoot, "docs"), "*.md") : [])
            .Concat(yamlFiles)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine("Checking for merge conflict markers...");
        foreach (var file in textFiles)
        {
            var lineNumber = 0;
            foreach (var line in await File.ReadAllLinesAsync(file))
            {
                lineNumber++;
                if (Regex.IsMatch(line, @"^(<<<<<<<|=======|>>>>>>>)"))
                {
                    AddError($"Merge conflict marker found in '{file}:{lineNumber}' - {line.Trim()}");
                }
            }
        }

        if (validationErrors.Count > 0)
        {
            Console.Error.WriteLine($"Validation failed with {validationErrors.Count} issue(s).");
            return 1;
        }

        Console.WriteLine("Validation completed successfully.");
        return 0;
    }

    private static async Task<ProcessResult> GitAsync(IEnumerable<string> arguments, bool throwOnError = true) =>
        await RunProcessAsync("git", arguments, throwOnError: throwOnError);

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        bool throwOnError = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var result = new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        if (throwOnError && result.ExitCode != 0)
        {
            var joinedArgs = string.Join(" ", arguments);
            var details = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new CliException($"{fileName} {joinedArgs} failed:{Environment.NewLine}{details}");
        }

        return result;
    }

    private static void SetPipelineVariable(string name, string? value)
    {
        var escaped = (value ?? "")
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal)
            .Replace("]", "%5D", StringComparison.Ordinal)
            .Replace(";", "%3B", StringComparison.Ordinal);
        Console.WriteLine($"##vso[task.setvariable variable={name}]{escaped}");
        Console.WriteLine($"##vso[task.setvariable variable={name};isOutput=true]{escaped}");
    }

    private static bool IsHelp(string value) =>
        value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("help", StringComparison.OrdinalIgnoreCase);

    private static void WriteHelp()
    {
        Console.WriteLine("Usage: dotnet run --file scripts/VersionTag.cs -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  validate");
        Console.WriteLine("  detect-bump");
        Console.WriteLine("  create-version-tag");
    }
}

static class StringExtensions
{
    public static IEnumerable<string> SplitLines(this string value) =>
        Regex.Split(value, @"\r?\n").Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim());
}

sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
