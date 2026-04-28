using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public interface IModelCatalogService
{
    Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default);
}
