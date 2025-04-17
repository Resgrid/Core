using System;
using System.Collections.Generic;
using Autofac;
using Resgrid.Config;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Workers.Framework;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Autofac.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Resgrid.Console.Commands;
using Resgrid.Console.Models;
using Resgrid.Console.Services;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Bus.Rabbit;
using Resgrid.Providers.Cache;
using Resgrid.Providers.Claims;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.Firebase;
using Resgrid.Providers.GeoLocationProvider;
using Resgrid.Providers.Marketing;
using Resgrid.Providers.Messaging;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Providers.NumberProvider;
using Resgrid.Providers.PdfProvider;
using Resgrid.Providers.Voip;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Services;

namespace Resgrid.Console
{
	public class Program
	{
		public static IConfigurationRoot Configuration { get; private set; }

		static async Task Main(string[] args)
		{
			System.Console.WriteLine("Resgrid Console");
			System.Console.WriteLine("-----------------------------------------");

			var builder = new HostBuilder()
				.UseServiceProviderFactory(new AutofacServiceProviderFactory())
				.ConfigureAppConfiguration((hostingContext, config) =>
				{
					config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
					config.AddEnvironmentVariables();

					config.AddCommandLine(args, new Dictionary<string, string>
					{
						{ "--UserId", "UserId" },
						{ "--Password", "Password" },
						{ "--DepartmentId", "DepartmentId" }
					});

					Configuration = config.Build();

					string configPath = Configuration["AppOptions:ConfigPath"];

					if (string.IsNullOrWhiteSpace(configPath))
						configPath = "C:\\Resgrid\\Config\\ResgridConfig.json";

					bool configResult = ConfigProcessor.LoadAndProcessConfig(configPath);
					if (configResult)
					{
						System.Console.WriteLine($"Loaded Config: {configPath}");
					}

					ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

				})
				.ConfigureServices((hostContext, services) =>
				{
					services.AddOptions();
					services.AddScoped<IUserStore<Model.Identity.IdentityUser>, IdentityUserStore>();
					services.AddScoped<IRoleStore<Model.Identity.IdentityRole>, IdentityRoleStore>();
					services.AddScoped<IUserClaimsPrincipalFactory<Model.Identity.IdentityUser>, ClaimsPrincipalFactory<Model.Identity.IdentityUser, Model.Identity.IdentityRole>>();

					services.AddIdentity<Model.Identity.IdentityUser, Model.Identity.IdentityRole>(config =>
					{
						//config.SignIn.RequireConfirmedAccount = false;
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

					services.AddKeyedTransient<ICommandService, ResetPasswordCommand>("ResetPasswordCommand");
					services.AddKeyedTransient<ICommandService, AddHostsCommand>("AddHostsCommand");
					services.AddKeyedTransient<ICommandService, ClearCacheCommand>("ClearCacheCommand");
					services.AddKeyedTransient<ICommandService, DbUpdateCommand>("DbUpdateCommand");
					services.AddKeyedTransient<ICommandService, GenOidcCertsCommand>("GenOidcCertsCommand");
					services.AddKeyedTransient<ICommandService, MigrateDocsDbCommand>("MigrateDocsDbCommand");
					services.AddKeyedTransient<ICommandService, OidcUpdateCommand>("OidcUpdateCommand");
					services.AddKeyedTransient<ICommandService, SecurityRefreshCommand>("SecurityRefreshCommand");
					services.AddKeyedTransient<ICommandService, HelpCommand>("HelpCommand");

					services.AddHostedService<ApplicationHostedService>();

					if (Config.DataConfig.DatabaseType == Config.DatabaseTypes.Postgres)
					{
						services
							// Add common FluentMigrator services
							.AddFluentMigratorCore()
							.ConfigureRunner(rb => rb
								// Add SQL Server support to FluentMigrator
								.AddPostgres11_0()
								// Set the connection string
								.WithGlobalConnectionString(Config.DataConfig.CoreConnectionString)
								// Define the assembly containing the migrations
								.ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.Migrations().For.EmbeddedResources())
							// Enable logging to console in the FluentMigrator way
							.AddLogging(lb => lb.AddFluentMigratorConsole())
							// Build the service provider
							.BuildServiceProvider(false);
					}
					else
					{
						services
							// Add common FluentMigrator services
							.AddFluentMigratorCore()
							.ConfigureRunner(rb => rb
								// Add SQL Server support to FluentMigrator
								.AddSqlServer()
								// Set the connection string
								.WithGlobalConnectionString(Config.DataConfig.CoreConnectionString)
								// Define the assembly containing the migrations
								.ScanIn(typeof(M0001_InitialMigration).Assembly).For.Migrations().For.EmbeddedResources())
							// Enable logging to console in the FluentMigrator way
							.AddLogging(lb => lb.AddFluentMigratorConsole())
							// Build the service provider
							.BuildServiceProvider(false);
					}
				})
				.ConfigureContainer((ContainerBuilder builder) =>
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
					builder.RegisterType<UserManager<Model.Identity.IdentityUser>>().As<UserManager<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
					builder.RegisterType<SignInManager<Model.Identity.IdentityUser>>().As<SignInManager<Model.Identity.IdentityUser>>().InstancePerLifetimeScope();
				})
				.ConfigureLogging((hostingContext, logging) =>
				{
					logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
					logging.AddConsole();
				});

			Prime();

			await builder.RunConsoleAsync();
		}

		private static void Prime()
		{
			Bootstrapper.Initialize();

			var eventAggragator = Bootstrapper.GetKernel().Resolve<IEventAggregator>();
			var outbound = Bootstrapper.GetKernel().Resolve<IOutboundEventProvider>();
			var coreEventService = Bootstrapper.GetKernel().Resolve<ICoreEventService>();

			SerializerHelper.WarmUpProtobufSerializer();
		}
	}
}
