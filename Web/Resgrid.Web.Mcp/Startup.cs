using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Web.Mcp.Infrastructure;
using Resgrid.Web.Mcp.Tools;

namespace Resgrid.Web.Mcp
{
	public class Startup
	{
		public IConfiguration Configuration { get; }

		public Startup(IConfiguration configuration)
		{
			Configuration = configuration;
		}

		public void ConfigureServices(IServiceCollection services)
		{
			// Initialize Resgrid logging framework with Sentry if available
			if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForMcp))
			{
				Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForMcp);
			}

			// Register MCP Server as a singleton for HTTP access
			services.AddSingleton<ModelContextProtocol.Server.McpServer>(sp =>
			{
				var logger = sp.GetRequiredService<ILogger<ModelContextProtocol.Server.McpServer>>();
				var serverName = McpConfig.ServerName;
				var serverVersion = McpConfig.ServerVersion;
				var mcpServer = new ModelContextProtocol.Server.McpServer(serverName, serverVersion, logger);

				// Register tools with the server
				var toolRegistry = sp.GetRequiredService<McpToolRegistry>();
				toolRegistry.RegisterTools(mcpServer);

				return mcpServer;
			});

			// Register IMcpRequestHandler interface
			services.AddSingleton<ModelContextProtocol.Server.IMcpRequestHandler>(sp =>
				sp.GetRequiredService<ModelContextProtocol.Server.McpServer>());

			// Register MCP server hosted service (for stdio transport)
			// Only enable if configured
			if (McpConfig.EnableStdioTransport)
			{
				services.AddHostedService<McpServerHost>();
			}

			// Add MVC controllers for MCP and health check endpoints
			services.AddControllers()
				.AddNewtonsoftJson();

			// Add CORS support for browser-based MCP clients
			if (McpConfig.EnableCors)
			{
				services.AddCors(options =>
				{
					options.AddPolicy("McpCorsPolicy", builder =>
					{
						if (string.IsNullOrWhiteSpace(McpConfig.CorsAllowedOrigins) || McpConfig.CorsAllowedOrigins == "*")
						{
							// Allow all origins (development/testing)
							builder
								.AllowAnyOrigin()
								.AllowAnyMethod()
								.AllowAnyHeader();
						}
						else
						{
							// Specific origins (production)
							var origins = McpConfig.CorsAllowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries);
							builder
								.WithOrigins(origins)
								.AllowAnyMethod()
								.AllowAnyHeader()
								.AllowCredentials();
						}
					});
				});
			}

			// Register infrastructure services
			services.AddMemoryCache();
			services.AddSingleton<IResponseCache, ResponseCache>();
			services.AddSingleton<IRateLimiter, RateLimiter>();
			services.AddSingleton<ITokenRefreshService, TokenRefreshService>();
			services.AddSingleton<IAuditLogger, AuditLogger>();

			// Validate required API configuration from SystemBehaviorConfig
			if (string.IsNullOrWhiteSpace(SystemBehaviorConfig.ResgridApiBaseUrl))
			{
				throw new InvalidOperationException(
					"SystemBehaviorConfig.ResgridApiBaseUrl is required but not configured. " +
					"Configure this setting via the Resgrid configuration file or environment variables (RESGRID:SystemBehaviorConfig:ResgridApiBaseUrl).");
			}

			// Register HTTP client for API calls with connection pooling
			services.AddHttpClient("ResgridApi", client =>
			{
				client.BaseAddress = new Uri(SystemBehaviorConfig.ResgridApiBaseUrl);
				client.DefaultRequestHeaders.Add("Accept", "application/json");
				client.Timeout = TimeSpan.FromSeconds(30);
			})
			.ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
			{
				PooledConnectionLifetime = TimeSpan.FromMinutes(5),
				PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
				MaxConnectionsPerServer = 10
			});

			// Register API client
			services.AddSingleton<IApiClient, ApiClient>();

			// Register tool providers
			services.AddSingleton<AuthenticationToolProvider>();
			services.AddSingleton<CallsToolProvider>();
			services.AddSingleton<DispatchToolProvider>();
			services.AddSingleton<PersonnelToolProvider>();
			services.AddSingleton<UnitsToolProvider>();
			services.AddSingleton<MessagesToolProvider>();
			services.AddSingleton<CalendarToolProvider>();
			services.AddSingleton<ShiftsToolProvider>();
			services.AddSingleton<InventoryToolProvider>();
			services.AddSingleton<ReportsToolProvider>();
			services.AddSingleton<McpToolRegistry>();
		}

		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			// Enable CORS if configured
			if (McpConfig.EnableCors)
			{
				app.UseCors("McpCorsPolicy");
			}

			app.UseRouting();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
			});
		}
	}
}


