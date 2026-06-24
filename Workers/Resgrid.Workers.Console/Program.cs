using System;
using Autofac;
using Microsoft.Extensions.Logging;
using Quidjibo;
using Quidjibo.Autofac.Extensions;
using Quidjibo.Autofac.Modules;
using Quidjibo.DataProtection.Extensions;
using Quidjibo.Extensions;
using Quidjibo.Models;
using Quidjibo.SqlServer.Extensions;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Console.Commands;
using Resgrid.Workers.Framework;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resgrid.Workers.Console.Tasks;
using Serilog.Formatting.Json;
using Stripe;
using FluentMigrator.Runner;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Model.Repositories;
using System.Reflection;
using Sentry;
using Resgrid.Framework;
using Resgrid.Providers.MigrationsPg.Migrations;
using Quidjibo.Clients;
using Quidjibo.Postgres.Extensions;

namespace Resgrid.Workers.Console
{
	public class Program
	{
		public static IConfigurationRoot Configuration { get; private set; }

		static async Task Main(string[] args)
		{
#if DEBUG
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = true;
#endif

			System.Console.WriteLine("Resgrid Worker Engine");
			System.Console.WriteLine("-----------------------------------------");

			LoadConfiguration(args);

			Prime();

			var builder = new HostBuilder()
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
					config.AddEnvironmentVariables();

					if (args != null)
					{
						config.AddCommandLine(args);
					}
				})
				.ConfigureServices((hostContext, services) =>
				{
					services.AddOptions();

					var upgradeDatabase = Environment.GetEnvironmentVariable("RESGRID__DODBUPGRADE");
					var runDatabaseUpgrade = string.Equals(upgradeDatabase, "true", StringComparison.OrdinalIgnoreCase);
					services.AddSingleton(new DatabaseUpgradeState(runDatabaseUpgrade));

					if (runDatabaseUpgrade)
					{
						services.AddSingleton<IHostedService, DatabaseUpgradeService>();
					}

					services.AddSingleton<IHostedService, QueuesProcessingService>();
					services.AddSingleton<IHostedService, ScheduledJobsService>();

				})
				.ConfigureLogging((hostingContext, logging) => {
					logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
					logging.AddConsole();
				});

