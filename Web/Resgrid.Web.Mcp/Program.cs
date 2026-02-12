using System;
using System.IO;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.Config;

namespace Resgrid.Web.Mcp
{
	public static class Program
	{
		public static async Task<int> Main(string[] args)
		{
			try
			{
				var host = CreateHostBuilder(args).Build();
				await host.RunAsync();
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Fatal error: {ex}");
				return 1;
			}
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.UseContentRoot(Directory.GetCurrentDirectory())
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.SetBasePath(Directory.GetCurrentDirectory())
						.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
						.AddEnvironmentVariables()
						.AddCommandLine(args);
				})
				.ConfigureLogging((hostingContext, logging) =>
				{
					logging.ClearProviders();
					logging.AddConsole();
					logging.SetMinimumLevel(LogLevel.Information);
				})
				.ConfigureServices((hostContext, services) =>
				{
					var configuration = hostContext.Configuration;

					// Load Resgrid configuration
					bool configResult = ConfigProcessor.LoadAndProcessConfig(configuration["AppOptions:ConfigPath"]);
					bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(configuration.AsEnumerable());

					if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForApi))
					{
						Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForApi);
					}

					// Register MCP server
					services.AddHostedService<McpServerHost>();

					// Register infrastructure services
					services.AddMemoryCache();
					services.AddSingleton<Infrastructure.IResponseCache, Infrastructure.ResponseCache>();
					services.AddSingleton<Infrastructure.IRateLimiter, Infrastructure.RateLimiter>();
					services.AddSingleton<Infrastructure.ITokenRefreshService, Infrastructure.TokenRefreshService>();
					services.AddSingleton<Infrastructure.IAuditLogger, Infrastructure.AuditLogger>();

					// Register HTTP client for API calls with connection pooling
					services.AddHttpClient("ResgridApi", client =>
					{
						var apiBaseUrl = configuration["McpServer:ApiBaseUrl"];
						if (!string.IsNullOrWhiteSpace(apiBaseUrl))
						{
							client.BaseAddress = new Uri(apiBaseUrl);
						}
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
					services.AddSingleton<Tools.AuthenticationToolProvider>();
					services.AddSingleton<Tools.CallsToolProvider>();
					services.AddSingleton<Tools.DispatchToolProvider>();
					services.AddSingleton<Tools.PersonnelToolProvider>();
					services.AddSingleton<Tools.UnitsToolProvider>();
					services.AddSingleton<Tools.MessagesToolProvider>();
					services.AddSingleton<Tools.CalendarToolProvider>();
					services.AddSingleton<Tools.ShiftsToolProvider>();
					services.AddSingleton<Tools.InventoryToolProvider>();
					services.AddSingleton<Tools.ReportsToolProvider>();
					services.AddSingleton<McpToolRegistry>();
				});
	}
}



