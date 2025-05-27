using SmartVoiceAgent.Core.Contracts;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Application.Queries;

/// <summary>
/// Query to get the status of a specified application.
/// </summary>
/// <param name="AppName">The name of the application to check status for.</param>
public record GetAppStatusQuery(string AppName) : IQuery<AppStatus>;
