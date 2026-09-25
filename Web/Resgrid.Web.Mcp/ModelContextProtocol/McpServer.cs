﻿﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Web.Mcp.Infrastructure;

namespace Resgrid.Web.Mcp.ModelContextProtocol
{
	/// <summary>
	/// Simple MCP Server implementation based on the Model Context Protocol specification
	/// </summary>
	public sealed class McpServer : IMcpRequestHandler
	{
		private const string ToolCallOperation = "tools/call";

		private readonly string _serverName;
		private readonly string _serverVersion;
		private readonly Dictionary<string, ToolDefinition> _tools;
		private readonly ILogger _logger;
		private readonly IRateLimiter _rateLimiter;

		public McpServer(string serverName, string serverVersion, ILogger logger = null, IRateLimiter rateLimiter = null)
		{
			_serverName = serverName;
			_serverVersion = serverVersion;
			_tools = new Dictionary<string, ToolDefinition>();
			_logger = logger;
			_rateLimiter = rateLimiter;
		}

		public void AddTool(string name, string description, Dictionary<string, object> inputSchema, Func<object, Task<object>> handler)
		{
			_tools[name] = new ToolDefinition
			{
				Name = name,
				Description = description,
				InputSchema = inputSchema,
				Handler = handler
			};
		}

		/// <summary>
		/// Handles a JSON-RPC request string from a caller whose address is unknown
		/// </summary>
		public Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken)
		{
			return HandleRequestAsync(requestJson, null, cancellationToken);
		}

