using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Resgrid.Config;
using Sentry;

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

			// Add Sentry breadcrumb for startup
			SentrySdk.AddBreadcrumb("MCP Server starting", "server.lifecycle", level: BreadcrumbLevel.Info);

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

				var toolCount = _toolRegistry.GetToolCount();
				_logger.LogInformation("MCP Server initialized with {ToolCount} tools", toolCount);

				// Add Sentry breadcrumb for successful initialization
				SentrySdk.AddBreadcrumb(
					$"MCP Server initialized with {toolCount} tools",
					"server.lifecycle",
					data: new System.Collections.Generic.Dictionary<string, string>
					{
						{ "server_name", serverName },
						{ "server_version", serverVersion },
						{ "tool_count", toolCount.ToString() }
					},
					level: BreadcrumbLevel.Info);

				// Start the server execution
				_executingTask = ExecuteAsync(_stoppingCts.Token);

				if (_executingTask.IsCompleted)
				{
					return _executingTask;
				}

				_logger.LogInformation("Resgrid MCP Server started successfully");
				SentrySdk.AddBreadcrumb("MCP Server started successfully", "server.lifecycle", level: BreadcrumbLevel.Info);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to start MCP Server");

				// Capture exception in Sentry
				SentrySdk.CaptureException(ex, scope =>
				{
					scope.SetTag("component", "McpServerHost");
					scope.SetTag("operation", "StartAsync");
					scope.Level = SentryLevel.Fatal;
				});

				throw;
			}

			return Task.CompletedTask;
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			_logger.LogInformation("Stopping Resgrid MCP Server...");
			SentrySdk.AddBreadcrumb("MCP Server stopping", "server.lifecycle", level: BreadcrumbLevel.Info);

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
			SentrySdk.AddBreadcrumb("MCP Server stopped", "server.lifecycle", level: BreadcrumbLevel.Info);
		}

		private async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			// Start a Sentry transaction for the MCP server execution
			var transaction = SentrySdk.StartTransaction("mcp.server.execution", "mcp.lifecycle");
			var transactionFinished = false;

			try
			{
				_logger.LogInformation("MCP Server listening on stdio transport...");
				SentrySdk.AddBreadcrumb("MCP Server started listening", "server.lifecycle", level: BreadcrumbLevel.Info);

				// Run the server - this will handle stdio communication
				await _mcpServer.RunAsync(stoppingToken);

				transaction.Status = SpanStatus.Ok;
			}
			catch (OperationCanceledException)
			{
				_logger.LogInformation("MCP Server execution was cancelled");
				SentrySdk.AddBreadcrumb("MCP Server execution cancelled", "server.lifecycle", level: BreadcrumbLevel.Info);
				transaction.Status = SpanStatus.Cancelled;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Unexpected error in MCP Server execution");

				// Capture exception in Sentry with context
				SentrySdk.CaptureException(ex, scope =>
				{
					scope.SetTag("component", "McpServerHost");
					scope.SetTag("operation", "ExecuteAsync");
					scope.Level = SentryLevel.Fatal;
					scope.AddBreadcrumb("Server execution failed", "server.error", level: BreadcrumbLevel.Error);
				});

				transaction.Status = SpanStatus.InternalError;
				transaction.Finish(ex);
				transactionFinished = true;

				_applicationLifetime.StopApplication();
				return;
			}
			finally
			{
				if (!transactionFinished)
				{
					transaction.Finish();
				}
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



