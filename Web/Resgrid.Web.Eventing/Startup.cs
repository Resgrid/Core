using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resgrid.Config;
using Resgrid.Providers.AddressVerification;
using Resgrid.Providers.Audio;
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
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Stores;
using Resgrid.Services;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing
{
	public class Startup
	{
		public Startup(IConfiguration configuration)
		{
			Configuration = configuration;
		}

		public IConfiguration Configuration { get; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddSignalR(hubOptions =>
			{
				hubOptions.EnableDetailedErrors = true;
			}).AddStackExchangeRedis(CacheConfig.RedisConnectionString, options => {
				options.Configuration.ChannelPrefix = $"{Config.SystemBehaviorConfig.GetEnvPrefix()}resgrid-evt-sr";
			});

			services.AddControllers();
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
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			//app.UseHttpsRedirection();

			app.UseRouting();

			app.UseAuthorization();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapControllers();

				endpoints.MapHub<EventingHub>("/eventingHub");
			});
		}
	}
}
