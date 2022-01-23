using Autofac;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Framework;
using SimpleInjector;
using System.Configuration;
using Consolas2.Core;
using Consolas2.ViewEngines;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Resgrid.Console
{
	public class Program : ConsoleApp<Program>
	{
		public static IConfigurationRoot Configuration { get; private set; }

		static void Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Console");
			System.Console.WriteLine("-----------------------------------------");

			LoadConfiguration(args);
			Prime();

			Match(args);
		}

		public override void Configure(Container container)
		{
			container.Register<IConsole, SystemConsole>();


			ViewEngines.Add<StubbleViewEngine>();
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
			var config = System.Configuration.ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			var connectionStringsSection = (ConnectionStringsSection)config.GetSection("connectionStrings");

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

			if (connectionStringsSection.ConnectionStrings["ResgridContext"] != null)
				connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString = DataConfig.ConnectionString;
			else
				connectionStringsSection.ConnectionStrings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			config.Save();
			System.Configuration.ConfigurationManager.RefreshSection("connectionStrings");
			collection.SetValue(settings, true);
			element.SetValue(settings, true);
		}
	}
}
