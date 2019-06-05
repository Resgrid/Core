using Autofac;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Framework;
using SmtpServer;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.EmailProcessor
{
	class Program
	{
		static async Task Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Email Processor");
			System.Console.WriteLine("-----------------------------------------");

			Prime();

			var options = new SmtpServerOptionsBuilder()
								.ServerName("localhost")
								.Port(25, 587)
								.MessageStore(new SampleMessageStore())
								.Build();

			var smtpServer = new SmtpServer.SmtpServer(options);
			await smtpServer.StartAsync(CancellationToken.None);
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
