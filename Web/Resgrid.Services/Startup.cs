using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Microsoft.Owin.Cors;
using Owin;
using Resgrid.Services;
using Resgrid.Web.Services.App_Start;
using Autofac.Integration.WebApi;
using System.Web.Cors;
using System.Threading.Tasks;
using Stripe;
using System.Configuration;
using WebApiThrottle;
using Resgrid.Config;
using System.Reflection;
using Microsoft.ApplicationInsights.Extensibility;

[assembly: OwinStartup(typeof(Resgrid.Web.Services.Startup))]
namespace Resgrid.Web.Services
{
	public partial class Startup
	{
		//public void Configuration(IAppBuilder app, IDependencyResolver resolver = null)
		public void Configuration(IAppBuilder app)
		{
			string configConnectionString = ConfigurationManager.ConnectionStrings["ResgridContext"].ToString();
			bool configResult = ConfigProcessor.LoadAndProcessConfig(ConfigurationManager.AppSettings["ConfigPath"]);

			var configManager = System.Web.Configuration.WebConfigurationManager.OpenWebConfiguration("~");
			var connectionStringsSection = (ConnectionStringsSection)configManager.GetSection("connectionStrings");

			if (!configResult)
			{
				if (connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString != configConnectionString)
				{
					connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString = configConnectionString;
					configManager.Save();
					ConfigurationManager.RefreshSection("connectionStrings");
				}
			}
			else
			{
				if (connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString != DataConfig.ConnectionString)
				{
					connectionStringsSection.ConnectionStrings["ResgridContext"].ConnectionString = DataConfig.ConnectionString;
					configManager.Save();
					ConfigurationManager.RefreshSection("connectionStrings");
				}
			}

			WebBootstrapper.Initialize();

			Framework.Logging.Initialize(Config.ExternalErrorConfig.ExternalErrorServiceUrlForWebsite);

			var config = GlobalConfiguration.Configuration;
			WebApiConfig.Register(config);

			config.DependencyResolver = new AutofacWebApiDependencyResolver(WebBootstrapper.GetKernel());
			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.Routes);
			BundleConfig.RegisterBundles(BundleTable.Bundles);

			if (!String.IsNullOrWhiteSpace(Config.ServiceBusConfig.SignalRServiceBusConnectionString))
				GlobalHost.DependencyResolver.UseServiceBus(Config.ServiceBusConfig.SignalRServiceBusConnectionString, Config.ServiceBusConfig.SignalRTopicName);

			// Branch the pipeline here for requests that start with "/signalr"
			app.Map("/signalr", map =>
			{
				// Setup the CORS middleware to run before SignalR.
				// By default this will allow all origins. You can 
				// configure the set of origins and/or http verbs by
				// providing a cors options with a different policy.
				//map.UseCors(CorsOptions.AllowAll);
				map.UseCors(SignalrCorsOptions.Value);
				var hubConfiguration = new HubConfiguration
				{
					// You can enable JSONP by uncommenting line below.
					// JSONP requests are insecure but some older browsers (and some
					// versions of IE) require JSONP to work cross domain
					EnableJSONP = true
				};
				// Run the SignalR pipeline. We're not using MapSignalR
				// since this branch already runs under the "/signalr"
				// path.
				map.RunSignalR(hubConfiguration);
			});

			
			config.MessageHandlers.Add(new ThrottlingHandler()
			{
				Policy = new ThrottlePolicy()
				{
					IpThrottling = true,
					ClientThrottling = true,
					EndpointThrottling = true,
					EndpointRules = new Dictionary<string, RateLimits>
					{
						{ "api/DepartmentRegistration/CheckIfEmailInUse", new RateLimits { PerMinute = 4 } },
						{ "api/DepartmentRegistration/CheckIfDepartmentNameUsed", new RateLimits { PerMinute = 4 } },
						{ "api/DepartmentRegistration/CheckIfUserNameUsed", new RateLimits { PerMinute = 4 } },
						{ "api/DepartmentRegistration/Register", new RateLimits { PerMinute = 1 } },
						{ "api/v3/health", new RateLimits { PerMinute = 1 } },
						{ "api/v3/geo/GetLocationForWhatThreeWords", new RateLimits { PerMinute = 15 } },
						{ "api/v3/geo/GetCoordinatesForAddress", new RateLimits { PerMinute = 15 } }
					}
				},
				Repository = new CacheRepository()
			});

			if (Resgrid.Config.PaymentProviderConfig.IsTestMode)
				StripeConfiguration.SetApiKey(Resgrid.Config.PaymentProviderConfig.TestKey);
			else
				StripeConfiguration.SetApiKey(Resgrid.Config.PaymentProviderConfig.ProductionKey);

			ConfigureAuth(app);
		}

		private static readonly Lazy<CorsOptions> SignalrCorsOptions = new Lazy<CorsOptions>(() =>
		{
			var policy = new CorsPolicy
			{
				AllowAnyHeader = true,
				AllowAnyMethod = true,
				AllowAnyOrigin = false,
				SupportsCredentials = true
			};

			foreach (var hostname in ApiConfig.CorsAllowedHostnames.Split(char.Parse(",")))
			{
				policy.Origins.Add(hostname.Trim());
			}

			return new CorsOptions
			{
				PolicyProvider = new CorsPolicyProvider
				{
					PolicyResolver = context =>
					{
						return Task.FromResult(policy);
					}
				}
			};
		});
	}

	public partial class TestStartup
	{
		//public void Configuration(IAppBuilder app, IDependencyResolver resolver = null)
		public void Configuration(IAppBuilder app)
		{
			WebBootstrapper.Initialize();

			app.MapSignalR();

			var dir = AppDomain.CurrentDomain.BaseDirectory;

			if (System.IO.File.Exists(AppDomain.CurrentDomain.BaseDirectory + "\\ResgridContext.sdf"))
				System.IO.File.Delete(AppDomain.CurrentDomain.BaseDirectory + "\\ResgridContext.sdf");

			//DbConfiguration.Loaded += (_, a) =>
			//{
			//	a.ReplaceService<DbProviderServices>((s, k) => new MyProviderServices(s));
			//	a.ReplaceService<IDbConnectionFactory>((s, k) => new MyConnectionFactory(s));
			//};

			//Database.DefaultConnectionFactory = new SqlCeConnectionFactory("System.Data.SqlServerCe.4.0");
			//Database.SetInitializer<DataContext>(new Resgrid.Repositories.DataRepository.Initialization.ResgridTestDbInitializer());

			//var migrator = new DbMigrator(new ResgridTestConfiguration());
			//migrator.Update();

			WebApiConfig.Register(GlobalConfiguration.Configuration);

			FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
			RouteConfig.RegisterRoutes(RouteTable.Routes);
			BundleConfig.RegisterBundles(BundleTable.Bundles);
			
			ConfigureAuth(app);
		}
	}
}
