using Autofac;
using Consolas.Core;
using Consolas.Mustache;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Framework;
using SimpleInjector;
using System.Configuration;

namespace Resgrid.Console
{
	public class Program : ConsoleApp<Program>
	{
		static void Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Console");
			System.Console.WriteLine("-----------------------------------------");

			Prime();

			Match(args);
		}

		public override void Configure(Container container)
		{
			container.Register<IConsole, SystemConsole>();


			ViewEngines.Add<MustacheViewEngine>();
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
