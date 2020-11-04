using System;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Providers.Claims;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Web.ServicesCore.Middleware;
using Resgrid.Web.ServicesCore.Options;
using Stripe;
using System.Configuration;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Audio;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.Firebase;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Repositories.DataRepository;
using Resgrid.Services;
using Resgrid.Web.Services.Hubs;
using Resgrid.Web.Services.Middleware;

namespace Resgrid.Web.ServicesCore
{
	public class Startup
	{
		public IConfigurationRoot Configuration { get; private set; }
		public ILifetimeScope AutofacContainer { get; private set; }
		public AutofacServiceLocator Locator { get; private set; }
		public IServiceCollection Services { get; private set; }

		public Startup(IHostingEnvironment env)
		{
			var builder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
					.SetBasePath(env.ContentRootPath)
					.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
					.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
					.AddEnvironmentVariables();

			if (env.IsDevelopment() || env.IsStaging())
			{
				// For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
				//builder.AddUserSecrets();

				// This will push telemetry data through Application Insights pipeline faster, allowing you to view results immediately.
				builder.AddApplicationInsightsSettings(developerMode: true);
			}

			this.Configuration = builder.Build();
			Configuration = builder.Build();
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			bool configResult = ConfigProcessor.LoadAndProcessConfig(Configuration["AppOptions:ConfigPath"]);
			bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

			Framework.Logging.Initialize(Configuration["AppOptions:SentryKey"]);

			//var manager = new ApplicationPartManager();
			//manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));

			// Add framework services.
			services.AddApplicationInsightsTelemetry(Configuration);

			var settings = System.Configuration.ConfigurationManager.ConnectionStrings;
			var element = typeof(ConfigurationElement).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
			var collection = typeof(ConfigurationElementCollection).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);

			element.SetValue(settings, false);
			collection.SetValue(settings, false);

			if (!configResult && !envConfigResult)
				settings.Add(new System.Configuration.ConnectionStringSettings("ResgridContext", Configuration["ConnectionStrings:ResgridContext"]));
			else
				settings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			// Repeat above line as necessary

			collection.SetValue(settings, true);
			element.SetValue(settings, true);

			//Database.SetInitializer(new MigrateDatabaseToLatestVersion<Repositories.DataRepository.Contexts.DataContext, Configuration>());
			//var migrator = new DbMigrator(new Repositories.DataRepository.Migrations.Configuration());
			//migrator.Update();
			
			services.AddScoped<IUserStore<Model.Identity.IdentityUser>, IdentityUserStore>();
			services.AddScoped<IRoleStore<Model.Identity.IdentityRole>, IdentityRoleStore>();
			services.AddScoped<IUserClaimsPrincipalFactory<Model.Identity.IdentityUser>, ClaimsPrincipalFactory<Model.Identity.IdentityUser, Model.Identity.IdentityRole>>();

			services.AddIdentity<Model.Identity.IdentityUser, Model.Identity.IdentityRole>(config =>
			{
				config.SignIn.RequireConfirmedAccount = false;
				config.SignIn.RequireConfirmedEmail = false;
				config.SignIn.RequireConfirmedPhoneNumber = false;
				config.User.RequireUniqueEmail = true;
				config.User.AllowedUserNameCharacters = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@";
				config.Password.RequireDigit = false;
				config.Password.RequireLowercase = false;
				config.Password.RequireUppercase = false;
				config.Password.RequireNonAlphanumeric = false;
				config.Password.RequiredLength = 6;
			}).AddDefaultTokenProviders().AddClaimsPrincipalFactory<ClaimsPrincipalFactory<Model.Identity.IdentityUser, Model.Identity.IdentityRole>>();

