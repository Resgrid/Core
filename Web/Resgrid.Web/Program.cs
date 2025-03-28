using System.IO;
using System.Reflection;
using System.Threading;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Sentry.Profiling;
using PostHog;

namespace Resgrid.Web
{
	public class Program
	{
		public static void Main(string[] args)
		{
			ThreadPool.SetMinThreads(200, 200);
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
					var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
					.SetBasePath(Directory.GetCurrentDirectory())
					.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
					.AddEnvironmentVariables();
					var config = builder.Build();

					bool configResult = ConfigProcessor.LoadAndProcessConfig(config["AppOptions:ConfigPath"]);
					bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(config.AsEnumerable());

					if (!string.IsNullOrWhiteSpace(Config.ExternalErrorConfig.ExternalErrorServiceUrlForWebsite))
					{
						webBuilder.UseSentry(options =>
						{
							//options.MinimumBreadcrumbLevel = LogEventLevel.Debug;
							//options.MinimumEventLevel = LogEventLevel.Error;
							options.Dsn = Config.ExternalErrorConfig.ExternalErrorServiceUrlForWebsite;
							options.AttachStacktrace = true;
							options.SendDefaultPii = true;

							//if (ExternalErrorConfig.SentryPerfSampleRate > 0)
							//	options.EnableTracing = true;

							options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
							options.Environment = ExternalErrorConfig.Environment;
							options.AutoSessionTracking = true;
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
									    samplingContext.CustomSamplingContext["__HttpPath"].ToString().ToLower() == "/health/getcurrent")
									{
										return 0;
									}
								}

								return ExternalErrorConfig.SentryPerfSampleRate;
							};
						});
					}

					webBuilder.UseStartup<Startup>();
				});
	}
}
