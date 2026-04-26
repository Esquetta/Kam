using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Policy;

public sealed class SkillPolicyManager : ISkillPolicyManager
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillPolicyStore _policyStore;

    public SkillPolicyManager(
        ISkillRegistry skillRegistry,
        ISkillPolicyStore policyStore)
    {
        _skillRegistry = skillRegistry;
        _policyStore = policyStore;
    }

    public Task<bool> ApproveReviewAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Update(skillId, manifest =>
        {
            manifest.Enabled = true;
            manifest.ReviewRequired = false;
            manifest.GrantedPermissions = RequiredPermissions(manifest);
            return true;
        }));
    }

    public Task<bool> EnableAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Update(skillId, manifest =>
        {
            if (manifest.ReviewRequired)
            {
                return false;
            }

            manifest.Enabled = true;
            return true;
        }));
    }

    public Task<bool> DisableAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Update(skillId, manifest =>
        {
            manifest.Enabled = false;
            manifest.ReviewRequired = false;
            return true;
        }));
    }

    public Task<bool> RevokePermissionsAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Update(skillId, manifest =>
        {
            manifest.GrantedPermissions = [];
            return true;
        }));
    }

    public Task<bool> GrantPermissionsAsync(
        string skillId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Update(skillId, manifest =>
        {
            if (manifest.ReviewRequired)
            {
                return false;
            }

            manifest.GrantedPermissions = RequiredPermissions(manifest);
            return true;
        }));
    }

    public Task<bool> SetRuntimeOptionAsync(
        string skillId,
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(Update(skillId, manifest =>
        {
            manifest.RuntimeOptions = CloneRuntimeOptions(manifest.RuntimeOptions);
            var normalizedKey = key.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                manifest.RuntimeOptions.Remove(normalizedKey);
            }
            else
            {
                manifest.RuntimeOptions[normalizedKey] = value.Trim();
            }

            return true;
        }));
    }

    private bool Update(string skillId, Func<KamSkillManifest, bool> update)
    {
        if (!_skillRegistry.TryGet(skillId, out var manifest) || manifest is null)
        {
            return false;
        }

        if (!update(manifest))
        {
            return false;
        }

        _skillRegistry.Register(manifest);
        _policyStore.SaveState(CreateState(manifest));
        return true;
    }

    private static SkillPolicyState CreateState(KamSkillManifest manifest)
    {
        return new SkillPolicyState
        {
            SkillId = manifest.Id,
            Enabled = manifest.Enabled,
            ReviewRequired = manifest.ReviewRequired,
            GrantedPermissions = manifest.GrantedPermissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .ToList(),
            RuntimeOptions = CloneRuntimeOptions(manifest.RuntimeOptions)
        };
    }

    private static List<SkillPermission> RequiredPermissions(KamSkillManifest manifest)
    {
        return manifest.Permissions
            .Where(permission => permission != SkillPermission.None)
            .Distinct()
            .ToList();
    }

    private static Dictionary<string, string> CloneRuntimeOptions(
        IReadOnlyDictionary<string, string>? runtimeOptions)
    {
        if (runtimeOptions is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return runtimeOptions
            .Where(option => !string.IsNullOrWhiteSpace(option.Key))
            .ToDictionary(
                option => option.Key.Trim(),
                option => option.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }
}
