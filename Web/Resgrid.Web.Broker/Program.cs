using System;
using System.Reflection;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Resgrid.Config;
using Resgrid.Providers.ProtectedData;

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
					webBuilder.ConfigureKestrel(ConfigureHttpsCertificate);

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
							options.AddExceptionFilter(new Resgrid.Framework.UserFacingExceptionSentryFilter());
						});
					}
				});

		/// <summary>
		/// Serves the HTTPS listener's certificate from <see cref="DataProtectionConfig.BrokerHttpsCertificatePath"/> and
		/// picks up a renewal without a restart. Kestrel resolves Kestrel:Certificates:Default exactly once, and the
		/// step-ca sidecar replaces a roughly day-long leaf in place, so a certificate loaded at startup expires under
		/// a running broker. Endpoints still come from ASPNETCORE_URLS (e.g. https://+:8443); this only supplies the
		/// certificate for the https ones. With no path configured, nothing changes.
		///
		/// Kestrel builds its options when the server starts, after ConfigureAppConfiguration has loaded the static
		/// configuration, so DataProtectionConfig is populated here.
		/// </summary>
		private static void ConfigureHttpsCertificate(KestrelServerOptions kestrel)
		{
			if (string.IsNullOrWhiteSpace(DataProtectionConfig.BrokerHttpsCertificatePath))
				return;

			// Throws when the file cannot be loaded the first time, so the broker stops at startup rather than
			// accepting connections it can never complete.
			var certificate = new ReloadingPkcs12Certificate(
				DataProtectionConfig.BrokerHttpsCertificatePath,
				DataProtectionConfig.BrokerHttpsCertificatePassword,
				ReloadingPkcs12Certificate.ConfiguredPollInterval,
				TimeProvider.System);

			var current = certificate.Current;
			Framework.Logging.LogInfo(
				$"Protected Data Broker HTTPS certificate loaded from '{certificate.Path}' ({current.Subject}), valid through {current.NotAfter.ToUniversalTime():u}; checked for renewal every {ReloadingPkcs12Certificate.ConfiguredPollInterval.TotalSeconds:0} s.");

			kestrel.ConfigureHttpsDefaults(https => https.ServerCertificateSelector = (_, _) => certificate.Current);
		}
	}
}
