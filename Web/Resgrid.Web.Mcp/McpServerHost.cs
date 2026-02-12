﻿﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Resgrid.Config;

namespace Resgrid.Web.Mcp
{
	/// <summary>
	/// Hosted service that runs the MCP server
	/// </summary>
	public sealed class McpServerHost : IHostedService, IDisposable
	{
		private readonly ILogger<McpServerHost> _logger;
		private readonly McpToolRegistry _toolRegistry;
		private readonly IHostApplicationLifetime _applicationLifetime;
		private McpServer _mcpServer;
		private Task _executingTask;
		private CancellationTokenSource _stoppingCts;
		private bool _disposed;

		public McpServerHost(
			ILogger<McpServerHost> logger,
			McpToolRegistry toolRegistry,
			IHostApplicationLifetime applicationLifetime)
		{
			_logger = logger;
			_toolRegistry = toolRegistry;
			_applicationLifetime = applicationLifetime;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Starting Resgrid MCP Server...");

			_stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			try
			{
				// Use McpConfig for server configuration
				var serverName = McpConfig.ServerName;
				var serverVersion = McpConfig.ServerVersion;

				// Create MCP server with server information
				_mcpServer = new McpServer(serverName, serverVersion, _logger);

				// Register all tools from the registry
				_toolRegistry.RegisterTools(_mcpServer);

				_logger.LogInformation("MCP Server initialized with {ToolCount} tools", _toolRegistry.GetToolCount());

				// Start the server execution
				_executingTask = ExecuteAsync(_stoppingCts.Token);

				if (_executingTask.IsCompleted)
				{
					return _executingTask;
				}

				_logger.LogInformation("Resgrid MCP Server started successfully");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to start MCP Server");
				throw;
			}

			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Stopping Resgrid MCP Server...");

			if (_executingTask == null)
			{
				return;
			}

			try
			{
				_stoppingCts?.Cancel();
			}
			finally
			{
				await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));

				_stoppingCts?.Dispose();
				_stoppingCts = null;
			}

			_logger.LogInformation("Resgrid MCP Server stopped");
		}

		private async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				_logger.LogInformation("MCP Server listening on stdio transport...");

				// Run the server - this will handle stdio communication
				await _mcpServer.RunAsync(stoppingToken);
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("MCP Server execution was cancelled");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unexpected error in MCP Server execution");
				_applicationLifetime.StopApplication();
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_stoppingCts?.Dispose();
			_disposed = true;
		}
	}
}



