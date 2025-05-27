using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Application.Queries;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Application.Handlers.Queries;

/// <summary>
/// Handles the query to get application status.
/// </summary>
public sealed class GetApplicationStatusQueryHandler(IApplicationService appService)
    : IQueryHandler<GetAppStatusQuery, AppStatus>
{
    private readonly IApplicationService _appService = appService;

    public async Task<AppStatus> HandleAsync(GetAppStatusQuery query)
    {
        return await _appService.GetApplicationStatusAsync(query.AppName);
    }
}
