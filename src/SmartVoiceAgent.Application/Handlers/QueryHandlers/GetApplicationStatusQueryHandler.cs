using SmartVoiceAgent.Application.Queries;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.Queries;

/// <summary>
/// Handles the query to get application status.
/// </summary>
public sealed class GetApplicationStatusQueryHandler(IApplicationService appService)
    : IQueryHandler<GetApplicationStatusQuery, AppStatus>
{
    private readonly IApplicationService _appService = appService;

    public async Task<AppStatus> Handle(GetApplicationStatusQuery request, CancellationToken cancellationToken)
    {
        return await _appService.GetApplicationStatusAsync(request.ApplicationName);
    }
}
