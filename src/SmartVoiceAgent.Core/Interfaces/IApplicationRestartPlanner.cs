using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IApplicationRestartPlanner
{
    ApplicationRestartPlan CreateRestartPlan(string? updatePackagePath = null);
}
