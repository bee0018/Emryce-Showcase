using Serilog;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

string appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PortfolioApp");
string logPath = Path.Combine(appDataDirectory, "logs", "updater-.log");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.Async(a => a.File(
        logPath,
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        retainedFileCountLimit: 7,
        rollOnFileSizeLimit: true))
    .CreateLogger();

if (args.Length != 4)
{
    string errorPrompt = "Invalid arguments given. Expected:" +
        "\n\t[0] File path to the update package (.zip)" +
        "\n\t[1] Working directory where the parent process is running" +
        "\n\t[2] Parent application executable full path" +
        "\n\t[3] Parent process ID";
    Log.Error(errorPrompt);
    SafeExit(-1);
}

string artifactFilePath = args[0];
string processDirectory = args[1];
string parentExecutablePath = args[2];
int processId = int.Parse(args[3]);

Log.Information("Initializing update sequence...");

try
{
    Process parentProcess = Process.GetProcessById(processId);
    Log.Information("Waiting for main application to close...");
    parentProcess.WaitForExit(10000);
}
catch (ArgumentException)
{
    // Process ID has already terminated
}

Thread.Sleep(3000);

string zipDirectory = Path.GetDirectoryName(artifactFilePath);
string extractPath = Path.Combine(zipDirectory, $"extracted_{Guid.NewGuid()}");

try
{
    Log.Information("Performing file extraction...");
    ZipFile.ExtractToDirectory(artifactFilePath, extractPath, overwriteFiles: true);

    Log.Information("Archive extraction complete. Applying file updates...");
    TotalFolderSwap(extractPath, processDirectory);
    Log.Information("Update applied successfully.");

    Log.Information("Cleaning up installation temporary files...");
    File.Delete(artifactFilePath);
}
catch (Exception ex)
{
    Log.Error("Error applying update: {Message}", ex.Message);
    SafeExit(-2);
}
finally
{
    try
    {
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, true);
        }
    }
    catch (Exception ex)
    {
        Log.Warning("Could not delete temporary extraction folder: {Message}", ex.Message);
    }
}

// Restore cross-platform execution permissions if running on Linux
if (OperatingSystem.IsLinux())
{
    ProcessStartInfo chmodInfo = new()
    {
        FileName = "chmod",
        Arguments = $"+x \"{parentExecutablePath}\"",
        RedirectStandardOutput = true,
        UseShellExecute = false
    };

    using (Process process = Process.Start(chmodInfo))
    {
        process?.WaitForExit();
    }

    Log.Information("Successfully restored Linux execution permissions.");
}

if (File.Exists(parentExecutablePath))
{
    Log.Information("Launching updated application: {Path}", parentExecutablePath);
    Process.Start(new ProcessStartInfo
    {
        FileName = parentExecutablePath,
        UseShellExecute = true
    });
}

SafeExit(0);

#region Private Methods

/// <summary>
/// Safely exits the program with log flushing.
/// </summary>
static void SafeExit(int exitCode)
{
    Log.CloseAndFlush();
    Environment.Exit(exitCode);
}

/// <summary>
/// Performs a total folder swap, handling nested root layouts and configuration preservation.
/// </summary>
static void TotalFolderSwap(string extractPath, string processDirectory)
{
    string trueSourcePath = extractPath;
    string[] subDirectories = Directory.GetDirectories(extractPath);
    
    // Handle nested folder structures commonly found in release zips
    if (Directory.GetFiles(extractPath).Length == 0 && subDirectories.Length == 1)
    {
        trueSourcePath = subDirectories[0];
        Log.Information("Detected nested archive layout. Adjusting payload root source path to: {Path}", trueSourcePath);
    }

    if (!Directory.Exists(processDirectory))
    {
        Directory.CreateDirectory(processDirectory);
    }

    // Backup local configuration so user settings aren't overwritten during updates
    string backupConfigFilePath = Path.Combine(Path.GetTempPath(), $"app_config_backup_{Guid.NewGuid()}.xml");
    bool backupCreated = false;
    string existingConfigFilePath = Path.Combine(processDirectory, "config.xml");
    
    if (File.Exists(existingConfigFilePath))
    {
        try
        {
            string xmlText = File.ReadAllText(existingConfigFilePath);
            File.WriteAllText(backupConfigFilePath, xmlText, Encoding.UTF8);
            backupCreated = true;
        }
        catch (Exception ex)
        {
            Log.Warning("Could not backup existing config.xml: {Message}.", ex.Message);
        }
    }

    Log.Information("Copying new deployment files into target directory: {Target}", processDirectory);
    CopyFolderContents(trueSourcePath, processDirectory, processDirectory);

    // Restore configuration file post-swap
    if (backupCreated && File.Exists(backupConfigFilePath))
    {
        try
        {
            string newConfigFilePath = Path.Combine(processDirectory, "config.xml");
            CopyFileWithRetry(backupConfigFilePath, newConfigFilePath);
            File.Delete(backupConfigFilePath);
            Log.Information("Configuration file successfully preserved.");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to restore config.xml backup: {Message}", ex.Message);
        }
    }
}

/// <summary>
/// Recursively copies folder contents while skipping updater binaries and preserving root configs.
/// </summary>
static void CopyFolderContents(string sourceFolder, string destFolder, string rootProcessDirectory)
{
    string[] files = Directory.GetFiles(sourceFolder);
    foreach (string file in files)
    {
        string name = Path.GetFileName(file);
        
        // Skip updater and target root configurations
        if (name.Equals("AppUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
           (name.Equals("config.xml", StringComparison.OrdinalIgnoreCase) && destFolder.Equals(rootProcessDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        string dest = Path.Combine(destFolder, name);
        CopyFileWithRetry(file, dest);
    }

    string[] folders = Directory.GetDirectories(sourceFolder);
    foreach (string folder in folders)
    {
        string name = Path.GetFileName(folder);
        string dest = Path.Combine(destFolder, name);

        if (!Directory.Exists(dest))
        {
            Directory.CreateDirectory(dest);
        }

        CopyFolderContents(folder, dest, rootProcessDirectory);
    }
}

/// <summary>
/// Copies a file with retry logic to handle file-lock contention during process terminations.
/// </summary>
static void CopyFileWithRetry(string sourcePath, string destPath)
{
    int maxRetries = 5;
    int delayMs = 1500;
    
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            return;
        }
        catch (IOException) when (i < maxRetries - 1)
        {
            Log.Warning("File {FileName} is locked. Retrying in {Delay}ms... (Attempt {Current}/{Max})",
                Path.GetFileName(destPath), delayMs, i + 1, maxRetries);
            Thread.Sleep(delayMs);
        }
    }
}

#endregion