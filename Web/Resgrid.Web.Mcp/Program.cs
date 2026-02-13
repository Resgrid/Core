using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Sentry.Profiling;

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
				.ConfigureWebHostDefaults(webBuilder =>
				{
					// Load configuration first
					var builder = new ConfigurationBuilder()
						.SetBasePath(Directory.GetCurrentDirectory())
						.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
						.AddEnvironmentVariables();
					var config = builder.Build();

					bool configResult = ConfigProcessor.LoadAndProcessConfig(config["AppOptions:ConfigPath"]);
					bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(config.AsEnumerable());

					// Configure Sentry if DSN is provided
					if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForMcp))
					{
						webBuilder.UseSentry(options =>
						{
							options.Dsn = ExternalErrorConfig.ExternalErrorServiceUrlForMcp;
							options.AttachStacktrace = true;
							options.SendDefaultPii = true;
							options.AutoSessionTracking = true;
							options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
							options.Environment = ExternalErrorConfig.Environment;
							options.Release = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
							options.ProfilesSampleRate = ExternalErrorConfig.SentryProfilingSampleRate;

							// Add profiling integration
							options.AddIntegration(new ProfilingIntegration());

							// Custom trace sampling to exclude health check endpoints
							options.TracesSampler = samplingContext =>
							{
								if (samplingContext?.CustomSamplingContext != null &&
								    samplingContext.CustomSamplingContext.ContainsKey("__HttpPath"))
								{
									var path = samplingContext.CustomSamplingContext["__HttpPath"]?.ToString()?.ToLower();
									if (path == "/health/getcurrent" ||
									    path == "/health" ||
									    path == "/api/health/getcurrent")
									{
										return 0; // Don't sample health checks
									}
								}

								return ExternalErrorConfig.SentryPerfSampleRate;
							};
						});
					}

					webBuilder.UseKestrel(serverOptions =>
					{
						// Configure Kestrel to listen on a specific port for health checks
						serverOptions.ListenAnyIP(5050); // Health check port
					});
					webBuilder.Configure(app =>
					{
						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapControllers();
						});
					});
				})
				.ConfigureAppConfiguration((_, config) =>
				{
					config.SetBasePath(Directory.GetCurrentDirectory())
						.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
						.AddEnvironmentVariables()
						.AddCommandLine(args);
				})
				.ConfigureLogging((_, logging) =>
				{
					logging.ClearProviders();
					logging.AddConsole();
					logging.SetMinimumLevel(LogLevel.Information);
				})
				.ConfigureServices((hostContext, services) =>
				{
					var configuration = hostContext.Configuration;

					// Configuration is already loaded in ConfigureWebHostDefaults
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
					services.AddSingleton<Infrastructure.IResponseCache, Infrastructure.ResponseCache>();
					services.AddSingleton<Infrastructure.IRateLimiter, Infrastructure.RateLimiter>();
					services.AddSingleton<Infrastructure.ITokenRefreshService, Infrastructure.TokenRefreshService>();
					services.AddSingleton<Infrastructure.IAuditLogger, Infrastructure.AuditLogger>();

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



