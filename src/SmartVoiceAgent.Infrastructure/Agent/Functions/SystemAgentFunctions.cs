using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public class SystemAgentFunctions : IAgentFunctions
{
    private readonly IMediator _mediator;

    public SystemAgentFunctions(IMediator mediator)
    {
        _mediator = mediator;
    }

    public IEnumerable<FunctionContract> GetFunctionContracts()
    {
        return new[]
        {
            OpenApplicationAsyncFunctionContract,
            CloseApplicationAsyncFunctionContract,
            CheckApplicationAsyncFunctionContract,
            GetApplicationPathAsyncFunctionContract,
            IsApplicationRunningAsyncFunctionContract,
            ListInstalledApplicationsAsyncFunctionContract,
            PlayMusicAsyncFunctionContract,
            ControlDeviceAsyncFunctionContract
        };
    }

    [Function]
    public async Task<string> OpenApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Opening application {applicationName}");
        try
        {
            var result = await _mediator.Send(new OpenApplicationCommand(applicationName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> CloseApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Closing application {applicationName}");
        try
        {
            var result = await _mediator.Send(new CloseApplicationCommand(applicationName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> CheckApplicationAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Checking application {applicationName}");
        try
        {
            var result = await _mediator.Send(new CheckApplicationCommand(applicationName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> GetApplicationPathAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Getting path for application {applicationName}");
        try
        {
            var result = await _mediator.Send(new GetApplicationPathCommand(applicationName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> IsApplicationRunningAsync(string applicationName)
    {
        Console.WriteLine($"SystemAgent: Checking if application {applicationName} is running");
        try
        {
            var result = await _mediator.Send(new IsApplicationRunningCommand(applicationName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> ListInstalledApplicationsAsync(bool includeSystemApps = false)
    {
        Console.WriteLine($"SystemAgent: Listing installed applications (includeSystemApps: {includeSystemApps})");
        try
        {
            var result = await _mediator.Send(new ListInstalledApplicationsCommand(includeSystemApps));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> PlayMusicAsync(string trackName)
    {
        Console.WriteLine($"SystemAgent: Playing music {trackName}");
        try
        {
            var result = await _mediator.Send(new PlayMusicCommand(trackName));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    [Function]
    public async Task<string> ControlDeviceAsync(string deviceName, string action)
    {
        Console.WriteLine($"SystemAgent: Controlling device {deviceName} with action {action}");
        try
        {
            var result = await _mediator.Send(new ControlDeviceCommand(deviceName, action));
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { Success = false, Message = ex.Message });
        }
    }

    // Function Contracts
    public FunctionContract OpenApplicationAsyncFunctionContract => new()
    {
        Name = nameof(OpenApplicationAsync),
        Description = "Opens a desktop application based on the given name",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                applicationName = new
                {
                    Type = "string",
                    Description = "The name of the application to open"
                }
            },
            Required = new[] { "applicationName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract CloseApplicationAsyncFunctionContract => new()
    {
        Name = nameof(CloseApplicationAsync),
        Description = "Closes a desktop application based on the given name",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                applicationName = new
                {
                    Type = "string",
                    Description = "The name of the application to close"
                }
            },
            Required = new[] { "applicationName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract CheckApplicationAsyncFunctionContract => new()
    {
        Name = nameof(CheckApplicationAsync),
        Description = "Checks if an application is installed and provides detailed information",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                applicationName = new
                {
                    Type = "string",
                    Description = "The name of the application to check"
                }
            },
            Required = new[] { "applicationName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract GetApplicationPathAsyncFunctionContract => new()
    {
        Name = nameof(GetApplicationPathAsync),
        Description = "Gets the executable path of an installed application",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                applicationName = new
                {
                    Type = "string",
                    Description = "The name of the application"
                }
            },
            Required = new[] { "applicationName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract IsApplicationRunningAsyncFunctionContract => new()
    {
        Name = nameof(IsApplicationRunningAsync),
        Description = "Checks if an application is currently running",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                applicationName = new
                {
                    Type = "string",
                    Description = "The name of the application to check"
                }
            },
            Required = new[] { "applicationName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract ListInstalledApplicationsAsyncFunctionContract => new()
    {
        Name = nameof(ListInstalledApplicationsAsync),
        Description = "Lists all installed applications on the system",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                includeSystemApps = new
                {
                    Type = "boolean",
                    Description = "Whether to include system applications in the list",
                    Default = false
                }
            },
            Required = new string[] { }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract PlayMusicAsyncFunctionContract => new()
    {
        Name = nameof(PlayMusicAsync),
        Description = "Plays music based on the given track name",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                trackName = new
                {
                    Type = "string",
                    Description = "The name or path of the track to play"
                }
            },
            Required = new[] { "trackName" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };

    public FunctionContract ControlDeviceAsyncFunctionContract => new()
    {
        Name = nameof(ControlDeviceAsync),
        Description = "Controls a device based on the given device name and action",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                deviceName = new
                {
                    Type = "string",
                    Description = "The name of the device to control"
                },
                action = new
                {
                    Type = "string",
                    Description = "The action to perform on the device"
                }
            },
            Required = new[] { "deviceName", "action" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };
}
