using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

			// Register MCP server
			services.AddHostedService<McpServerHost>();

			// Add MVC controllers for health check endpoint
			services.AddControllers()
				.AddNewtonsoftJson();

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
			app.UseRouting();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();
			});
		}
	}
}


