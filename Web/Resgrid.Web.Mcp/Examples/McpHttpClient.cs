using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Mcp.Examples
{
	/// <summary>
	/// Example HTTP client for connecting to the MCP server
	/// </summary>
	public sealed class McpHttpClient : IDisposable
	{
		private readonly HttpClient _httpClient;
		private readonly string _baseUrl;
		private int _requestId;

		public McpHttpClient(string baseUrl)
		{
			_baseUrl = baseUrl;
			_httpClient = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(30)
			};
		}

		/// <summary>
		/// Initialize the MCP connection
		/// </summary>
		public async Task<JsonDocument> InitializeAsync(CancellationToken cancellationToken = default)
		{
			var request = new
			{
				jsonrpc = "2.0",
				id = Interlocked.Increment(ref _requestId),
				method = "initialize",
				@params = new
				{
					protocolVersion = "2024-11-05",
					capabilities = new { },
					clientInfo = new
					{
						name = "example-client",
						version = "1.0.0"
					}
				}
			};

			return await SendRequestAsync(request, cancellationToken);
		}

		/// <summary>
		/// List all available tools
		/// </summary>
		public async Task<JsonDocument> ListToolsAsync(CancellationToken cancellationToken = default)
		{
			var request = new
			{
				jsonrpc = "2.0",
				id = Interlocked.Increment(ref _requestId),
				method = "tools/list",
				@params = new { }
			};

			return await SendRequestAsync(request, cancellationToken);
		}

		/// <summary>
		/// Call a specific tool
		/// </summary>
		public async Task<JsonDocument> CallToolAsync(string toolName, object arguments, CancellationToken cancellationToken = default)
		{
			var request = new
			{
				jsonrpc = "2.0",
				id = Interlocked.Increment(ref _requestId),
				method = "tools/call",
				@params = new
				{
					name = toolName,
					arguments
				}
			};

			return await SendRequestAsync(request, cancellationToken);
		}

		/// <summary>
		/// Ping the server
		/// </summary>
		public async Task<JsonDocument> PingAsync(CancellationToken cancellationToken = default)
		{
			var request = new
			{
				jsonrpc = "2.0",
				id = Interlocked.Increment(ref _requestId),
				method = "ping",
				@params = new { }
			};

			return await SendRequestAsync(request, cancellationToken);
		}

		private async Task<JsonDocument> SendRequestAsync(object request, CancellationToken cancellationToken)
		{
			var json = JsonSerializer.Serialize(request);
			var content = new StringContent(json, Encoding.UTF8, "application/json");

			var response = await _httpClient.PostAsync(_baseUrl, content, cancellationToken);
			response.EnsureSuccessStatusCode();

			var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
			return JsonDocument.Parse(responseJson);
		}

		public void Dispose()
		{
			_httpClient?.Dispose();
		}
	}

	/// <summary>
	/// Example usage of the MCP HTTP client
	/// </summary>
	public static class McpHttpClientExample
	{
		public static async Task RunExampleAsync()
		{
			using var client = new McpHttpClient("http://localhost:8080/mcp");

			try
			{
				// Initialize connection
				Console.WriteLine("Initializing MCP connection...");
				var initResponse = await client.InitializeAsync();
				Console.WriteLine($"Initialize response: {initResponse.RootElement}");
				Console.WriteLine();

				// List available tools
				Console.WriteLine("Listing available tools...");
				var toolsResponse = await client.ListToolsAsync();
				var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
				Console.WriteLine($"Available tools ({tools.GetArrayLength()}):");
				foreach (var tool in tools.EnumerateArray())
				{
					var name = tool.GetProperty("name").GetString();
					var description = tool.GetProperty("description").GetString();
					Console.WriteLine($"  - {name}: {description}");
				}
				Console.WriteLine();

				// Ping server
				Console.WriteLine("Pinging server...");
				var pingResponse = await client.PingAsync();
				Console.WriteLine($"Ping response: {pingResponse.RootElement}");
				Console.WriteLine();

				// Example: Call authenticate tool
				Console.WriteLine("Calling authenticate tool...");
				var authArgs = new
				{
					username = "your-username",
					password = "your-password"
				};
				var authResponse = await client.CallToolAsync("authenticate", authArgs);
				Console.WriteLine($"Auth response: {authResponse.RootElement}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
			}
		}
	}
}


