using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SmartVoiceAgent.Infrastructure.Mcp;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    public sealed class TaskAgentTools
    {
        private readonly McpOptions _mcpOptions;
        private IEnumerable<AIFunction>? _mcpTools;
        public TaskAgentTools(IOptions<McpOptions> options)
        {
            this._mcpOptions = options.Value;
        }

        private static async Task<McpClient> GetMcpClientAsync(McpOptions mcpOptions)
        {
            McpClient mcpClient = await McpClient.CreateAsync(
        clientTransport: new HttpClientTransport(new()
        {
            Endpoint = new Uri(mcpOptions.TodoistServerLink),
            Name = "todoist.mcpverse.dev",
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {mcpOptions.TodoistApiKey}"
            }
        }),
        clientOptions: new McpClientOptions
        {
            ClientInfo = new Implementation()
            {
                Name = "MCP.Client",
                Version = "1.0.0"
            }
        });

            return mcpClient;
        }
        public async Task InitializeAsync()
        {
            var client = await GetMcpClientAsync(_mcpOptions);
            _mcpTools = await client.ListToolsAsync();
        }

        public IEnumerable<AIFunction> GetTools()
        {
            return _mcpTools ?? Array.Empty<AIFunction>();
        }
    }
}
