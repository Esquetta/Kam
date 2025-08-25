using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.Json;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions;

public class WebSearchAgentFunctions : IAgentFunctions
{
    private readonly IMediator _mediator;

    public WebSearchAgentFunctions(IMediator mediator)
    {
        _mediator = mediator;
    }

    public IEnumerable<FunctionContract> GetFunctionContracts()
    {
        return new[]
        {
            SearchWebAsyncFunctionContract,
        };
    }

    [Function]
    public async Task<string> SearchWebAsync(string query, string lang = "tr", int results = 5)
    {
        Console.WriteLine($"WebSearchAgent: Searching web for {query}");
        try
        {
            var result = await _mediator.Send(new SearchWebCommand(query, lang, results));
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
    public FunctionContract SearchWebAsyncFunctionContract => new()
    {
        Name = nameof(SearchWebAsync),
        Description = "Searches the web for the given query and opens results on user's default browser",
        Parameters = (IEnumerable<FunctionParameterContract>)BinaryData.FromObjectAsJson(new
        {
            Type = "object",
            Properties = new
            {
                query = new
                {
                    Type = "string",
                    Description = "Search Query string"
                },
                lang = new
                {
                    Type = "string",
                    Description = "Search Language",
                    Default = "tr"
                },
                results = new
                {
                    Type = "integer",
                    Description = "Results count",
                    Default = 5
                }
            },
            Required = new[] { "query" }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
    };
    
}
