using SmartVoiceAgent.Core.Contracts;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Application.Queries;

/// <summary>
/// Query for getting the status of an application.
/// </summary>
public record GetApplicationStatusQuery(string ApplicationName) : IQuery<AppStatus>;