			//services.AddCors(options =>
			//{
			//	options.AddPolicy("_resgridWebsiteAllowSpecificOrigins",
			//	builder =>
			//	{
			//		builder.WithOrigins("http://resgrid.com",
			//				"http://www.resgrid.com",
			//				"https://resgrid.com",
			//				"https://www.resgrid.com",
			//				"https://qaweb.resgrid.com",
			//				"https://qaapi.resgrid.com",
			//				"http://qaweb.resgrid.com",
			//				"http://qaapi.resgrid.com",
			//				"https://s3.amazonaws.com",
			//				"https://resgrid.freshdesk.com",
			//				"https://www.google.com",
			//				"https://js.stripe.com",
			//				"https://wchat.freshchat.com",
			//				"https://q.stripe.com",
			//				"https://cdn.plyr.io",
			//				"https://unit.resgrid.com",
			//				"https://responder.resgrid.com",
			//				"https://dispatch.resgrid.com",
			//				"https://command.resgrid.com",
			//				"https://eventing.resgrid.com",
			//				"https://qaeventing.resgrid.com",
			//				"http://eventing.resgrid.com",
			//				"http://qaeventing.resgrid.com",
			//				"https://cdn.jsdelivr.net",
			//				"https://ajax.googleapis.com",
			//				"https://maps.googleapis.com",
			//				"https://assetscdn-wchat.freshchat.com",
			//				"https://assets.freshdesk.com",
			//				"https://assets1.freshdesk.com",
			//				"https://assets2.freshdesk.com",
			//				"https://assets3.freshdesk.com",
			//				"https://assets4.freshdesk.com",
			//				"https://assets5.freshdesk.com",
			//				"https://assets6.freshdesk.com",
			//				"https://az416426.vo.msecnd.net",
			//				"https://localhost",
			//				"http://localhost",
			//				"http://localhost:8100",
			//				"https://localhost:8100",
			//				"http://localhost:44319",
			//				"https://localhost:44319")
			//			.AllowAnyMethod()
			//			.AllowAnyHeader()
			//			.AllowCredentials();
			//	});
			//});

			services.AddCors();

			services.AddControllers().AddNewtonsoftJson(options =>
			{
				options.SerializerSettings.ContractResolver = new DefaultContractResolver();
			});

			services.AddApiVersioning(x =>  
			{  
				x.DefaultApiVersion = new ApiVersion(3, 0);  
				x.AssumeDefaultVersionWhenUnspecified = true;  
				x.ReportApiVersions = true;  
			});  

			services.AddSwaggerGen();
			services.AddSwaggerGenNewtonsoftSupport();
			services.ConfigureSwaggerGen(options =>
			{
				options.SwaggerDoc("v3",
					new OpenApiInfo
					{
						Title = "Resgrid API",
						Version = "v3",
						Description = "The Resgrid Computer Aided Dispatch (CAD) API reference",
						Contact = new OpenApiContact() {Email = "team@resgrid.com", Name = "Resgrid Team", Url = new Uri("https://resgrid.com")},
						TermsOfService = new Uri("https://resgrid.com/Public/Terms")
					}
				);
 
				var filePath = Path.Combine(PlatformServices.Default.Application.ApplicationBasePath, "Resgrid.Web.Services.xml");
				options.IncludeXmlComments(filePath);
				options.DescribeAllEnumsAsStrings();
			});

			services.AddAuthentication("BasicAuthentication")
				.AddScheme<ResgridAuthenticationOptions, ResgridTokenAuthHandler>("BasicAuthentication", null);

