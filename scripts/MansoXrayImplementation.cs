#:property TargetFramework=net10.0
#:property PublishAot=false

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

return await MansoXrayImplementationProgram.RunAsync(args);

static class MansoXrayImplementationProgram
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = CliOptions.Parse(args);
            await RunImplementationAsync(options);
            return 0;
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

    private static async Task RunImplementationAsync(CliOptions options)
    {
        var commitSha = options.Required("commit-sha");
        var repositoryDirectory = Path.GetFullPath(options.Required("repository-directory"));
        var outputDirectory = Path.GetFullPath(options.Required("out"));
        var packageSpec = options.Get("package-spec", "@mmarca-tech/manso-testing-agent@0.1.4")!;
        var workDirectory = Path.GetFullPath(options.Get(
            "work-directory",
            Path.Combine(GetAgentTempDirectory(), "manso-testing-agent-implementation"))!);
        var npmUserConfig = options.Get("npm-user-config");
        var repositoryName = options.Get("repository-name");
        var targetBranch = options.Get("target-branch", "main")!;
        var healAttempts = options.GetInt("heal-attempts", 4);
        var testDesignEnabled = options.GetBool("test-design-enabled", true);
        var reuseBranchIfPossible = options.GetBool("reuse-branch-if-possible", true);

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            repositoryName = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BUILD_REPOSITORY_NAME"))
                ? Environment.GetEnvironmentVariable("BUILD_REPOSITORY_NAME")
                : Path.GetFileName(repositoryDirectory);
        }

        if (!Directory.Exists(repositoryDirectory))
        {
            throw new CliException($"Repository directory does not exist: {repositoryDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(workDirectory);

        var commitMessage = (await RunProcessAsync(
            "git",
            ["-C", repositoryDirectory, "log", "-1", "--pretty=%B", commitSha],
            echoOutput: false)).StandardOutput.TrimEnd();

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            throw new CliException($"Unable to read the commit message for {commitSha}.");
        }

        var commitSubject = commitMessage.SplitLines().FirstOrDefault() ?? "";
        if (Regex.IsMatch(commitSubject, @"\bImplement Xray tests for [A-Z][A-Z0-9]+-\d+\b", RegexOptions.IgnoreCase))
        {
            WriteLog($"Skipping Manso because this looks like a generated Xray implementation commit: {commitSubject}");
            return;
        }

        var authorEmail = (await RunProcessAsync(
            "git",
            ["-C", repositoryDirectory, "log", "-1", "--pretty=%ae", commitSha],
            echoOutput: false)).StandardOutput.SplitLines().FirstOrDefault();

        if (string.Equals(authorEmail, "automation@example.com", StringComparison.OrdinalIgnoreCase))
        {
            WriteLog($"Skipping Manso because the commit author is {authorEmail}.");
            return;
        }

        var ticketKeys = GetJiraTicketKeys(commitMessage).ToArray();
        if (ticketKeys.Length == 0)
        {
            WriteLog($"No Jira ticket key found in commit message subject: {commitSubject}");
            WriteLog("Skipping Manso Xray test implementation.");
            return;
        }

        if (ticketKeys.Length > 1)
        {
            WriteLog($"Found multiple Jira keys ({string.Join(", ", ticketKeys)}); using first key {ticketKeys[0]}.");
        }

        var ticketKey = ticketKeys[0];
        var implementationBranch = $"tests/{ticketKey.ToLowerInvariant()}-xray-implementation";

        ClearUnresolvedSecretVariables();
        SetConfluenceUrlFromJiraUrl();

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN")))
        {
            throw new CliException("SYSTEM_ACCESSTOKEN is required so Manso can push the implementation branch.");
        }

        await SetAzureDevOpsGitAuthHeaderAsync(repositoryDirectory);
        await RunProcessAsync("git", ["-C", repositoryDirectory, "config", "user.email", "automation@example.com"]);
        await RunProcessAsync("git", ["-C", repositoryDirectory, "config", "user.name", "Manso Testing Agent"]);
        await RunProcessAsync("git", ["-C", repositoryDirectory, "fetch", "origin", targetBranch, "--prune"]);
        await RunProcessAsync("git", ["-C", repositoryDirectory, "checkout", "-B", targetBranch, commitSha]);

        if (!string.IsNullOrWhiteSpace(npmUserConfig))
        {
            npmUserConfig = Path.GetFullPath(npmUserConfig);
            if (!File.Exists(npmUserConfig))
            {
                throw new CliException($"npm user config does not exist: {npmUserConfig}");
            }

            Environment.SetEnvironmentVariable("NPM_CONFIG_USERCONFIG", npmUserConfig);
        }

        var installArgs = new List<string>
        {
            "install",
            "--prefix",
            workDirectory,
            "--no-save",
            "--no-package-lock",
            packageSpec
        };

        if (!string.IsNullOrWhiteSpace(npmUserConfig))
        {
            installArgs.AddRange(["--userconfig", npmUserConfig]);
        }

        WriteLog($"Installing {packageSpec} into {workDirectory}.");
        await RunProcessAsync("npm", installArgs);

        var agentCommand = await InitializeAgentCommandAsync(workDirectory);
        var guidancePaths = ResolveGuidancePaths(repositoryDirectory).ToArray();
        var verifyCommand = ResolveRepositoryVerificationCommand(repositoryDirectory);

        var implementationArgs = new List<string>
        {
            "--issue",
            ticketKey,
            "--out",
            outputDirectory,
            "--repo",
            repositoryDirectory,
            "--TestDesignEnabled",
            testDesignEnabled.ToString().ToLowerInvariant(),
            "--branch",
            implementationBranch,
            "--target-branch",
            targetBranch,
            "--commit-message",
            $"{ticketKey}: implement Xray tests for {repositoryName}",
            "--pr-title",
            $"Implement Xray tests for {ticketKey}",
            "--pr-description",
            $"Automated Xray test implementation generated from {repositoryName} main commit {commitSha}."
        };

        foreach (var guidancePath in guidancePaths)
        {
            implementationArgs.AddRange(["--guidance", guidancePath]);
        }

        if (!string.IsNullOrWhiteSpace(agentCommand))
        {
            implementationArgs.AddRange(["--agent-command", agentCommand]);
        }

        if (!string.IsNullOrWhiteSpace(verifyCommand))
        {
            implementationArgs.AddRange(["--verify-command", verifyCommand]);
        }

        if (healAttempts != 4)
        {
            implementationArgs.AddRange(["--heal-attempts", healAttempts.ToString()]);
        }

        if (reuseBranchIfPossible)
        {
            implementationArgs.Add("--reuse-branch-if-possible");
        }

        var implementationBinary = ResolveAgentBinary(workDirectory, "implement-xray-tests");
        WriteLog($"Running Manso Xray implementation for {ticketKey} from commit {commitSha}.");
        await RunProcessAsync(implementationBinary, implementationArgs, workingDirectory: repositoryDirectory);

        var issueOutputDirectory = Path.Combine(outputDirectory, ticketKey);
        WriteLog($"Manso Xray implementation completed for {ticketKey}. Output: {issueOutputDirectory}");
    }

    private static async Task<string?> InitializeAgentCommandAsync(string workDirectory)
    {
        var provider = Environment.GetEnvironmentVariable("LLM_PROVIDER");
        provider = string.IsNullOrWhiteSpace(provider) ? "openai" : provider.Trim().ToLowerInvariant();

        if (provider is not "openai" and not "anthropic")
        {
            throw new CliException($"LLM_PROVIDER must be one of: openai, anthropic. Actual value: '{provider}'.");
        }

        Environment.SetEnvironmentVariable("LLM_PROVIDER", provider);

        if (provider != "openai")
        {
            WriteLog($"Using manso-testing-agent default command selected by LLM_PROVIDER={provider}.");
            return null;
        }

        var codexPath = ResolveCodexBinary(workDirectory);
        await InitializeCodexApiKeyLoginAsync(codexPath, workDirectory);

        const string codexExecArguments = "exec --ephemeral --ignore-rules --dangerously-bypass-approvals-and-sandbox -";
        Environment.SetEnvironmentVariable("MANSO_TEST_IMPLEMENTATION_AGENT_PATH", codexPath);
        Environment.SetEnvironmentVariable("MANSO_CODEX_PATH", codexPath);
        Environment.SetEnvironmentVariable("MANSO_CODEX_EXEC_ARGUMENTS", codexExecArguments);

        var agentCommand = $"{ConvertToShellToken(codexPath)} {codexExecArguments}";
        WriteLog($"Using Codex implementation command: {agentCommand}");
        return agentCommand;
    }

    private static async Task InitializeCodexApiKeyLoginAsync(string codexPath, string workDirectory)
    {
        var codexHome = Path.GetFullPath(Path.Combine(workDirectory, ".codex"));
        Environment.SetEnvironmentVariable("CODEX_HOME", codexHome);
        Directory.CreateDirectory(codexHome);

        var llmApiKey = Environment.GetEnvironmentVariable("LLM_API_KEY");
        if (string.IsNullOrWhiteSpace(llmApiKey))
        {
            WriteLog($"LLM_API_KEY is not set; Codex will use existing auth state in {codexHome}.");
            return;
        }

        WriteLog($"Configuring Codex API-key auth in {codexHome}.");
        await RunProcessAsync(codexPath, ["login", "--with-api-key"], standardInput: llmApiKey);
    }

    private static string ResolveAgentBinary(string workDirectory, string name)
    {
        var binDirectory = Path.Combine(workDirectory, "node_modules", ".bin");
        var candidateNames = OperatingSystem.IsWindows()
            ? new[] { $"{name}.cmd", $"{name}.ps1", name }
            : new[] { name };

        foreach (var candidateName in candidateNames)
        {
            var candidatePath = Path.Combine(binDirectory, candidateName);
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        throw new CliException($"Could not find npm binary '{name}' under '{binDirectory}'.");
    }

    private static string ResolveCodexBinary(string workDirectory)
    {
        var nativeBinary = ResolveCodexNativeExecutable(workDirectory);
        if (!string.IsNullOrWhiteSpace(nativeBinary))
        {
            return nativeBinary;
        }

        var candidatePaths = OperatingSystem.IsWindows()
            ? []
            : new[]
            {
                Path.Combine(workDirectory, "node_modules", ".bin", "codex"),
                Path.Combine(workDirectory, "node_modules", "manso-testing-agent", "node_modules", ".bin", "codex")
            };

        foreach (var candidatePath in candidatePaths)
        {
            if (File.Exists(candidatePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        throw new CliException($"Could not find a bundled Codex binary under '{workDirectory}'.");
    }

    private static string? ResolveCodexNativeExecutable(string workDirectory)
    {
        var platform = GetCodexPlatformBinaryInfo();
        if (platform is null)
        {
            return null;
        }

        var moduleRoots = new[]
        {
            Path.Combine(workDirectory, "node_modules"),
            Path.Combine(workDirectory, "node_modules", "manso-testing-agent", "node_modules")
        };

        foreach (var moduleRoot in moduleRoots)
        {
            var candidatePaths = new[]
            {
                Path.Combine(moduleRoot, "@openai", platform.PackageDirectory, "vendor", platform.TargetTriple, "codex", platform.BinaryName),
                Path.Combine(moduleRoot, "@openai", "codex", "vendor", platform.TargetTriple, "codex", platform.BinaryName)
            };

            foreach (var candidatePath in candidatePaths)
            {
                if (File.Exists(candidatePath))
                {
                    return Path.GetFullPath(candidatePath);
                }
            }
        }

        return null;
    }

    private static CodexPlatformBinaryInfo? GetCodexPlatformBinaryInfo()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => new("codex-win32-x64", "x86_64-pc-windows-msvc", "codex.exe"),
                Architecture.Arm64 => new("codex-win32-arm64", "aarch64-pc-windows-msvc", "codex.exe"),
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => new("codex-linux-x64", "x86_64-unknown-linux-musl", "codex"),
                Architecture.Arm64 => new("codex-linux-arm64", "aarch64-unknown-linux-musl", "codex"),
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => new("codex-darwin-x64", "x86_64-apple-darwin", "codex"),
                Architecture.Arm64 => new("codex-darwin-arm64", "aarch64-apple-darwin", "codex"),
                _ => null
            };
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveGuidancePaths(string repositoryDirectory)
    {
        var conventionalPaths = new[]
        {
            ".manso",
            "architecture",
            "docs",
            "tests/.manso",
            "tests/architecture",
            "tests/docs"
        };

        var effectivePaths = conventionalPaths
            .Where(path => Directory.Exists(Path.Combine(repositoryDirectory, path)) || File.Exists(Path.Combine(repositoryDirectory, path)))
            .ToArray();

        if (effectivePaths.Length == 0)
        {
            WriteLog("No conventional guidance folders found. The implementation agent will use its package defaults.");
            return [];
        }

        WriteLog($"Auto-discovered guidance path(s): {string.Join(", ", effectivePaths)}");

        var resolvedPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var guidancePath in effectivePaths)
        {
            var candidatePath = Path.Combine(repositoryDirectory, guidancePath);
            if (Directory.Exists(candidatePath))
            {
                foreach (var filePath in Directory.EnumerateFiles(candidatePath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    resolvedPaths.Add(ConvertToRepoRelativePath(repositoryDirectory, filePath));
                }
            }
            else if (File.Exists(candidatePath))
            {
                resolvedPaths.Add(ConvertToRepoRelativePath(repositoryDirectory, candidatePath));
            }
        }

        return resolvedPaths.ToArray();
    }

    private static string? ResolveRepositoryVerificationCommand(string repositoryDirectory)
    {
        var conventionPaths = new[]
        {
            "tests/.manso/repo-conventions.json",
            ".manso/repo-conventions.json"
        };

        foreach (var conventionPath in conventionPaths)
        {
            var fullPath = Path.Combine(repositoryDirectory, conventionPath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
            var root = document.RootElement;

            foreach (var propertyName in new[]
                     {
                         "xrayImplementationVerificationCommand",
                         "functionalVerificationCommand",
                         "functionalDiscoveryCommand",
                         "defaultVerificationCommand",
                         "verificationCommand"
                     })
            {
                var value = GetOptionalString(root, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    WriteLog($"Using verification command from {conventionPath}: {value}");
                    return value;
                }
            }

            if (root.TryGetProperty("validationCommands", out var validationCommands) && validationCommands.ValueKind == JsonValueKind.Array)
            {
                var commands = validationCommands
                    .EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.String)
                    .Select(element => element.GetString())
                    .Where(command => !string.IsNullOrWhiteSpace(command))
                    .Cast<string>()
                    .ToArray();

                if (commands.Length > 0)
                {
                    var preferredCommand = commands.FirstOrDefault(command => command.Contains("FunctionalTests", StringComparison.OrdinalIgnoreCase))
                        ?? commands[0];
                    WriteLog($"Using verification command from {conventionPath} validationCommands: {preferredCommand}");
                    return preferredCommand;
                }
            }
        }

        WriteLog("No repository verification command found. Implementation verification will be skipped.");
        return null;
    }

    private static async Task SetAzureDevOpsGitAuthHeaderAsync(string repositoryDirectory)
    {
        var accessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
        var collectionUriValue = Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI");
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(collectionUriValue))
        {
            return;
        }

        if (!Uri.TryCreate(collectionUriValue, UriKind.Absolute, out var collectionUri) ||
            !string.Equals(collectionUri.Host, "dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var organization = collectionUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(organization))
        {
            return;
        }

        var gitAuthConfigKey = $"http.https://dev.azure.com/{organization}/.extraheader";
        var gitAuthConfigValue = $"AUTHORIZATION: bearer {accessToken}";

        await RunProcessAsync(
            "git",
            [
                "-C",
                repositoryDirectory,
                "config",
                gitAuthConfigKey,
                gitAuthConfigValue
            ],
            echoOutput: false);
        AppendGitConfigEnvironment(gitAuthConfigKey, gitAuthConfigValue);
    }

    private static void ClearUnresolvedSecretVariables()
    {
        var secretVariableNames = new[]
        {
            "JIRA_URL",
            "JIRA_USERNAME",
            "JIRA_API_TOKEN",
            "CONFLUENCE_ENABLED",
            "CONFLUENCE_URL",
            "CONFLUENCE_USERNAME",
            "CONFLUENCE_API_TOKEN",
            "AZURE_DEVOPS_PAT",
            "LLM_PROVIDER",
            "LLM_API_KEY",
            "LLM_MODEL",
            "OTEL_PROVIDER",
            "OTEL_ENABLED",
            "OTEL_PROJECT_NAME",
            "OTEL_ENDPOINT",
            "OTEL_API_KEY",
            "OTEL_PUBLIC_KEY",
            "OTEL_SECRET_KEY",
            "OTEL_BATCH",
            "MANSO_CODEX_FORWARD_OTEL",
            "MANSO_MEMORY_ROOT",
            "MANSO_MEMORY_ENABLED",
            "MANSO_MEMORY_CACHE_DIR",
            "MANSO_MEMORY_BRANCH",
            "MANSO_MEMORY_STRICT",
            "OPENINFERENCE_HIDE_INPUTS",
            "OPENINFERENCE_HIDE_OUTPUTS",
            "XRAY_BASE_URL",
            "XRAY_CLIENT_ID",
            "XRAY_CLIENT_SECRET",
            "XRAY_TEST_LINK_TYPE",
            "XRAY_TEST_LINK_DIRECTION"
        };

        foreach (var name in secretVariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null && Regex.IsMatch(value, @"^\$\([^)]+\)$"))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    private static void SetConfluenceUrlFromJiraUrl()
    {
        var jiraUrl = Environment.GetEnvironmentVariable("JIRA_URL");
        if (string.IsNullOrWhiteSpace(jiraUrl))
        {
            return;
        }

        jiraUrl = jiraUrl.TrimEnd('/');
        Environment.SetEnvironmentVariable("JIRA_URL", jiraUrl);

        var confluenceUrl = jiraUrl.EndsWith("/wiki", StringComparison.OrdinalIgnoreCase)
            ? jiraUrl
            : $"{jiraUrl}/wiki";

        Environment.SetEnvironmentVariable("CONFLUENCE_URL", confluenceUrl);
    }

    private static IEnumerable<string> GetJiraTicketKeys(string commitMessage)
    {
        return Regex.Matches(commitMessage, @"(?<![A-Z0-9])([A-Z][A-Z0-9]+-\d+)(?![A-Z0-9])", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string ConvertToRepoRelativePath(string repositoryDirectory, string path)
    {
        return Path.GetRelativePath(repositoryDirectory, path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ConvertToShellToken(string value)
    {
        return value.Any(char.IsWhiteSpace)
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }

    private static string GetAgentTempDirectory()
    {
        return Environment.GetEnvironmentVariable("AGENT_TEMPDIRECTORY") ?? Path.GetTempPath();
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
        Usage:
          dotnet run --file scripts/MansoXrayImplementation.cs -- --commit-sha <sha> --repository-directory <path> --out <path> [options]

        Options:
          --package-spec <npm package>              Default: @mmarca-tech/manso-testing-agent@0.1.4
          --work-directory <path>                   Default: $(Agent.TempDirectory)/manso-testing-agent-implementation
          --npm-user-config <path>                  Authenticated npmrc path
          --repository-name <name>                  Name used in generated commit/PR text
          --target-branch <branch>                  Default: main
          --heal-attempts <count>                   Default: 4
          --test-design-enabled <true|false>        Default: true
          --reuse-branch-if-possible <true|false>   Default: true
        """);
    }

    private static void WriteLog(string message)
    {
        var elapsed = (DateTimeOffset.UtcNow - StartedAt).TotalSeconds.ToString("0.0");
        Console.WriteLine($"[{elapsed} s] {message}");
    }

    private static void AppendGitConfigEnvironment(string key, string value)
    {
        var countValue = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT");
        var count = int.TryParse(countValue, out var parsedCount) && parsedCount >= 0
            ? parsedCount
            : 0;

        for (var index = 0; index < count; index++)
        {
            if (string.Equals(Environment.GetEnvironmentVariable($"GIT_CONFIG_KEY_{index}"), key, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable($"GIT_CONFIG_VALUE_{index}", value);
                return;
            }
        }

        Environment.SetEnvironmentVariable($"GIT_CONFIG_KEY_{count}", key);
        Environment.SetEnvironmentVariable($"GIT_CONFIG_VALUE_{count}", value);
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", (count + 1).ToString());
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        bool echoOutput = true,
        string? standardInput = null)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            processStartInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            processStartInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processStartInfo)
            ?? throw new CliException($"Failed to start process '{fileName}'.");

        if (standardInput is not null)
        {
            await process.StandardInput.WriteLineAsync(standardInput);
            process.StandardInput.Close();
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (echoOutput && !string.IsNullOrWhiteSpace(standardOutput))
        {
            Console.Write(standardOutput);
        }

        if (echoOutput && !string.IsNullOrWhiteSpace(standardError))
        {
            Console.Error.Write(standardError);
        }

        if (process.ExitCode != 0)
        {
            throw new CliException($"{fileName} failed with exit code {process.ExitCode}.");
        }

        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record CodexPlatformBinaryInfo(string PackageDirectory, string TargetTriple, string BinaryName);
}

sealed class CliOptions
{
    private readonly Dictionary<string, string> values;

    private CliOptions(Dictionary<string, string> values)
    {
        this.values = values;
    }

    public static CliOptions Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var name = enumerator.Current;
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new CliException($"Unexpected argument '{name}'. Options must use --name value syntax.");
            }

            name = name[2..];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CliException("Option name cannot be empty.");
            }

            if (!enumerator.MoveNext())
            {
                throw new CliException($"Missing value for option '--{name}'.");
            }

            values[name] = enumerator.Current;
        }

        return new CliOptions(values);
    }

    public string Required(string name)
    {
        return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CliException($"--{name} is required.");
    }

    public string? Get(string name, string? defaultValue = null)
    {
        return values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new CliException($"--{name} must be an integer. Actual value: '{value}'.");
    }

    public bool GetBool(string name, bool defaultValue)
    {
        var value = Get(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => throw new CliException($"--{name} must be a boolean value. Actual value: '{value}'.")
        };
    }
}

sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

sealed class CliException : Exception
{
    public CliException(string message)
        : base(message)
    {
    }
}

static class StringExtensions
{
    public static IEnumerable<string> SplitLines(this string value)
    {
        return value.Split(["\r\n", "\n"], StringSplitOptions.None);
    }
}
