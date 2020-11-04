using System;
using System.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quidjibo;
using Quidjibo.Autofac.Extensions;
using Quidjibo.Autofac.Modules;
using Quidjibo.DataProtection.Extensions;
using Quidjibo.Extensions;
using Quidjibo.SqlServer.Extensions;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Events.Console.Commands;
using Resgrid.Workers.Framework;

namespace Resgrid.Workers.Events.Console
{
	public class Program
	{
		public static IConfigurationRoot Configuration { get; private set; }

		static async Task Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Worker Engine (Events)");
			System.Console.WriteLine("-----------------------------------------");

			LoadConfiguration(args);
			Prime();

			var aes = Aes.Create();
			var key = string.Join(",", aes.Key);
			var cts = new CancellationTokenSource();
			var cancellationToken = cts.Token;
			System.Console.CancelKeyPress += (s, e) => { cts.Cancel(); };

			var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

			var logger = loggerFactory.CreateLogger<Program>();

			// Setup DI
			var containerBuilder = new ContainerBuilder();
			containerBuilder.RegisterModule(new QuidjiboModule(typeof(Program).Assembly));
			containerBuilder.RegisterInstance<ILogger>(logger);
			var container = containerBuilder.Build();
			
			// Setup Quidjibo
			var quidjiboBuilder = new QuidjiboBuilder()
								  .ConfigureLogging(loggerFactory)
								  .UseAutofac(container)
								  .UseAes(Encoding.ASCII.GetBytes(WorkerConfig.PayloadKey))
								  .UseSqlServer(WorkerConfig.WorkerDbConnectionString)
								  .ConfigurePipeline(pipeline => pipeline.UseDefault());

			// Quidjibo Client
			var client = quidjiboBuilder.BuildClient();

			// Long Running Jobs
			await client.PublishAsync(new SystemQueueProcessorCommand(50), cancellationToken);
			await client.PublishAsync(new QueuesProcessorCommand(51), cancellationToken);

			// Quidjibo Server
			using (var workServer = quidjiboBuilder.BuildServer())
			{
				// Start Quidjibo
				workServer.Start();
				cancellationToken.WaitHandle.WaitOne();
			}

			System.Console.ReadLine();
		}

		private static void Prime()
		{
			System.Console.WriteLine("Initializing Dependencies...");

			if (!String.IsNullOrWhiteSpace(Configuration["DOTNET_RUNNING_IN_CONTAINER"]))
				ConfigProcessor.LoadAndProcessConfig(ConfigurationManager.AppSettings["ConfigPath"]);

			SetConnectionString();

			Bootstrapper.Initialize();

			var eventAggragator = Bootstrapper.GetKernel().Resolve<IEventAggregator>();
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
			var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			var connectionStringsSection = (ConnectionStringsSection)config.GetSection("connectionStrings");

			//var test = Configuration["ConnectionStrings:ResgridContext"];

			ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

			if (connectionStringsSection.ConnectionStrings["ResgridContext"] != null)
				connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString = DataConfig.ConnectionString;
			else
				connectionStringsSection.ConnectionStrings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			config.Save();
			ConfigurationManager.RefreshSection("connectionStrings");
		}
	}
}
