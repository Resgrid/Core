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

namespace Resgrid.Workers.Console
{
	public class Program
	{
		public static void Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Worker Engine");
			System.Console.WriteLine("-----------------------------------------");

			Prime();

			var aes = Aes.Create();
			var key = string.Join(",", aes.Key);
			var cts = new CancellationTokenSource();
			MainAsync(args, cts.Token).GetAwaiter().GetResult();
			System.Console.CancelKeyPress += (s, e) => { cts.Cancel(); };
			cts.Token.WaitHandle.WaitOne();

			System.Console.ReadLine();
		}

		private static async Task MainAsync(string[] args, CancellationToken cancellationToken)
		{
			var loggerFactory = new LoggerFactory().AddConsole(LogLevel.Debug);
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
			await client.PublishAsync(new SystemQueueProcessorCommand(8), cancellationToken);
			await client.PublishAsync(new QueuesProcessorCommand(4), cancellationToken);


			// Scheduled Jobs
			await client.ScheduleAsync("Calendar Notifications",
						   new CalendarNotificationCommand(1),
						   Cron.MinuteIntervals(20),
						   cancellationToken);

			await client.ScheduleAsync("Call Email Import",
						   new CallEmailImportCommand(2),
						   Cron.MinuteIntervals(5),
						   cancellationToken);

			await client.ScheduleAsync("Call Pruning",
						   new CallPruneCommand(3),
						   Cron.MinuteIntervals(60),
						   cancellationToken);

			await client.ScheduleAsync("Report Delivery",
						   new ReportDeliveryTaskCommand(5),
						   Cron.MinuteIntervals(60),
						   cancellationToken);

			await client.ScheduleAsync("Shift Notifier",
						   new ShiftNotiferCommand(6),
						   Cron.MinuteIntervals(60),
						   cancellationToken);

			await client.ScheduleAsync("Staffing Schedule",
						   new Commands.StaffingScheduleCommand(7),
						   Cron.MinuteIntervals(15),
						   cancellationToken);
			

			await client.ScheduleAsync("Training Notifier",
						   new TrainingNotiferCommand(9),
						   Cron.MinuteIntervals(60),
						   cancellationToken);
			
			// Quidjibo Server
			using (var workServer = quidjiboBuilder.BuildServer())
			{
				// Start Quidjibo
				workServer.Start();
				cancellationToken.WaitHandle.WaitOne();
			}
		}

		private static void Prime()
		{
			SetConnectionString();

			Bootstrapper.Initialize();

			var eventAggragator = Bootstrapper.GetKernel().Resolve<IEventAggregator>();
			var outbound = Bootstrapper.GetKernel().Resolve<IOutboundEventProvider>();
			var coreEventService = Bootstrapper.GetKernel().Resolve<ICoreEventService>();

			SerializerHelper.WarmUpProtobufSerializer();
		}

		private static void SetConnectionString()
		{
			var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			var connectionStringsSection = (ConnectionStringsSection)config.GetSection("connectionStrings");

			if (connectionStringsSection.ConnectionStrings["ResgridContext"] != null)
				connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString = DataConfig.ConnectionString;
			else
				connectionStringsSection.ConnectionStrings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			config.Save();
			ConfigurationManager.RefreshSection("connectionStrings");
		}
	}
}
