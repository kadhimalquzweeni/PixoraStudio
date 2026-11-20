using System;
using System.IO;
using System.Linq;

namespace PixoraStudio.Infrastructure.Services;

/// <summary>
/// Locates FFmpeg executables in the application bundle or on the process PATH.
/// </summary>
public static class FFmpegExecutableResolver
{
    public static string? Resolve(
        string executableName,
        string applicationDirectory,
        string? pathEnvironment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        string bundledPath = Path.Combine(
            applicationDirectory, "FFmpeg", executableName);

        if (File.Exists(bundledPath))
            return Path.GetFullPath(bundledPath);

        string applicationRootPath = Path.Combine(
            applicationDirectory, executableName);

        if (File.Exists(applicationRootPath))
            return Path.GetFullPath(applicationRootPath);

        if (string.IsNullOrWhiteSpace(pathEnvironment))
            return null;

        foreach (string directory in pathEnvironment.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                string candidate = Path.Combine(
                    Environment.ExpandEnvironmentVariables(directory),
                    executableName);

                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // Ignore malformed PATH entries and continue searching.
            }
        }

        return null;
    }
}
