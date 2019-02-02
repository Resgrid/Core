using System;
using System.Configuration;
using System.Data.Entity;
using System.Data.Entity.Migrations;
using System.Reflection;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNet.Identity.EntityFramework6;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Resgrid.Model;
using Newtonsoft.Json.Serialization;
using Resgrid.Providers.Claims.Core;
using Resgrid.Web.Data;
using Resgrid.Web.Helpers;
using Resgrid.Web.Options;
using Resgrid.Providers.Claims;
using Configuration = Resgrid.Repositories.DataRepository.Migrations.Configuration;
using Stripe;
using Microsoft.Practices.ServiceLocation;

namespace Resgrid.Web
{
	public class Startup
	{
		public IContainer ApplicationContainer { get; private set; }
		public IConfigurationRoot Configuration { get; private set; }

		public Startup(IHostingEnvironment env)
		{
			var builder = new ConfigurationBuilder()
					.SetBasePath(env.ContentRootPath)
					.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
					.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
					.AddEnvironmentVariables();

			if (env.IsDevelopment() || env.IsStaging())
			{
				// For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
				builder.AddUserSecrets();

				// This will push telemetry data through Application Insights pipeline faster, allowing you to view results immediately.
				builder.AddApplicationInsightsSettings(developerMode: true);
			}

			this.Configuration = builder.Build();
			Configuration = builder.Build();
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public IServiceProvider ConfigureServices(IServiceCollection services)
		{
			Framework.Logging.Initialize(Configuration["AppOptions:SentryKey"]);

			var manager = new ApplicationPartManager();
			manager.ApplicationParts.Add(new AssemblyPart(typeof(Startup).Assembly));

			// Add framework services.
			services.AddApplicationInsightsTelemetry(Configuration);

			var settings = ConfigurationManager.ConnectionStrings;
			var element = typeof(ConfigurationElement).GetField("_bReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
			var collection = typeof(ConfigurationElementCollection).GetField("bReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);

			element.SetValue(settings, false);
			collection.SetValue(settings, false);

			settings.Add(new ConnectionStringSettings("ResgridContext", Configuration["ConnectionStrings:ResgridContext"]));

			// Repeat above line as necessary

			collection.SetValue(settings, true);
			element.SetValue(settings, true);

			Database.SetInitializer(new MigrateDatabaseToLatestVersion<Repositories.DataRepository.Contexts.DataContext, Configuration>()); 
			var migrator = new DbMigrator(new Repositories.DataRepository.Migrations.Configuration());
			migrator.Update();

			//services.AddDbContext<DataContext>(options =>
			//		options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

			//services.AddIdentity<ApplicationUser, IdentityRole>()
			//		.AddEntityFrameworkStores<DataContext>()
			//		.AddDefaultTokenProviders();

			services.AddScoped<IPasswordHasher<IdentityUser>, SqlPasswordHasher>();

			//Inject ApplicationDbContext in place of IdentityDbContext and use connection string
			services.AddScoped<IdentityDbContext<IdentityUser>>(context =>
					new ApplicationDbContext(Configuration["ConnectionStrings:ResgridContext"]));


			//Configure Identity middleware with ApplicationUser and the EF6 IdentityDbContext
			services.AddIdentity<IdentityUser, IdentityRole>(config =>
			{
				config.User.RequireUniqueEmail = true;
				config.User.AllowedUserNameCharacters = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@";
				config.Password.RequireDigit = false;
				config.Password.RequireLowercase = false;
				config.Password.RequireUppercase = false;
				config.Password.RequireNonAlphanumeric = false;
				config.Password.RequiredLength = 6;
			})
			.AddEntityFrameworkStores<IdentityDbContext<IdentityUser>>()
			.AddDefaultTokenProviders();

			services.AddScoped<IUserClaimsPrincipalFactory<IdentityUser>, ClaimsPrincipalFactory<IdentityUser, IdentityRole>>();
			services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

			services.AddSingleton(manager);
			services.AddMvc().AddJsonOptions(opt =>
			{
				var resolver = opt.SerializerSettings.ContractResolver;
				if (resolver != null)
				{
					var res = resolver as DefaultContractResolver;
					res.NamingStrategy = null;  // <<!-- this removes the camelcasing
				}
			});

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
			});


			var configOptions = Configuration.GetSection("AppOptions").Get<AppOptions>();
			services.Configure<AppOptions>(Configuration.GetSection("AppOptions"));

			WebBootstrapper.Initialize(services);
			this.ApplicationContainer = WebBootstrapper.GetKernel();

			StripeConfiguration.SetApiKey(configOptions.StripeApiKey);

			// Create and return the service provider.
			return new AutofacServiceProvider(this.ApplicationContainer);
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
		{
			loggerFactory.AddConsole(Configuration.GetSection("Logging"));
			loggerFactory.AddDebug();

			//Azure Support's note: need to invoke the middleware's method here to test whether it logs telemetry (the myUrl / request_Url from MyQueue) to App Insights when the 502s occur. If it does log telemetry when the 502s occur, this indicates that .NET Core framework is reached and that the issue is in the app.
			//If it doesn't log telemtry when the 502s occur, it indicates an issue at the .NET Core platform level.
			//app.UseMiddleware<AzureMiddleware>();
			app.UseApplicationInsightsRequestTelemetry();

			var sslPort = 0;
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
				//app.UseDatabaseErrorPage();
				app.UseBrowserLink();

				var builder = new ConfigurationBuilder()
					.SetBasePath(env.ContentRootPath)
					.AddJsonFile(@"Properties/launchSettings.json", optional: false, reloadOnChange: true);
				var launchConfig = builder.Build();
				sslPort = launchConfig.GetValue<int>("iisSettings:iisExpress:sslPort");
			}
			else if (env.IsStaging())
			{
				app.UseDeveloperExceptionPage();
				//app.UseDatabaseErrorPage();
			}
			else
			{
				app.UseExceptionHandler("/Home/Error");
			}

			app.UseApplicationInsightsExceptionTelemetry();

			app.UseStaticFiles();

			app.UseIdentity();

			// Add external authentication middleware below. To configure them please see http://go.microsoft.com/fwlink/?LinkID=532715

			//app.UseClaimsTransformation(new ClaimsTransformationOptions
			//{
			//	Transformer = new CoreClaimsTransformer()
			//});

			app.UseCookieAuthentication(new CookieAuthenticationOptions()
			{
				AuthenticationScheme = "ResgridCookieMiddlewareInstance",
				LoginPath = new PathString("/Account/LogOn/"),
				AccessDeniedPath = new PathString("/Public/Forbidden/"),
				AutomaticAuthenticate = false,
				AutomaticChallenge = false
			});

			app.UseMvc(routes =>
			{
				routes.MapRoute("Admin", "{area:exists}/{controller}/{action=Index}/{id?}");
				routes.MapRoute("User", "{area:exists}/{controller}/{action=Index}/{id?}");

				routes.MapRoute(
									name: "default",
									template: "{controller=Home}/{action=Index}/{id?}");
			});

			//app.Use(async (context, next) =>
			//{
			//	if (context.Request.IsHttps)
			//	{
			//		await next();
			//	}
			//	else
			//	{
			//		var sslPortStr = sslPort == 0 || sslPort == 443 ? string.Empty : $":{sslPort}";
			//		var httpsUrl = $"https://{context.Request.Host.Host}{sslPortStr}{context.Request.Path}";
			//		context.Response.Redirect(httpsUrl);
			//	}
			//});
		}
	}
}
