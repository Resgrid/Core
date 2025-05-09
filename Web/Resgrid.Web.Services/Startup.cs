using System;
using System.Collections.Generic;
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
using Resgrid.Web.ServicesCore.Options;
using Stripe;
using System.Configuration;
using System.IO;
using System.Net;
using System.Reflection;
using AspNetCoreRateLimit;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Serialization;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.AddressVerification;
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
using Resgrid.Providers.Voip;
using Resgrid.Web.Services.Models;
using Microsoft.EntityFrameworkCore;
using static OpenIddict.Abstractions.OpenIddictConstants;
//using OpenTelemetry.Metrics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Cryptography.X509Certificates;
using Sentry.Extensibility;
using Resgrid.Web.ServicesCore.Middleware;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;
using System.Net.Http;
using Resgrid.Providers.Messaging;

namespace Resgrid.Web.ServicesCore
{
	public class Startup
	{
		//public IConfiguration Configuration { get; }
		public IConfigurationRoot Configuration { get; private set; }
		public ILifetimeScope AutofacContainer { get; private set; }
		public AutofacServiceLocator Locator { get; private set; }
		public IServiceCollection Services { get; private set; }
		//private MeterProvider meterProvider;

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
			}

			this.Configuration = builder.Build();
			Configuration = builder.Build();
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			bool configResult = ConfigProcessor.LoadAndProcessConfig(Configuration["AppOptions:ConfigPath"]);
			bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

			Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForApi);

			//var manager = new ApplicationPartManager();
			//manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));

			// Add framework services.
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

			if (Config.ApiConfig.BypassSslChecks)
			{
				services.AddHttpClient("ByPassSSLHttpClient")
					 .ConfigurePrimaryHttpMessageHandler(() =>
					 {
						 var handler = new HttpClientHandler
						 {
							 AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
							 ServerCertificateCustomValidationCallback = (sender, certificate, chain, errors) =>
							 {
								 return true;
							 }
						 };
						 return handler;
					 });
			}
			else
			{
				services.AddHttpClient("ByPassSSLHttpClient");
			}

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
				config.Password.RequireDigit = true;
				config.Password.RequireLowercase = true;
				config.Password.RequireUppercase = true;
				config.Password.RequireNonAlphanumeric = false;
				config.Password.RequiredLength = 8;
				config.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
				config.Lockout.MaxFailedAccessAttempts = 5;
				config.Lockout.AllowedForNewUsers = true;
			}).AddDefaultTokenProviders().AddClaimsPrincipalFactory<ClaimsPrincipalFactory<Model.Identity.IdentityUser, Model.Identity.IdentityRole>>();

			services.AddCors();

			services.AddControllers().AddNewtonsoftJson(options =>
			{
				options.SerializerSettings.ContractResolver = new DefaultContractResolver();
			});

			services.AddApiVersioning(x =>
			{
				x.DefaultApiVersion = new ApiVersion(4, 0);
				x.AssumeDefaultVersionWhenUnspecified = true;
				x.ReportApiVersions = true;
			});

			services.AddMemoryCache();
			services.Configure<IpRateLimitOptions>(Configuration.GetSection("IpRateLimiting"));
			services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
			services.AddInMemoryRateLimiting();
			services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
			//services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

			services.AddSwaggerGen();
			services.AddSwaggerGenNewtonsoftSupport();
			services.ConfigureSwaggerGen(options =>
			{
				options.CustomSchemaIds(type => type.ToString());

				// add JWT Authentication
				var securityScheme = new OpenApiSecurityScheme
				{
					Name = "JWT Authentication",
					Description = "Enter JWT Bearer token **_only_**",
					In = ParameterLocation.Header,
					Type = SecuritySchemeType.Http,
					Scheme = "bearer", // must be lower case
					BearerFormat = "JWT",
					Reference = new OpenApiReference
					{
						Id = JwtBearerDefaults.AuthenticationScheme,
						Type = ReferenceType.SecurityScheme
					}
				};

				options.AddSecurityDefinition(securityScheme.Reference.Id, securityScheme);
				options.AddSecurityRequirement(new OpenApiSecurityRequirement
				{
					{securityScheme, new string[] { }}
				});

				//options.SwaggerDoc("v3",

				//	new OpenApiInfo
				//	{
				//		Title = "Resgrid API",
				//		Version = "v3",
				//		Description = "The Resgrid Computer Aided Dispatch (CAD) API reference. Documentation: https://resgrid-core.readthedocs.io/en/latest/api/index.html",
				//		Contact = new OpenApiContact() { Email = "team@resgrid.com", Name = "Resgrid Team", Url = new Uri("https://resgrid.com") },
				//		TermsOfService = new Uri("https://resgrid.com/Public/Terms")
				//	}
				//);

				options.SwaggerDoc("v4",

					new OpenApiInfo
					{
						Title = "Resgrid API",
						Version = "v4",
						Description = "The Resgrid Computer Aided Dispatch (CAD) API reference. Documentation: https://resgrid-core.readthedocs.io/en/latest/api/index.html",
						Contact = new OpenApiContact() { Email = "team@resgrid.com", Name = "Resgrid Team", Url = new Uri("https://resgrid.com") },
						TermsOfService = new Uri("https://resgrid.com/Public/Terms")
					}
				);

				var filePath = Path.Combine(AppContext.BaseDirectory, "Resgrid.Web.Services.xml");
				options.IncludeXmlComments(filePath);
				//options.DescribeAllEnumsAsStrings();
			});

			services.AddSignalR(hubOptions =>
			{
				hubOptions.EnableDetailedErrors = true;
			}).AddStackExchangeRedis(CacheConfig.RedisConnectionString, options =>
			{
				options.Configuration.ChannelPrefix = $"{Config.SystemBehaviorConfig.GetEnvPrefix()}resgrid-evt-sr";
			});

			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
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

				options.AddPolicy(ResgridResources.Forms_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Forms_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Forms_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Forms_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Forms, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Voice_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Voice_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Voice_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Voice_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Voice, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.CustomStates_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.CustomStates_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.CustomStates_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.CustomStates_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.CustomStates, ResgridClaimTypes.Actions.Delete));

				options.AddPolicy(ResgridResources.Contacts_View, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.View));
				options.AddPolicy(ResgridResources.Contacts_Update, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Update));
				options.AddPolicy(ResgridResources.Contacts_Create, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Create));
				options.AddPolicy(ResgridResources.Contacts_Delete, policy => policy.RequireClaim(ResgridClaimTypes.Resources.Contacts, ResgridClaimTypes.Actions.Delete));
			});
			#endregion Auth Roles

			var configOptions = Configuration.GetSection("AppOptions").Get<AppOptions>();
			services.Configure<AppOptions>(Configuration.GetSection("AppOptions"));

			StripeConfiguration.ApiKey = Config.PaymentProviderConfig.IsTestMode ? PaymentProviderConfig.TestApiKey : PaymentProviderConfig.ProductionApiKey;

			// Register the Identity services.
			//services.AddIdentity<ApplicationUser, IdentityRole>()
			//	.AddEntityFrameworkStores<ApplicationDbContext>()
			//	.AddDefaultTokenProviders();

			// Configure Identity to use the same JWT claims as OpenIddict instead
			// of the legacy WS-Federation claims it uses by default (ClaimTypes),
			// which saves you from doing the mapping in your authorization controller.
			services.Configure<IdentityOptions>(options =>
			{
				options.ClaimsIdentity.UserNameClaimType = Claims.Name;
				options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
				options.ClaimsIdentity.RoleClaimType = Claims.Role;
			});

			//// OpenIddict offers native integration with Quartz.NET to perform scheduled tasks
			//// (like pruning orphaned authorizations/tokens from the database) at regular intervals.
			//services.AddQuartz(options =>
			//{
			//	options.UseMicrosoftDependencyInjectionJobFactory();
			//	options.UseSimpleTypeLoader();
			//	options.UseInMemoryStore();
			//});

			//// Register the Quartz.NET service and configure it to block shutdown until jobs are complete.
			//services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

			if (OidcConfig.DatabaseType == DatabaseTypes.Postgres)
			{
				services.AddDbContext<AuthorizationDbContext>(options =>
				{
					// Configure the context to use Microsoft SQL Server.
					options.UseNpgsql(
						Config.OidcConfig.ConnectionString,
						opt =>
						{
							opt.EnableRetryOnFailure(
								maxRetryCount: 10,
								maxRetryDelay: TimeSpan.FromSeconds(30),
								errorCodesToAdd: new List<string>() { });

							opt.UseAdminDatabase("postgres");
						});//.UseLowerCaseNamingConvention();

					options.UseOpenIddict<Guid>();
				});

				AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
			}
			else
			{
				services.AddDbContext<AuthorizationDbContext>(options =>
				{
					// Configure the context to use Microsoft SQL Server.
					options.UseSqlServer(OidcConfig.ConnectionString);

					// Register the entity sets needed by OpenIddict.
					// Note: use the generic overload if you need
					// to replace the default OpenIddict entities.
					options.UseOpenIddict<Guid>();
				});
			}

			services.AddOpenIddict()
				// Register the OpenIddict core components.
				.AddCore(options =>
				{
					// Configure OpenIddict to use the Entity Framework Core stores and models.
					// Note: call ReplaceDefaultEntities() to replace the default OpenIddict entities.
					options.UseEntityFrameworkCore()
						.UseDbContext<AuthorizationDbContext>()
						.ReplaceDefaultEntities<Guid>();

					// Enable Quartz.NET integration.
					//options.UseQuartz();
				})
				// Register the OpenIddict server components.
				.AddServer(options =>
				{
					options.RegisterScopes(
						Scopes.Profile,
						Scopes.Email,
						Scopes.OfflineAccess,
						"mobile",
						"web");

					// Enable the token endpoint.
					options.SetTokenEndpointUris("/api/v4/connect/token");
					options.SetIntrospectionEndpointUris("/api/v4/connect/introspect");

					options.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
					options.SetRefreshTokenLifetime(TimeSpan.FromDays(OidcConfig.RefreshTokenExpiryDays));

					// Enable the password and the refresh token flows.
					//options.AllowPasswordFlow()
					//	   .AllowRefreshTokenFlow();
					options//.AllowAuthorizationCodeFlow()
						   //.AllowHybridFlow()
					   .AllowClientCredentialsFlow()
					   .AllowPasswordFlow()
					   .AllowRefreshTokenFlow();

					// Accept anonymous clients (i.e clients that don't send a client_id).
					options.AcceptAnonymousClients();

					options.AddEncryptionCertificate(new X509Certificate2(Convert.FromBase64String(OidcConfig.EncryptionCert)));
					options.AddSigningCertificate(new X509Certificate2(Convert.FromBase64String(OidcConfig.SigningCert)));

					//options.AddEncryptionKey(new SymmetricSecurityKey(
					//	Convert.FromBase64String(OidcConfig.Key)));

					// Register the signing and encryption credentials.
					//options.AddDevelopmentEncryptionCertificate()
					//	   .AddDevelopmentSigningCertificate();

					// Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
					options.UseAspNetCore()
						   .EnableTokenEndpointPassthrough();
				})
				// Register the OpenIddict validation components.
				.AddValidation(options =>
				{
					// Import the configuration from the local OpenIddict server instance.
					options.UseLocalServer();

					// Register the ASP.NET Core host.
					options.UseAspNetCore();
				});


			//builder.Logging.AddOpenTelemetry(options =>
			//{
			//	options
			//		.SetResourceBuilder(
			//			ResourceBuilder.CreateDefault()
			//				.AddService("ResgridApi"))
			//		.AddConsoleExporter();
			//});
			//services.AddOpenTelemetry()
			//	  .ConfigureResource(resource => resource.AddService("ResgridApi"))
			//	  .WithTracing(tracing => tracing
			//		  .AddAspNetCoreInstrumentation()
			//		  .AddConsoleExporter())
			//	  .WithMetrics(metrics => metrics
			//		  .AddAspNetCoreInstrumentation()
			//		  .AddConsoleExporter());

			//// For options which can be bound from IConfiguration.
			//services.Configure<AspNetCoreTraceInstrumentationOptions>(this.Configuration.GetSection("AspNetCoreInstrumentation"));

			//// For options which can be configured from code only.
			//services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
			//{
			//	options.Filter = (req) =>
			//	{
			//		return req.Request.Host != null;
			//	};
			//});


			services.AddAuthentication("BasicAuthentication")
				.AddScheme<ResgridAuthenticationOptions, ResgridTokenAuthHandler>("BasicAuthentication", null);

			//// TODO: Add IServiceCollection.AddOpenTelemetryMetrics extension method
			//var providerBuilder = Sdk.CreateMeterProviderBuilder()
			//	.AddAspNetCoreInstrumentation();

			//// TODO: Add configuration switch for Prometheus and OTLP export
			//providerBuilder
			//	.AddConsoleExporter();

			//this.meterProvider = providerBuilder.Build();

			services.AddTransient<ISentryEventProcessor, SentryEventProcessor>();

			//services.AddHostedService<Worker>();
			this.Services = services;

			//if (Config.ExternalErrorConfig.ApplicationInsightsEnabled)
			//{
			//	services.AddSingleton<ITelemetryInitializer, ApiTelemetryInitializer>();

			//	var aiOptions = new ApplicationInsightsServiceOptions();
			//	aiOptions.InstrumentationKey = ExternalErrorConfig.ApplicationInsightsInstrumentationKey;
			//	aiOptions.ConnectionString = ExternalErrorConfig.ApplicationInsightsConnectionString;

			//	services.AddApplicationInsightsTelemetry(aiOptions);
			//}
		}

		public void ConfigureContainer(ContainerBuilder builder)
		{
			builder.RegisterType<HttpContextAccessor>().As<IHttpContextAccessor>().SingleInstance();

			builder.RegisterModule(new DataModule());
			builder.RegisterModule(new NoSqlDataModule());
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
			builder.RegisterModule(new FirebaseProviderModule());
			builder.RegisterModule(new VoipProviderModule());
			builder.RegisterModule(new MessagingProviderModule());

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
			var forwardOpts = new ForwardedHeadersOptions
			{
				ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
			};
			app.UseForwardedHeaders(forwardOpts);

			//loggerFactory.AddConsole(Configuration.GetSection("Logging"));
			//loggerFactory.AddDebug();

			this.AutofacContainer = app.ApplicationServices.GetAutofacRoot();
			var eventAggregator = this.AutofacContainer.Resolve<IEventAggregator>();
			var outbound = this.AutofacContainer.Resolve<IOutboundEventProvider>();
			var eventService = this.AutofacContainer.Resolve<ICoreEventService>();

			this.Locator = new AutofacServiceLocator(this.AutofacContainer);
			ServiceLocator.SetLocatorProvider(() => this.Locator);

			app.Use(async (context, next) =>
			{
				context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000");
				context.Request.Scheme = "https";
				await next.Invoke();
			});

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
				//c.SwaggerEndpoint($"/swagger/v3/swagger.json", "Resgrid API V3");
				c.SwaggerEndpoint($"/swagger/v4/swagger.json", "Resgrid API V4");

				c.RoutePrefix = string.Empty;
			});

			app.UseIpRateLimiting();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();

				endpoints.MapHub<EventingHub>("/eventingHub");
			});
		}
	}
}