			services.AddSignalR(hubOptions =>
			{
				hubOptions.EnableDetailedErrors = true;
			}).AddStackExchangeRedis(CacheConfig.RedisConnectionString, options => {
				options.Configuration.ChannelPrefix = $"{Config.SystemBehaviorConfig.GetEnvPrefix()}resgrid-api-sr";
			});

			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders =
					ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
				options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse($"::ffff:{WebConfig.IngressProxyNetwork}"), WebConfig.IngressProxyNetworkCidr));
			});

			#region Auth Roles
			services.AddAuthorization(options =>
			{
				options.AddPolicy(ResgridResources.SystemAdmin, policy => policy.RequireClaim(System.Security.Claims.ClaimTypes.Role, "Admins"));
				options.AddPolicy(ResgridResources.Department_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Department_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Department_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Department_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Group_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Group_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Group_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Group_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Group, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Call_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Call_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Call_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Call_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Call, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Action_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Action_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Action_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Action_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Action, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Log_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Log_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Log_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Log_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Shift_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Shift_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Shift_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Shift_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Shift, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Personnel_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Personnel_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Personnel_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Personnel_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Personnel, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Role_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Role_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Role_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Role_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Role, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Unit_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Unit_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Unit_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Unit_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Unit, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.UnitLog_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.UnitLog_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.UnitLog_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.UnitLog_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.UnitLog, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Messages_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Messages_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Messages_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Messages_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Messages, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Profile_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Profile_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Profile, ResgridClaimTypes.Actions.Update));

				options.AddPolicy(ResgridResources.Reports_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Reports, ResgridClaimTypes.Actions.View));

				options.AddPolicy(ResgridResources.GenericGroup_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.GenericGroup_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.GenericGroup_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.GenericGroup_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.GenericGroup, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Documents_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Documents_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Documents_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Documents_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Notes_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Notes_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Notes_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Notes_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Notes, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Schedule_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Schedule_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Schedule_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Schedule_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Schedule, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Training_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Training_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Training_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Training_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.PersonalInfo_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.PersonalInfo_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.PersonalInfo_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.PersonalInfo_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.PersonalInfo, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Inventory_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Inventory_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Inventory_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Inventory_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Inventory, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Command_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Command_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Command_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Command_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Command, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Connect_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Connect_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Connect_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Connect_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Connect, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Protocol_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Protocol_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Protocol_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Protocol_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.Delete));
			});
			#endregion Auth Roles

			var configOptions = Configuration.GetSection("AppOptions").Get<AppOptions>();
			services.Configure<AppOptions>(Configuration.GetSection("AppOptions"));

			if (Config.PaymentProviderConfig.IsTestMode)
				StripeConfiguration.SetApiKey(PaymentProviderConfig.TestApiKey);
			else
				StripeConfiguration.SetApiKey(PaymentProviderConfig.ProductionApiKey);

			this.Services = services;
		}

		public void ConfigureContainer(ContainerBuilder builder)
		{
			builder.RegisterType<HttpContextAccessor>().As<IHttpContextAccessor>().SingleInstance();

			builder.RegisterModule(new DataModule());
			builder.RegisterModule(new ServicesModule());
			builder.RegisterModule(new ProviderModule());
			builder.RegisterModule(new EmailProviderModule());
			builder.RegisterModule(new BusModule());
			builder.RegisterModule(new RabbitBusModule());
			builder.RegisterModule(new AddressVerificationModule());
			builder.RegisterModule(new NumbersProviderModule());
			builder.RegisterModule(new CacheProviderModule());
			builder.RegisterModule(new MarketingModule());
			builder.RegisterModule(new PdfProviderModule());
			builder.RegisterModule(new AudioProviderModule());
			builder.RegisterModule(new FirebaseProviderModule());

			builder.RegisterType<IdentityUserStore>().As<IUserStore<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<IdentityRoleStore>().As<IRoleStore<Model.Identity.IdentityRole>>().InstancePerLifetimeScope();
			builder.RegisterType<ClaimsPrincipalFactory<Model.Identity.IdentityUser, Model.Identity.IdentityRole>>().As<IUserClaimsPrincipalFactory<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();

			builder.RegisterType<UserValidator<Model.Identity.IdentityUser>>().As<IUserValidator<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordValidator<Model.Identity.IdentityUser>>().As<IPasswordValidator<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordHasher<Model.Identity.IdentityUser>>().As<IPasswordHasher<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<UpperInvariantLookupNormalizer>().As<ILookupNormalizer>().InstancePerLifetimeScope();
			builder.RegisterType<DefaultUserConfirmation<Model.Identity.IdentityUser>>().As<IUserConfirmation<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<UserManager<Model.Identity.IdentityUser>>().As<UserManager<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<SignInManager<Model.Identity.IdentityUser>>().As<SignInManager<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
		{
			app.UseForwardedHeaders();

			//loggerFactory.AddConsole(Configuration.GetSection("Logging"));
			//loggerFactory.AddDebug();

			//app.UseApplicationInsightsRequestTelemetry();

			this.AutofacContainer = app.ApplicationServices.GetAutofacRoot();
			var eventAggregator = this.AutofacContainer.Resolve<IEventAggregator>();
			var outbound = this.AutofacContainer.Resolve<IOutboundEventProvider>();
			var eventService = this.AutofacContainer.Resolve<ICoreEventService>();

			this.Locator = new AutofacServiceLocator(this.AutofacContainer);
			ServiceLocator.SetLocatorProvider(() => this.Locator);

			app.Use(async (context, next) => {
				context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000");
				context.Request.Scheme = "https";
				await next.Invoke();
			});

			//app.UseApplicationInsightsExceptionTelemetry();

			//app.UseCors("_resgridWebsiteAllowSpecificOrigins");
			// global cors policy
			app.UseCors(x => x
				.AllowAnyMethod()
				.AllowAnyHeader()
				.SetIsOriginAllowed(origin => true) // allow any origin
				.AllowCredentials()); // allow credentials

			app.UseRouting();
			app.UseStaticFiles();

			app.UseAuthentication();
			app.UseAuthorization(); 

			app.UseSwagger();
			app.UseSwaggerUI(c =>
			{
				c.SwaggerEndpoint("/swagger/v3/swagger.json", "Resgrid API V3");
				c.RoutePrefix = string.Empty;
			});

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();

				endpoints.MapHub<EventingHub>("/eventingHub");
			});
		}
	}
}
