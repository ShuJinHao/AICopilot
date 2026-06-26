namespace AICopilot.AiGatewayService.CloudReadiness;

internal sealed class InMemoryCloudReadonlyPilotReadinessStore : ICloudReadonlyPilotReadinessStore
{
    private readonly object sync = new();
    private readonly Dictionary<string, CloudReadonlyPilotConfigPackageDto> packages = new(StringComparer.OrdinalIgnoreCase);
    private PilotApprovalRehearsalDto? latestApprovalRehearsal;
    private CloudReadonlyPilotContractRehearsalDto? latestContractRehearsal;

    public void SavePackage(CloudReadonlyPilotConfigPackageDto package)
    {
        lock (sync)
        {
            packages[package.PackageId] = package;
            if (packages.Count <= 20)
            {
                return;
            }

            foreach (var key in packages.Keys.Take(packages.Count - 20).ToArray())
            {
                packages.Remove(key);
            }
        }
    }

    public CloudReadonlyPilotConfigPackageDto? GetPackage(string packageId)
    {
        lock (sync)
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : packages.GetValueOrDefault(packageId);
        }
    }

    public CloudReadonlyPilotConfigPackageDto? LatestPackage()
    {
        lock (sync)
        {
            return packages.Values.LastOrDefault();
        }
    }

    public void SaveApprovalRehearsal(PilotApprovalRehearsalDto rehearsal)
    {
        lock (sync)
        {
            latestApprovalRehearsal = rehearsal;
        }
    }

    public PilotApprovalRehearsalDto? LatestApprovalRehearsal()
    {
        lock (sync)
        {
            return latestApprovalRehearsal;
        }
    }

    public void SaveContractRehearsal(CloudReadonlyPilotContractRehearsalDto rehearsal)
    {
        lock (sync)
        {
            latestContractRehearsal = rehearsal;
        }
    }

    public CloudReadonlyPilotContractRehearsalDto? LatestContractRehearsal()
    {
        lock (sync)
        {
            return latestContractRehearsal;
        }
    }
}
