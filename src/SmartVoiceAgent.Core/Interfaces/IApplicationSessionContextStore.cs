using SmartVoiceAgent.Core.Models.Session;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IApplicationSessionContextStore
{
    ApplicationSessionContext Load();

    void Save(ApplicationSessionContext context);
}
