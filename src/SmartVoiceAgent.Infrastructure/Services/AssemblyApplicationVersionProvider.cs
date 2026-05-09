using System.Diagnostics;
using System.Reflection;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class AssemblyApplicationVersionProvider : IApplicationVersionProvider
{
    public string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AssemblyApplicationVersionProvider).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return TrimBuildMetadata(informationalVersion);
            }

            var location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location))
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(location).FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return fileVersion;
                }
            }

            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }
    }

    private static string TrimBuildMetadata(string version)
    {
        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex < 0 ? version : version[..metadataIndex];
    }
}
