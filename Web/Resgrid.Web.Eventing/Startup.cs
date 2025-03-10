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
using Stripe;
using System.Configuration;
using System.IO;
using System.Net;
using System.Reflection;
using Autofac.Extensions.DependencyInjection;
using Autofac.Extras.CommonServiceLocator;
using CommonServiceLocator;
using Microsoft.AspNetCore.HttpOverrides;
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
using Resgrid.Web.Eventing.Hubs;
using System.Security.Claims;
using OpenIddict.Validation;
using static OpenIddict.Abstractions.OpenIddictConstants;
using System.Security.Cryptography.X509Certificates;
using Resgrid.Web.Services.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Threading.Tasks;
using OpenIddict.Server.AspNetCore;
using Microsoft.IdentityModel.Logging;
using OpenIddict.Validation;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Asn1.Ess;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;
using System.Net.Http;
using Resgrid.Providers.Messaging;

namespace Resgrid.Web.Eventing
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

			this.Configuration = builder.Build();
			Configuration = builder.Build();
		}

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			IdentityModelEventSource.ShowPII = true;

			bool configResult = ConfigProcessor.LoadAndProcessConfig(Configuration["AppOptions:ConfigPath"]);
			bool envConfigResult = ConfigProcessor.LoadAndProcessEnvVariables(Configuration.AsEnumerable());

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

			Framework.Logging.Initialize(ExternalErrorConfig.ExternalErrorServiceUrlForEventing);

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

			services.AddCors();

			services.AddControllers().AddNewtonsoftJson(options =>
			{
				options.SerializerSettings.ContractResolver = new DefaultContractResolver();
			});

			services.AddSignalR(hubOptions =>
			{
				hubOptions.EnableDetailedErrors = true;
				hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(10);
				hubOptions.HandshakeTimeout = TimeSpan.FromSeconds(5);

			}).AddStackExchangeRedis(CacheConfig.RedisConnectionString, options =>
			{
				options.Configuration.ChannelPrefix = $"{Config.SystemBehaviorConfig.GetEnvPrefix()}resgrid-evt-sr";
			});

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

			services.Configure<ForwardedHeadersOptions>(options =>
			{
				options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
				options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse($"::ffff:{WebConfig.IngressProxyNetwork}"), WebConfig.IngressProxyNetworkCidr));
			});

			services.Configure<IdentityOptions>(options =>
			{
				options.ClaimsIdentity.UserNameClaimType = Claims.Name;
				options.ClaimsIdentity.UserIdClaimType = Claims.Subject;
				options.ClaimsIdentity.RoleClaimType = Claims.Role;
			});

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
					options.SetTokenEndpointUris(SystemBehaviorConfig.ResgridApiBaseUrl + "/api/v4/connect/token");

					//options.SetIssuer(new Uri(Configuration["AppSettings:IssuerUrl"]));

					options.SetAccessTokenLifetime(TimeSpan.FromMinutes(OidcConfig.AccessTokenExpiryMinutes));
					options.SetRefreshTokenLifetime(TimeSpan.FromDays(OidcConfig.RefreshTokenExpiryDays));

					// Enable the password and the refresh token flows.
					options.AllowRefreshTokenFlow();
					//options.AllowPasswordFlow();
					//	   .AllowRefreshTokenFlow();

					// Accept anonymous clients (i.e clients that don't send a client_id).
					options.AcceptAnonymousClients();

					options.AddEncryptionCertificate(new X509Certificate2(Convert.FromBase64String(OidcConfig.EncryptionCert)));
					options.AddSigningCertificate(new X509Certificate2(Convert.FromBase64String(OidcConfig.SigningCert)));

					options.SetConfigurationEndpointUris($"{SystemBehaviorConfig.ResgridApiBaseUrl}/.well-known/openid-configuration");
					//options.SetIntrospectionEndpointUris($"{SystemBehaviorConfig.ResgridApiBaseUrl}/.well-known/openid-configuration");

					//options.AddEncryptionKey(new SymmetricSecurityKey(
					//	Convert.FromBase64String(OidcConfig.Key)));

					// Register the signing and encryption credentials.
					//options.AddDevelopmentEncryptionCertificate()
					//	   .AddDevelopmentSigningCertificate();

					// Register the ASP.NET Core host and configure the ASP.NET Core-specific options.
					options.UseAspNetCore()
						   .EnableTokenEndpointPassthrough();
				})
				.AddValidation(options =>
				{
					// Import the configuration from the local OpenIddict server instance.
					//options.UseLocalServer();

					options.SetIssuer(SystemBehaviorConfig.ResgridApiBaseUrl);
					options.AddAudiences(JwtConfig.EventsClientId);

					options.UseIntrospection()
						.SetClientId(JwtConfig.EventsClientId)
						.SetClientSecret(JwtConfig.EventsClientSecret);

					options.UseSystemNetHttp();

					// Register the ASP.NET Core host.
					options.UseAspNetCore();
				});

			var tokenValidationParameters = new TokenValidationParameters
			{
				// The signing key must match!
				ValidateIssuerSigningKey = true,
				//IssuerSigningKey = signingKey,

				// Validate the JWT Issuer (iss) claim
				ValidateIssuer = false,
				ValidIssuer = JwtConfig.Issuer,

				// Validate the JWT Audience (aud) claim
				ValidateAudience = true,
				ValidAudience = JwtConfig.Audience,

				// Validate the token expiry
				ValidateLifetime = true,

				// If you want to allow a certain amount of clock drift, set that here:
				ClockSkew = TimeSpan.Zero,

				IssuerSigningKey = new X509SecurityKey(new X509Certificate2(Convert.FromBase64String(OidcConfig.SigningCert)))
			};

			services.AddAuthentication(options =>
			{
				// Identity made Cookie authentication the default.
				// However, we want JWT Bearer Auth to be the default.
				options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
			}).AddJwtBearer(options =>
			{
				// Configure the Authority to the expected value for
				// the authentication provider. This ensures the token
				// is appropriately validated.
				options.Authority = JwtConfig.Issuer;// SystemBehaviorConfig.ResgridApiBaseUrl;
				options.MetadataAddress = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/.well-known/openid-configuration";
				options.RequireHttpsMetadata = false;
				options.Audience = JwtConfig.Audience;
				options.TokenValidationParameters = tokenValidationParameters;

				// We have to hook the OnMessageReceived event in order to
				// allow the JWT authentication handler to read the access
				// token from the query string when a WebSocket or
				// Server-Sent Events request comes in.

				// Sending the access token in the query string is required due to
				// a limitation in Browser APIs. We restrict it to only calls to the
				// SignalR hub in this code.
				// See https://docs.microsoft.com/aspnet/core/signalr/security#access-token-logging
				// for more information about security considerations when using
				// the query string to transmit the access token.
				options.Events = new JwtBearerEvents
				{
					OnMessageReceived = context =>
					{
						var accessToken = context.Request.Query["access_token"];

						// If the request is for our hub...
						var path = context.HttpContext.Request.Path;
						if (!string.IsNullOrEmpty(accessToken) &&
							(path.StartsWithSegments("/geolocationHub")))
						{
							// Read the token out of the query string
							var token = System.Uri.UnescapeDataString(accessToken);
							context.Token = token;
						}
						return Task.CompletedTask;
					}
				};
			});

			//services.AddHostedService<Worker>();
		}

		public void ConfigureContainer(ContainerBuilder builder)
		{
			builder.RegisterType<HttpContextAccessor>().As<IHttpContextAccessor>().SingleInstance();
			//builder.RegisterType<EventingHubService>().As<EventingHubService>().SingleInstance();

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
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			var forwardOpts = new ForwardedHeadersOptions
			{
				ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
			};
			app.UseForwardedHeaders(forwardOpts);

			this.AutofacContainer = app.ApplicationServices.GetAutofacRoot();
			var eventAggregator = this.AutofacContainer.Resolve<IEventAggregator>();
			var outbound = this.AutofacContainer.Resolve<IOutboundEventProvider>();
			var eventService = this.AutofacContainer.Resolve<ICoreEventService>();

			this.Locator = new AutofacServiceLocator(this.AutofacContainer);
			ServiceLocator.SetLocatorProvider(() => this.Locator);

			//app.UseHttpsRedirection();

			app.UseCors(x => x
				.AllowAnyMethod()
				.AllowAnyHeader()
				.SetIsOriginAllowed(origin => true) // allow any origin
				.AllowCredentials()); // allow credentials

			app.UseAuthentication();

			app.UseRouting();

			app.UseAuthorization();

			app.UseWebSockets(new WebSocketOptions
			 {
				 KeepAliveInterval = TimeSpan.FromSeconds(120),
			 });

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();

				endpoints.MapHub<EventingHub>("/eventingHub");
				endpoints.MapHub<GeolocationHub>("/geolocationHub");
			});
		}
	}
}