		/// <summary>
		/// Handles a JSON-RPC request string and returns a JSON-RPC response string
		/// </summary>
		/// <param name="requestJson">The JSON-RPC request</param>
		/// <param name="clientAddress">The caller's address, used to rate limit tool calls made without an access token</param>
		/// <param name="cancellationToken">Cancellation token</param>
		public async Task<string> HandleRequestAsync(string requestJson, string clientAddress, CancellationToken cancellationToken)
		{
			try
			{
				var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson);
				var response = await HandleRequestAsync(request, clientAddress, cancellationToken);
				return JsonSerializer.Serialize(response);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Error processing request");
				var errorResponse = new JsonRpcResponse
				{
					Jsonrpc = "2.0",
					Id = null,
					Error = new JsonRpcError
					{
						Code = -32603,
						Message = "Internal error",
						Data = null
					}
				};
				return JsonSerializer.Serialize(errorResponse);
			}
		}

		public async Task RunAsync(CancellationToken cancellationToken)
		{
			_logger?.LogInformation("MCP Server starting stdio transport");

			try
			{
				while (!cancellationToken.IsCancellationRequested)
				{
					// Start ReadLineAsync task
					var readLineTask = Console.In.ReadLineAsync();

					// Start cancellation task
					var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);

					// Race the two tasks
					var completedTask = await Task.WhenAny(readLineTask, cancellationTask);

					// If cancellation won, exit the loop
					if (completedTask == cancellationTask)
					{
						_logger?.LogInformation("MCP Server cancellation requested, exiting loop");
						break;
					}

					// Otherwise, get the result from ReadLineAsync
					var line = await readLineTask;
					if (line == null)
						break;

					if (string.IsNullOrWhiteSpace(line))
						continue;

					try
					{
						// The stdio transport has a single local client.
						var responseJson = await HandleRequestAsync(line, "stdio", cancellationToken);
						await Console.Out.WriteLineAsync(responseJson);
						await Console.Out.FlushAsync();
					}
					catch (Exception ex)
					{
						_logger?.LogError(ex, "Error processing request");
						var errorResponse = new JsonRpcResponse
						{
							Jsonrpc = "2.0",
							Id = null,
							Error = new JsonRpcError
							{
								Code = -32603,
								Message = "Internal error",
								Data = null
							}
						};
						var errorJson = JsonSerializer.Serialize(errorResponse);
						await Console.Out.WriteLineAsync(errorJson);
						await Console.Out.FlushAsync();
					}
				}
			}
			catch (OperationCanceledException)
			{
				_logger?.LogInformation("MCP Server cancelled");
			}
		}

		private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request, string clientAddress, CancellationToken cancellationToken)
		{
			var response = new JsonRpcResponse
			{
				Jsonrpc = "2.0",
				Id = request.Id
			};

			try
			{
				switch (request.Method)
				{
					case "initialize":
						response.Result = new
						{
							protocolVersion = "2024-11-05",
							capabilities = new
							{
								tools = new { }
							},
							serverInfo = new
							{
								name = _serverName,
								version = _serverVersion
							}
						};
						break;

					case "tools/list":
						var toolsList = new List<object>();
						foreach (var tool in _tools.Values)
						{
							toolsList.Add(new
							{
								name = tool.Name,
								description = tool.Description,
								inputSchema = tool.InputSchema
							});
						}
						response.Result = new { tools = toolsList };
						break;

					case "tools/call":
						var toolCallParams = JsonSerializer.Deserialize<ToolCallParams>(
							JsonSerializer.Serialize(request.Params));

						if (!_tools.TryGetValue(toolCallParams.Name, out var toolDef))
						{
							response.Error = new JsonRpcError
							{
								Code = -32602,
								Message = $"Tool not found: {toolCallParams.Name}",
								Data = null
							};
							return response;
						}

						object result;
						try
						{
							await EnforceRateLimitAsync(toolCallParams.Name, toolCallParams.Arguments, clientAddress);
							result = await toolDef.Handler(toolCallParams.Arguments);
						}
						catch (McpToolErrorException ex)
						{
							// Something the caller can act on (e.g. refresh an expired token): report it as the tool's
							// result, in the shape tools use for their own errors, not as a JSON-RPC internal error.
							_logger?.LogInformation("Tool {Tool} returned {ErrorCode}", toolCallParams.Name, ex.ErrorCode);
							result = new { success = false, errorCode = ex.ErrorCode, error = ex.Message };
						}

						// Newtonsoft, not System.Text.Json: tool results carry the JObject/JArray payloads ApiClient
						// deserializes, which System.Text.Json writes out as nested empty arrays.
						var resultJson = result == null ? JValue.CreateNull() : JToken.FromObject(result);

						response.Result = new
						{
							content = new[]
							{
								new
								{
									type = "text",
									text = resultJson.ToString(Newtonsoft.Json.Formatting.None)
								}
							},
							isError = IsFailedToolResult(resultJson)
						};
						break;

					case "ping":
						response.Result = new { };
						break;

					default:
						response.Error = new JsonRpcError
						{
							Code = -32601,
							Message = "Method not found",
							Data = request.Method
						};
						break;
				}
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Error handling method {Method}", request.Method);
				response.Error = new JsonRpcError
				{
					Code = -32603,
					Message = "Internal error",
					Data = null
				};
			}

			return response;
		}

		/// <summary>Tools that authenticate the caller rather than take an access token.</summary>
		private static readonly HashSet<string> TokenlessTools = new(StringComparer.Ordinal) { "authenticate", "refresh_access_token" };

		/// <summary>
		/// Limits tool calls per signed-in session, keyed by access token so that callers sharing an address (such as a
		/// hosted AI client's egress) do not share a limit. Calls made without a token, in practice authenticate and
		/// refresh_access_token, are keyed by client address and held to a tighter limit.
		/// </summary>
		private async Task EnforceRateLimitAsync(string toolName, object arguments, string clientAddress)
		{
			if (_rateLimiter == null)
				return;

			// A tool that takes no token must never be keyed by a caller-chosen token value: a fresh fake
			// accessToken per authenticate call would otherwise escape the tighter per-address limit.
			var accessToken = TokenlessTools.Contains(toolName ?? string.Empty) ? null : ReadAccessToken(arguments);
			var clientId = accessToken != null ? $"token:{Fingerprint(accessToken)}" : $"address:{clientAddress ?? "unknown"}";
			var limit = accessToken != null ? McpConfig.ToolCallsPerMinute : McpConfig.UnauthenticatedCallsPerMinute;

			if (!await _rateLimiter.IsAllowedAsync(clientId, ToolCallOperation, limit))
			{
				throw new McpToolErrorException(McpToolErrorException.RateLimited,
					$"Rate limit reached: at most {limit} tool calls per minute. Wait before calling again.");
			}
		}

		private static string ReadAccessToken(object arguments)
		{
			if (arguments is JsonElement { ValueKind: JsonValueKind.Object } element
				&& element.TryGetProperty("accessToken", out var token)
				&& token.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(token.GetString()))
			{
				return token.GetString();
			}

			return null;
		}

		/// <summary>A short hash identifying a token, so the token itself is never used as a key or written to a log.</summary>
		private static string Fingerprint(string accessToken)
		{
			return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accessToken)), 0, 8);
		}

		/// <summary>
		/// Tools report a failure as { success = false, error }. MCP clients rely on isError instead, to tell a failed
		/// call from data the model should use.
		/// </summary>
		private static bool IsFailedToolResult(JToken result)
		{
			return result is JObject obj
				&& obj["success"]?.Type == JTokenType.Boolean
				&& !obj.Value<bool>("success");
		}

		private sealed class ToolDefinition
		{
			public string Name { get; set; }
			public string Description { get; set; }
			public Dictionary<string, object> InputSchema { get; set; }
			public Func<object, Task<object>> Handler { get; set; }
		}

		private sealed class JsonRpcRequest
		{
			[JsonPropertyName("jsonrpc")]
			public string Jsonrpc { get; set; }

			[JsonPropertyName("id")]
			public object Id { get; set; }

			[JsonPropertyName("method")]
			public string Method { get; set; }

			[JsonPropertyName("params")]
			public object Params { get; set; }
		}

		private sealed class JsonRpcResponse
		{
			[JsonPropertyName("jsonrpc")]
			public string Jsonrpc { get; set; }

			[JsonPropertyName("id")]
			public object Id { get; set; }

			[JsonPropertyName("result")]
			public object Result { get; set; }

			[JsonPropertyName("error")]
			public JsonRpcError Error { get; set; }
		}

		private sealed class JsonRpcError
		{
			[JsonPropertyName("code")]
			public int Code { get; set; }

			[JsonPropertyName("message")]
			public string Message { get; set; }

			[JsonPropertyName("data")]
			public object Data { get; set; }
		}

		private sealed class ToolCallParams
		{
			[JsonPropertyName("name")]
			public string Name { get; set; }

			[JsonPropertyName("arguments")]
			public object Arguments { get; set; }
		}
	}
}