			Resgrid.Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForWebjobs);

			if (!String.IsNullOrWhiteSpace(ExternalErrorConfig.ExternalErrorServiceUrlForWebjobs))
			{
				SentrySdk.Init(options =>
				{
					options.Dsn = Config.ExternalErrorConfig.ExternalErrorServiceUrlForWebjobs;
					options.AttachStacktrace = true;
					options.SendDefaultPii = true;
					options.AutoSessionTracking = true;
					options.TracesSampleRate = ExternalErrorConfig.SentryPerfSampleRate;
					options.ProfilesSampleRate = ExternalErrorConfig.SentryProfilingSampleRate;
					options.IsGlobalModeEnabled = true;
					options.Environment = ExternalErrorConfig.Environment;
					options.Release = Assembly.GetEntryAssembly().GetName().Version.ToString();
				});
			}


			await builder.RunConsoleAsync();
		}

		private static void Prime()
		{
			System.Console.WriteLine("Initializing Dependencies...");

			if (!String.IsNullOrWhiteSpace(Configuration["DOTNET_RUNNING_IN_CONTAINER"]))
				ConfigProcessor.LoadAndProcessConfig(System.Configuration.ConfigurationManager.AppSettings["ConfigPath"]);

			SetConnectionString();

			Bootstrapper.Initialize();

			var eventAggragator = Bootstrapper.GetKernel().Resolve<IEventAggregator>();
			var workflowProvider = Bootstrapper.GetKernel().Resolve<IWorkflowEventProvider>();
			var outbound = Bootstrapper.GetKernel().Resolve<IOutboundEventProvider>();
			var coreEventService = Bootstrapper.GetKernel().Resolve<ICoreEventService>();

			SerializerHelper.WarmUpProtobufSerializer();

			System.Console.WriteLine("Finished Initializing Dependencies.");
		}

		private static void LoadConfiguration(string[] args)
		{
			System.Console.WriteLine("Loading Configuration...");

			var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
				.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
				.AddCommandLine(args)
				.AddEnvironmentVariables();

			Configuration = builder.Build();
			System.Console.WriteLine("Finished Loading Configuration.");
		}

		private static void SetConnectionString()
		{
			string configPath = Configuration["AppOptions:ConfigPath"];

			if (string.IsNullOrWhiteSpace(configPath))
				configPath = "C:\\Resgrid\\Config\\ResgridConfig.json";

			bool configResult = ConfigProcessor.LoadAndProcessConfig(configPath);
			if (configResult)
			{
				System.Console.WriteLine($"Loaded Config: {configPath}");
			}

			var settings = System.Configuration.ConfigurationManager.ConnectionStrings;
			var element = typeof(ConfigurationElement).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
			var collection = typeof(ConfigurationElementCollection).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);

			element.SetValue(settings, false);
			collection.SetValue(settings, false);

			if (!configResult)
			{
				if (settings["ResgridContext"] == null)
				{
					settings.Add(new System.Configuration.ConnectionStringSettings("ResgridContext", Configuration["ConnectionStrings:ResgridContext"]));
				}
			}
			else
			{
				if (settings["ResgridContext"] == null)
				{
					settings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));
				}
			}

			// Previously this also persisted ConnectionStrings to the on-disk .dll.config via config.Save().
			// That fails on read-only / non-root hardened (DHI) containers and is unnecessary: the in-memory
			// injection above is what downstream ConfigurationManager.ConnectionStrings consumers read
			// (this mirrors the web apps' Startup, which only inject in-memory and never write to disk).
			collection.SetValue(settings, true);
			element.SetValue(settings, true);

			ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());
		}
	}

	public sealed class DatabaseUpgradeState
	{
		private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public DatabaseUpgradeState(bool upgradeRequired)
		{
			if (!upgradeRequired)
				_completionSource.TrySetResult(true);
		}

		public Task WaitForCompletionAsync(CancellationToken cancellationToken)
		{
			return _completionSource.Task.WaitAsync(cancellationToken);
		}

		public void MarkCompleted()
		{
			_completionSource.TrySetResult(true);
		}

		public void MarkFailed(Exception ex)
		{
			_completionSource.TrySetException(ex);
		}
	}

	public class QueuesProcessingService : BackgroundService
	{
		private ILogger _logger;
		private readonly DatabaseUpgradeState _databaseUpgradeState;

		public QueuesProcessingService(ILogger<QueuesProcessingService> logger, DatabaseUpgradeState databaseUpgradeState)
		{
			_logger = logger;
			_databaseUpgradeState = databaseUpgradeState;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await _databaseUpgradeState.WaitForCompletionAsync(stoppingToken);
			_logger.Log(LogLevel.Information, "Starting Queues Event Watcher");

			Task.Run(async () =>
			{
				var queuesTask = new QueuesProcessorTask(_logger);
				await queuesTask.ProcessAsync(new QueuesProcessorCommand(4), null, stoppingToken);
			}, stoppingToken);

			return;
		}
	}

	public class ScheduledJobsService : BackgroundService
	{
		private ILogger _logger;
		private readonly DatabaseUpgradeState _databaseUpgradeState;
		private IQuidjiboClient Client { get; set; }
		private QuidjiboBuilder Builder { get; set; }

		public ScheduledJobsService(ILogger<ScheduledJobsService> logger, DatabaseUpgradeState databaseUpgradeState)
		{
			_logger = logger;
			_databaseUpgradeState = databaseUpgradeState;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			await _databaseUpgradeState.WaitForCompletionAsync(stoppingToken);

			var aes = Aes.Create();
			var key = string.Join(",", aes.Key);
			//System.Console.CancelKeyPress += (s, e) => { cancellationToken..Cancel(); };

			_logger.Log(LogLevel.Information, "Starting Scheduler");

			var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

			var logger = loggerFactory.CreateLogger<Program>();

			// Setup DI
			var containerBuilder = new ContainerBuilder();
			containerBuilder.RegisterModule(new QuidjiboModule(typeof(Program).Assembly));
			containerBuilder.RegisterInstance<ILogger>(logger);
			var container = containerBuilder.Build();

			// Setup Quidjibo
			if (WorkerConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				Builder = new QuidjiboBuilder()
									  .ConfigureLogging(loggerFactory)
									  .UseAutofac(container)
									  .UseAes(Encoding.ASCII.GetBytes(WorkerConfig.PayloadKey))
									  .UsePostgres(WorkerConfig.WorkerDbConnectionString)
									  .ConfigurePipeline(pipeline => pipeline.UseDefault());

				// Quidjibo Client
				Client = Builder.BuildClient();
			}
			else
			{
				Builder = new QuidjiboBuilder()
									  .ConfigureLogging(loggerFactory)
									  .UseAutofac(container)
									  .UseAes(Encoding.ASCII.GetBytes(WorkerConfig.PayloadKey))
									  .UseSqlServer(WorkerConfig.WorkerDbConnectionString)
									  .ConfigurePipeline(pipeline => pipeline.UseDefault());

				// Quidjibo Client
				Client = Builder.BuildClient();
			}

			_logger.Log(LogLevel.Information, "Scheduler Started");

			//// Long Running Jobs
			////await client.PublishAsync(new SystemQueueProcessorCommand(8), cancellationToken);
			////await client.PublishAsync(new QueuesProcessorCommand(4), cancellationToken);

			var isEventsOnly = Environment.GetEnvironmentVariable("RESGRID__EVENTSONLY");

			if (String.IsNullOrWhiteSpace(isEventsOnly) || isEventsOnly.ToLower() == "false")
			{
				_logger.Log(LogLevel.Information, "Starting Scheduled Jobs");
				// Scheduled Jobs

				_logger.Log(LogLevel.Information, "Scheduling Calendar Notifications");
				await Client.ScheduleAsync("Calendar Notifications",
					new CalendarNotificationCommand(1),
					Cron.MinuteIntervals(20),
					stoppingToken);

				//System.Console.WriteLine("Scheduling Calendar Notifications");
				//await client.ScheduleAsync("Call Email Import",
				//	new CallEmailImportCommand(2),
				//	Cron.MinuteIntervals(5),
				//	cancellationToken);

				_logger.Log(LogLevel.Information, "Scheduling Call Pruning");
				await Client.ScheduleAsync("Call Pruning",
					new CallPruneCommand(3),
					Cron.MinuteIntervals(60),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Report Delivery");
				await Client.ScheduleAsync("Report Delivery",
					new ReportDeliveryTaskCommand(5),
					Cron.MinuteIntervals(14),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Shift Notifier");
				await Client.ScheduleAsync("Shift Notifier",
					new ShiftNotiferCommand(6),
					Cron.MinuteIntervals(720),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Staffing Schedule");
				await Client.ScheduleAsync("Staffing Schedule",
					new Commands.StaffingScheduleCommand(7),
					Cron.MinuteIntervals(5),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Training Notifier");
				await Client.ScheduleAsync("Training Notifier",
					new TrainingNotiferCommand(9),
					Cron.MinuteIntervals(30),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Status Schedule");
				await Client.ScheduleAsync("Status Schedule",
					new Commands.StatusScheduleCommand(11),
					Cron.MinuteIntervals(5),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Dispatch Scheduled Calls");
				await Client.ScheduleAsync("Scheduled Calls",
					new Commands.StatusScheduleCommand(12),
					Cron.MinuteIntervals(5),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling OIDC Token Cleaning");
				await Client.ScheduleAsync("Clean OIDC Tokens",
					new Commands.CleanOIDCCommand(13),
					Cron.MinuteIntervals(30),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling System SQL Queue");
				await Client.ScheduleAsync("System SQL Queue",
					new Commands.SystemSqlQueueCommand(14),
					Cron.Daily(3, 0),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Security Refresh");
				await Client.ScheduleAsync("Security Refresh",
					new Commands.SecurityRefreshScheduleCommand(15),
					Cron.Daily(2, 0),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling GDPR Data Export");
				await Client.ScheduleAsync("GDPR Data Export",
					new Commands.GdprExportCommand(16),
					Cron.MinuteIntervals(5),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Communication Test");
				await Client.ScheduleAsync("Communication Test",
					new Commands.CommunicationTestCommand(17),
					Cron.MinuteIntervals(15),
					stoppingToken);

				if (!string.IsNullOrWhiteSpace(TtsConfig.ServiceBaseUrl) && !string.IsNullOrWhiteSpace(TtsConfig.StaticPromptAdminKey))
				{
					var refreshInterval = TtsConfig.StaticPromptRefreshIntervalMinutes > 0 ? TtsConfig.StaticPromptRefreshIntervalMinutes : 60;

					_logger.Log(LogLevel.Information, "Scheduling TTS Static Prompt Refresh");
					await Client.ScheduleAsync("TTS Static Prompt Refresh",
						new Commands.TtsStaticPromptRefreshCommand(18),
						Cron.MinuteIntervals(refreshInterval),
						stoppingToken);
				}

				_logger.Log(LogLevel.Information, "Scheduling Weather Alert Import");
				await Client.ScheduleAsync("Weather Alert Import",
					new Commands.WeatherAlertImportCommand(20),
					Cron.MinuteIntervals(5),
					stoppingToken);

				_logger.Log(LogLevel.Information, "Scheduling Reporting Rollup");
				await Client.ScheduleAsync("Reporting Rollup",
					new Commands.ReportingRollupCommand(21),
					Cron.Daily(3, 30),
					stoppingToken);

				// Frequent on purpose: PAR (personnel accountability) is safety-critical, so the backstop
				// sweep runs every minute. Each call's evaluation short-circuits cheaply when there's no
				// active incident command, and the alert is idempotent (timeline-deduped).
				_logger.Log(LogLevel.Information, "Scheduling PAR Evaluation");
				await Client.ScheduleAsync("PAR Evaluation",
					new Commands.ParEvaluationCommand(23),
					Cron.MinuteIntervals(1),
					stoppingToken);

				if (SystemBehaviorConfig.Utf8CleanupEnabled)
				{
					var utf8CleanupHour = SystemBehaviorConfig.Utf8CleanupHourUtc >= 0 && SystemBehaviorConfig.Utf8CleanupHourUtc <= 23
						? SystemBehaviorConfig.Utf8CleanupHourUtc
						: 4;

					_logger.Log(LogLevel.Information, "Scheduling UTF-8 Data Cleanup");
					await Client.ScheduleAsync("UTF-8 Data Cleanup",
						new Commands.Utf8CleanupCommand(22),
						Cron.Daily(utf8CleanupHour, 0),
						stoppingToken);
				}
			}
			else
			{
				_logger.Log(LogLevel.Information, "Starting in Events Only Mode!");
			}


			// Quidjibo Server
			using (var workServer = Builder.BuildServer())
			{
				// Start Quidjibo
				workServer.Start();
				stoppingToken.WaitHandle.WaitOne();
			}

			//var test = "a";
			//while (!cancellationToken.IsCancellationRequested)
			//{
			//	await Task.Delay(TimeSpan.FromSeconds(1));
			//}
		}
	}

	public class DatabaseUpgradeService : BackgroundService
	{
		private ILogger _logger;
		private readonly DatabaseUpgradeState _databaseUpgradeState;

		public DatabaseUpgradeService(ILogger<DatabaseUpgradeService> logger, DatabaseUpgradeState databaseUpgradeState)
		{
			_logger = logger;
			_databaseUpgradeState = databaseUpgradeState;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			_logger.Log(LogLevel.Information, "Starting Database Upgrade");
			var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

			var logger = loggerFactory.CreateLogger<Program>();

			try
			{
				var serviceProvider = CreateServices();

				// Put the database update into a scope to ensure
				// that all resources will be disposed.
				using (var scope = serviceProvider.CreateScope())
				{
					UpdateDatabase(scope.ServiceProvider);
					await UpdateOidcDatabaseAsync(logger, scope.ServiceProvider);
					await UpdateDocumentDatabaseAsync(logger, scope.ServiceProvider);
				}

				_databaseUpgradeState.MarkCompleted();
				_logger.Log(LogLevel.Information, "Completed updating the Resgrid Database!");
			}
			catch (Exception ex)
			{
				_databaseUpgradeState.MarkFailed(ex);
				_logger.Log(LogLevel.Error, ex, "There was an error trying to update the Resgrid Database.");
				Environment.Exit(1);
			}

		}

		/// <summary>
		/// Update the database
		/// </summary>
		private static void UpdateDatabase(IServiceProvider serviceProvider)
		{
			// Instantiate the runner
			var runner = serviceProvider.GetRequiredService<IMigrationRunner>();

			// Execute the migrations
			runner.MigrateUp();
		}

		/// <summary>
		/// Update the database
		/// </summary>
		private static async Task UpdateOidcDatabaseAsync(ILogger<Program> logger, IServiceProvider serviceProvider)
		{
			logger.Log(LogLevel.Information, "Starting OIDC Database Upgrade");

			try
			{
				var oidcRepository = Bootstrapper.GetKernel().Resolve<IOidcRepository>();
				bool result = await oidcRepository.UpdateOidcDatabaseAsync();

				if (result)
					logger.Log(LogLevel.Information, "Completed updating the OIDC Database!");
				else
					logger.Log(LogLevel.Warning, "UpdateOidcDatabaseAsync returned false; the OIDC database may not have been fully updated.");
			}
			catch (Exception ex)
			{
				logger.Log(LogLevel.Error, ex, "There was an error trying to update the OIDC Database.");
				throw;
			}
		}

		/// <summary>
		/// Update the document database
		/// </summary>
		private static async Task UpdateDocumentDatabaseAsync(ILogger<Program> logger, IServiceProvider serviceProvider)
		{
			if (Config.DataConfig.DocDatabaseType != Config.DatabaseTypes.Postgres)
				return;

			logger.Log(LogLevel.Information, "Starting Document Database Upgrade");

			try
			{
				var documentDbRepository = Bootstrapper.GetKernel().Resolve<IDocumentDbRepository>();
				bool result = await documentDbRepository.UpdateDocumentDatabaseAsync();

				if (result)
					logger.Log(LogLevel.Information, "Completed updating the Document Database!");
				else
					throw new InvalidOperationException("UpdateDocumentDatabaseAsync returned false; the document database was not fully updated.");
			}
			catch (Exception ex)
			{
				logger.Log(LogLevel.Error, ex, "There was an error trying to update the Document Database.");
				throw;
			}
		}

		private static IServiceProvider CreateServices()
		{
			if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
			{
				return new ServiceCollection()
					.AddOptions()
					// Add common FluentMigrator services
					.AddFluentMigratorCore()
					.ConfigureRunner(rb => rb
						// Add SQL Server support to FluentMigrator
						.AddPostgres11_0()
						// Set the connection string
						.WithGlobalConnectionString(Config.DataConfig.CoreConnectionString)
						// Index builds on large tables (e.g. ActionLogs) exceed the 30s default;
						// allow up to 30 minutes per command so migrations don't time out.
						.WithGlobalCommandTimeout(TimeSpan.FromMinutes(30))
						// Define the assembly containing the migrations
						.ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.Migrations().For.EmbeddedResources())
					// Enable logging to console in the FluentMigrator way
					.AddLogging(lb => lb.AddFluentMigratorConsole())
					// Build the service provider
					.BuildServiceProvider(false);
			}
			else
			{
				return new ServiceCollection()
					.AddOptions()
					// Add common FluentMigrator services
					.AddFluentMigratorCore()
					.ConfigureRunner(rb => rb
						// Add SQL Server support to FluentMigrator
						.AddSqlServer()
						// Set the connection string
						.WithGlobalConnectionString(Config.DataConfig.CoreConnectionString)
						// Index builds on large tables (e.g. ActionLogs) exceed the 30s default;
						// allow up to 30 minutes per command so migrations don't time out.
						.WithGlobalCommandTimeout(TimeSpan.FromMinutes(30))
						// Define the assembly containing the migrations
						.ScanIn(typeof(M0001_InitialMigration).Assembly).For.Migrations().For.EmbeddedResources())
					// Enable logging to console in the FluentMigrator way
					.AddLogging(lb => lb.AddFluentMigratorConsole())
					// Build the service provider
					.BuildServiceProvider(false);
			}
		}
	}
}
