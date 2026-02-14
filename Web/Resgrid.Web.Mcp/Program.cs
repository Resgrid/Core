﻿﻿using System;
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
							options.Dsn = ExternalErrorConfig.ExternalErrorServiceUrlForMcp;
							options.AttachStacktrace = true;
							options.SendDefaultPii = true;
							options.AutoSessionTracking = true;
							options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
							options.Environment = ExternalErrorConfig.Environment;
							options.Release = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
							options.ProfilesSampleRate = ExternalErrorConfig.SentryProfilingSampleRate;

							// Add profiling integration
							options.AddIntegration(new ProfilingIntegration());

							options.TracesSampler = samplingContext =>
							{
								if (samplingContext?.CustomSamplingContext != null)
								{
									if (samplingContext.CustomSamplingContext.TryGetValue("__HttpPath", out var httpPath))
									{
										var pathValue = httpPath?.ToString();
										if (string.Equals(pathValue, "/health/getcurrent", StringComparison.OrdinalIgnoreCase))
										{
											return 0;
										}
									}
								}

								return ExternalErrorConfig.SentryPerfSampleRate;
							};
						});
					}

					webBuilder.UseKestrel(serverOptions =>
					{
						// Configure Kestrel to listen on a specific port for health checks
						serverOptions.ListenAnyIP(5050);
					});

					webBuilder.UseStartup<Startup>();
				});
	}
}



