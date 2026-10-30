#:property TargetFramework=net10.0
#:property PublishAot=false

using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

var version = Environment.GetEnvironmentVariable("RELEASE_TOOLS_VERSION") ?? "v1.0.0";
if (!Regex.IsMatch(version, @"^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$"))
{
	Console.Error.WriteLine($"Invalid ReleaseTools version '{version}'. Expected a v-prefixed semantic version.");
	return 1;
}

var tempRoot = Environment.GetEnvironmentVariable("AGENT_TEMPDIRECTORY") ?? Path.GetTempPath();
var installDirectory = Path.Combine(tempRoot, "release-management-tools", version);
var executablePath = Path.Combine(installDirectory, "ReleaseTools");

if (!File.Exists(executablePath))
{
	Directory.CreateDirectory(installDirectory);
	var archivePath = Path.Combine(installDirectory, "release-management-tools-linux-x64.tar.gz");
	var assetUrl = $"https://github.com/mmarca-tech/release-management-tools/releases/download/{version}/release-management-tools-linux-x64.tar.gz";

	using var client = new HttpClient();
	await using (var remoteStream = await client.GetStreamAsync(assetUrl))
	await using (var archiveStream = File.Create(archivePath))
	{
		await remoteStream.CopyToAsync(archiveStream);
	}

	var extractInfo = new ProcessStartInfo("tar")
	{
		UseShellExecute = false
	};
	foreach (var argument in new[] { "-xzf", archivePath, "-C", installDirectory })
	{
		extractInfo.ArgumentList.Add(argument);
	}

	using var extractProcess = Process.Start(extractInfo) ?? throw new InvalidOperationException("Could not start tar to extract ReleaseTools.");
	await extractProcess.WaitForExitAsync();
	File.Delete(archivePath);
	if (extractProcess.ExitCode != 0 || !File.Exists(executablePath))
	{
		Console.Error.WriteLine($"Failed to install ReleaseTools version {version} from {assetUrl}.");
		return 1;
	}
}

var startInfo = new ProcessStartInfo(executablePath)
{
	UseShellExecute = false
};
foreach (var argument in args)
{
	startInfo.ArgumentList.Add(argument);
}

using var releaseTools = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ReleaseTools.");
await releaseTools.WaitForExitAsync();
return releaseTools.ExitCode;
