﻿using System;
using System.IO;
using System.Reflection;
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
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.UseContentRoot(Directory.GetCurrentDirectory())
				.ConfigureLogging(logging =>
				{
					logging.ClearProviders();
					logging.AddConsole();
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					var builder = new ConfigurationBuilder()
						.SetBasePath(Directory.GetCurrentDirectory())
						.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
						.AddEnvironmentVariables();
					var config = builder.Build();

					bool configResult = ConfigProcessor.LoadAndProcessConfig(config["AppOptions:ConfigPath"]);
					bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(config.AsEnumerable());

					if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForMcp))
					{
						webBuilder.UseSentry(options =>
						{
							//options.MinimumBreadcrumbLevel = LogEventLevel.Debug;
							//options.MinimumEventLevel = LogEventLevel.Error;
							options.Dsn = ExternalErrorConfig.ExternalErrorServiceUrlForMcp;
							options.AttachStacktrace = true;
							options.SendDefaultPii = true;
							options.AutoSessionTracking = true;

							//if (ExternalErrorConfig.SentryPerfSampleRate > 0)
							//	options.EnableTracing = true;

							options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
							options.Environment = ExternalErrorConfig.Environment;
							options.Release = Assembly.GetEntryAssembly().GetName().Version.ToString();
							options.ProfilesSampleRate = ExternalErrorConfig.SentryProfilingSampleRate;

							// Requires NuGet package: Sentry.Profiling
							// Note: By default, the profiler is initialized asynchronously. This can be tuned by passing a desired initialization timeout to the constructor.
							options.AddIntegration(new ProfilingIntegration(
							// During startup, wait up to 500ms to profile the app startup code. This could make launching the app a bit slower so comment it out if your prefer profiling to start asynchronously
							//TimeSpan.FromMilliseconds(500)
							));

							options.TracesSampler = samplingContext =>
							{
								if (samplingContext != null && samplingContext.CustomSamplingContext != null)
								{
									if (samplingContext.CustomSamplingContext.ContainsKey("__HttpPath") &&
									    samplingContext.CustomSamplingContext["__HttpPath"]?.ToString().ToLower() == "/health/getcurrent")
									{
										return 0;
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



