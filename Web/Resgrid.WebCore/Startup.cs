using System;
using System.Configuration;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.Claims;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.Firebase;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Providers.Voip;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Services;
using Resgrid.Web.Options;
using StackExchange.Redis;
using Stripe;

namespace Resgrid.Web
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
			}

			this.Configuration = builder.Build();
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			bool configResult = ConfigProcessor.LoadAndProcessConfig(Configuration["AppOptions:ConfigPath"]);
			bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

			var settings = System.Configuration.ConfigurationManager.ConnectionStrings;
			var element = typeof(ConfigurationElement).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);
			var collection = typeof(ConfigurationElementCollection).GetField("_readOnly", BindingFlags.Instance | BindingFlags.NonPublic);

			element.SetValue(settings, false);
			collection.SetValue(settings, false);

			if (!configResult && !envConfigResult)
				settings.Add(new ConnectionStringSettings("ResgridContext", Configuration["ConnectionStrings:ResgridContext"]));
			else
				settings.Add(new ConnectionStringSettings("ResgridContext", DataConfig.ConnectionString));

			collection.SetValue(settings, true);
			element.SetValue(settings, true);

			Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForWebsite);

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

			services.AddAuthentication(sharedOptions =>
				{
					sharedOptions.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
					sharedOptions.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
					sharedOptions.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
				})
				.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
				{
					options.LoginPath = "/Account/LogIn";
					options.LogoutPath = "/Account/LogOff";
					options.LoginPath = new PathString("/Account/LogOn/");
					options.AccessDeniedPath = new PathString("/Public/Forbidden/");
					options.Cookie.SecurePolicy = CookieSecurePolicy.None;//.SameAsRequest;
					options.Cookie.SameSite = SameSiteMode.Lax;//.None;
					options.Cookie.Name = "RGSITEAUTHCOOKIE";
					options.ExpireTimeSpan = new TimeSpan(48, 0, 0);
				});

			services.ConfigureApplicationCookie(options =>
			{
				options.Events.OnSignedIn = (context) =>
				{
					context.HttpContext.User = context.Principal;
					return Task.CompletedTask;
				};
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
			});
			#endregion Auth Roles

			//var manager = new ApplicationPartManager();
			//manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));
			//services.AddSingleton(manager);

			var configOptions = Configuration.GetSection("AppOptions").Get<AppOptions>();
			services.Configure<AppOptions>(Configuration.GetSection("AppOptions"));

			services.AddHttpContextAccessor();
			services.AddRazorPages();

			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders =
					ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
				options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse($"::ffff:{WebConfig.IngressProxyNetwork}"), WebConfig.IngressProxyNetworkCidr));
			});

			//services.AddWebOptimizer(pipeline =>
			//{
			//	pipeline.MinifyJsFiles("/js/**/*.js");
			//	pipeline.MinifyCssFiles("/css/**/*.css");
			//	pipeline.AddCssBundle("/css/pub-bundle.css", "css/style.css", "css/animate.css", "css/pricing/pricing-tables.css", "lib/font-awesome/css/font-awesome.min.css");
			//});

			services.AddMvc().AddMvcOptions(options =>
			{
				options.EnableEndpointRouting = false;
			}).AddJsonOptions(jsonOptions =>
			{
				jsonOptions.JsonSerializerOptions.PropertyNamingPolicy = null;
			});//.AddRazorRuntimeCompilation();

#if (!DEBUG)
			var redis = ConnectionMultiplexer.Connect(CacheConfig.RedisConnectionString);
			services.AddDataProtection().SetApplicationName($"{Config.SystemBehaviorConfig.GetEnvPrefix()}resgrid-web").PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");

			services.AddStackExchangeRedisCache(option =>
			{
				option.Configuration = Environment
					.GetEnvironmentVariable("Redis-Session");
				option.InstanceName = "RedisInstance";
			});

			services.AddSession(options =>
			{
				options.IdleTimeout = TimeSpan.FromMinutes(30);
				options.Cookie.Name = "ResgridSessionCookie";
			});
#endif

			StripeConfiguration.ApiKey = Config.PaymentProviderConfig.IsTestMode ? PaymentProviderConfig.TestApiKey : PaymentProviderConfig.ProductionApiKey;

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
			builder.RegisterModule(new FirebaseProviderModule());
			builder.RegisterModule(new VoipProviderModule());

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

			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}
			else if (env.IsStaging())
			{
				app.UseDeveloperExceptionPage();
			}
			else
			{
				app.UseExceptionHandler("/Home/Error");

#if (DOCKER)
				app.Use(async (context, next) => {
					context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000");
					context.Request.Scheme = "https";
					await next.Invoke();
				});

				//app.Use((context, next) =>
				//{
				//	context.Request.Scheme = "https";
				//	return next();
				//});
#endif

				/* I'm disabling this for now. Ideally it would be configured, but HSTS won't validate
				 * self-signed or QA certs from ACME or cert-manager. This will cause issues with
				 * hybrid Kubernetes\Docker implementations that are using proper certificates on the
				 * ingress ssl terminator and non-ssl or self signed certs on internal cluster
				 * communication channels. -SJ
				 */
				//app.UseHsts();
				//app.UseHttpsRedirection();
			}

			this.AutofacContainer = app.ApplicationServices.GetAutofacRoot();
			var eventAggregator = this.AutofacContainer.Resolve<IEventAggregator>();
			var outbound = this.AutofacContainer.Resolve<IOutboundEventProvider>();
			var eventService = this.AutofacContainer.Resolve<ICoreEventService>();

			this.Locator = new AutofacServiceLocator(this.AutofacContainer);
			ServiceLocator.SetLocatorProvider(() => this.Locator);

			var cookiePolicyOptions = new CookiePolicyOptions
			{
				Secure = CookieSecurePolicy.None,//.SameAsRequest,
				MinimumSameSitePolicy = SameSiteMode.Strict//.None,

			};

			app.UseCookiePolicy(cookiePolicyOptions);

			//app.UseWebOptimizer();
			app.UseStaticFiles();
			app.UseRouting();

			app.UseAuthentication();
			app.UseAuthorization();

			app.UseMvc(routes =>
			{
				routes.MapRoute("User", "{area:exists}/{controller}/{action=Index}/{id?}");

				routes.MapRoute(
					name: "default",
					template: "{controller=Home}/{action=Index}/{id?}");
			});
		}
	}
}
