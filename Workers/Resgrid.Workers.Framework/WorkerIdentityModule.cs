using System;
using Autofac;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Resgrid.Repositories.DataRepository.Stores;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Workers.Framework
{
	/// <summary>
	/// ASP.NET Identity's UserManager for the worker host (Department Import plan section 11.2: workers create imported
	/// accounts through member provisioning). The worker has no service collection, so this mirrors the Web host's Autofac
	/// registrations and supplies the options, logger and service provider UserManager needs. Account rules match the Web
	/// host's AddIdentity configuration.
	/// </summary>
	public sealed class WorkerIdentityModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<IdentityUserStore>().As<IUserStore<IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<UserValidator<IdentityUser>>().As<IUserValidator<IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordValidator<IdentityUser>>().As<IPasswordValidator<IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<PasswordHasher<IdentityUser>>().As<IPasswordHasher<IdentityUser>>().InstancePerLifetimeScope();
			builder.RegisterType<UpperInvariantLookupNormalizer>().As<ILookupNormalizer>().InstancePerLifetimeScope();
			builder.RegisterType<IdentityErrorDescriber>().AsSelf().SingleInstance();
			builder.RegisterInstance(Options.Create(new PasswordHasherOptions())).As<IOptions<PasswordHasherOptions>>();
			builder.RegisterInstance(Options.Create(Create())).As<IOptions<IdentityOptions>>();
			builder.RegisterInstance(NullLogger<UserManager<IdentityUser>>.Instance).As<ILogger<UserManager<IdentityUser>>>();
			builder.Register<IServiceProvider>(c => new ScopeServiceProvider(c.Resolve<ILifetimeScope>())).InstancePerLifetimeScope();
			builder.RegisterType<UserManager<IdentityUser>>().AsSelf().InstancePerLifetimeScope();
		}

		/// <summary>The Web host's account rules (Startup AddIdentity): unique email and the same username characters.</summary>
		public static IdentityOptions Create()
		{
			var options = new IdentityOptions();
			options.User.RequireUniqueEmail = true;
			options.User.AllowedUserNameCharacters = " abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@";
			options.Password.RequireDigit = true;
			options.Password.RequireLowercase = true;
			options.Password.RequireUppercase = true;
			options.Password.RequireNonAlphanumeric = false;
			options.Password.RequiredLength = 8;
			options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
			options.Lockout.MaxFailedAccessAttempts = 5;
			options.Lockout.AllowedForNewUsers = true;
			return options;
		}

		private sealed class ScopeServiceProvider : IServiceProvider
		{
			private readonly ILifetimeScope _scope;

			public ScopeServiceProvider(ILifetimeScope scope)
			{
				_scope = scope;
			}

			public object GetService(Type serviceType) => _scope.ResolveOptional(serviceType);
		}
	}
}
