﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ModelContextProtocol.Server
{
	/// <summary>
	/// Simple MCP Server implementation based on the Model Context Protocol specification
	/// </summary>
	public sealed class McpServer : IMcpRequestHandler
	{
		private readonly string _serverName;
		private readonly string _serverVersion;
		private readonly Dictionary<string, ToolDefinition> _tools;
		private readonly ILogger _logger;

		public McpServer(string serverName, string serverVersion, ILogger logger = null)
		{
			_serverName = serverName;
			_serverVersion = serverVersion;
			_tools = new Dictionary<string, ToolDefinition>();
			_logger = logger;
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
		/// Handles a JSON-RPC request string and returns a JSON-RPC response string
		/// </summary>
		public async Task<string> HandleRequestAsync(string requestJson, CancellationToken cancellationToken)
		{
			try
			{
				var request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson);
				var response = await HandleRequestAsync(request, cancellationToken);
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
						var responseJson = await HandleRequestAsync(line, cancellationToken);
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

		private async Task<JsonRpcResponse> HandleRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
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

						var result = await toolDef.Handler(toolCallParams.Arguments);
						response.Result = new
						{
							content = new[]
							{
								new
								{
									type = "text",
									text = JsonSerializer.Serialize(result)
								}
							}
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
