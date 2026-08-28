using System.Reflection;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Resgrid.Config;

namespace Resgrid.Web.Broker
{
	/// <summary>
	/// Protected Data Broker host (ADP plan sections 2.1-2.2): the only deployable with a KMS route
	/// and a real IKeyWrappingProvider. Deploy it on an isolated subnet — application/worker hosts
	/// may reach the broker's HTTP endpoints; nothing but the broker reaches OpenBao. Never
	/// co-locate this process with Web, API, worker, or BackOffice hosts.
	/// </summary>
	public class Program
	{
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
					config.AddEnvironmentVariables();

					var built = config.Build();
					ConfigProcessor.LoadAndProcessConfig(built["AppOptions:ConfigPath"]);
					ConfigProcessor.LoadAndProcessEnvVariables(built.AsEnumerable());
				})
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.UseStartup<Startup>();

					if (!string.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForBroker))
					{
						webBuilder.UseSentry(options =>
						{
							options.Dsn = ExternalErrorConfig.ExternalErrorServiceUrlForBroker;
							options.AttachStacktrace = true;
							// This host decrypts protected values: keep request bodies and PII out of
							// telemetry entirely.
							options.SendDefaultPii = false;
							options.MaxRequestBodySize = Sentry.Extensibility.RequestSize.None;
							options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
							options.Environment = ExternalErrorConfig.Environment;
							options.Release = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
						});
					}
				});
	}
}
