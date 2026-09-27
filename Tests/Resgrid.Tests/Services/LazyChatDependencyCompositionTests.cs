using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Builder;
using Autofac.Core;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// IncidentCommandService and PermissionsService used to reach the chat and command-access services through
	/// ServiceLocator.Current, which resolves from the ROOT scope and so shared the root unit of work with everything
	/// else resolved there. They now take Lazy&lt;T&gt; constructor parameters, because the dependencies run both ways:
	/// ChatChannelService and ChatPermissionService take IIncidentCommandService, and CommandAccessService takes
	/// IPermissionsService. These tests compose the real classes, with every other dependency a loose mock, to prove
	/// the cycles still resolve and that each Lazy resolves in the caller's own scope.
	/// </summary>
	[TestFixture]
	public sealed class LazyChatDependencyCompositionTests
	{
		private IContainer _container;

		[SetUp]
		public void SetUp()
		{
			var builder = new ContainerBuilder();
			builder.RegisterType<IncidentCommandService>().As<IIncidentCommandService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatChannelService>().As<IChatChannelService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatPermissionService>().As<IChatPermissionService>().InstancePerLifetimeScope();
			builder.RegisterType<CommandAccessService>().As<ICommandAccessService>().InstancePerLifetimeScope();
			builder.RegisterType<PermissionsService>().As<IPermissionsService>().InstancePerLifetimeScope();
			builder.RegisterType<ChatMessageService>().As<IChatMessageService>().InstancePerLifetimeScope();
			builder.RegisterSource(new LooseMockSource());
			_container = builder.Build();
		}

		[TearDown]
		public void TearDown() => _container.Dispose();

		[Test]
		public void Incident_command_chat_dependencies_resolve_through_the_cycle_in_the_callers_scope()
		{
			using var first = _container.BeginLifetimeScope();
			using var second = _container.BeginLifetimeScope();

			// Entering from the chat side constructs IncidentCommandService inside ChatChannelService's resolve.
			var chat = first.Resolve<IChatChannelService>();
			var incident = first.Resolve<IIncidentCommandService>();

			LazyValue<IChatChannelService>(incident, "_chatChannelService").Should().BeSameAs(chat);
			LazyValue<ICommandAccessService>(incident, "_commandAccessService").Should().BeSameAs(first.Resolve<ICommandAccessService>());
			LazyValue<IChatChannelRepository>(incident, "_chatChannelRepository").Should().BeSameAs(first.Resolve<IChatChannelRepository>());

			LazyValue<IChatChannelService>(second.Resolve<IIncidentCommandService>(), "_chatChannelService")
				.Should().BeSameAs(second.Resolve<IChatChannelService>()).And.NotBeSameAs(chat);
		}

		[Test]
		public void Permissions_chat_refresh_dependencies_resolve_through_the_cycle_in_the_callers_scope()
		{
			using var scope = _container.BeginLifetimeScope();

			var permissions = scope.Resolve<IPermissionsService>();

			LazyValue<IChatPermissionService>(permissions, "_chatPermissionService").Should().BeSameAs(scope.Resolve<IChatPermissionService>());
			LazyValue<IChatChannelRepository>(permissions, "_chatChannelRepository").Should().BeSameAs(scope.Resolve<IChatChannelRepository>());
		}

		[Test]
		public void Chat_message_service_can_reach_the_root_for_its_background_push_fan_out()
		{
			using var scope = _container.BeginLifetimeScope();

			var lifetimeScope = typeof(ChatMessageService).GetField("_lifetimeScope", BindingFlags.Instance | BindingFlags.NonPublic)
				.GetValue(scope.Resolve<IChatMessageService>());

			lifetimeScope.Should().BeSameAs(scope);
			((ISharingLifetimeScope)lifetimeScope).RootLifetimeScope.Should().BeSameAs(((ISharingLifetimeScope)scope).RootLifetimeScope);
		}

		private static T LazyValue<T>(object service, string field) =>
			((Lazy<T>)service.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(service)).Value;

		/// <summary>A loose Moq mock, one per scope, for any interface nothing else registers.</summary>
		private sealed class LooseMockSource : IRegistrationSource
		{
			private static readonly Type[] CollectionTypes =
				{ typeof(IEnumerable<>), typeof(ICollection<>), typeof(IList<>), typeof(IReadOnlyCollection<>), typeof(IReadOnlyList<>) };

			public bool IsAdapterForIndividualComponents => false;

			public IEnumerable<IComponentRegistration> RegistrationsFor(Service service, Func<Service, IEnumerable<ServiceRegistration>> registrationAccessor)
			{
				if (service is not IServiceWithType typed || !typed.ServiceType.IsInterface || registrationAccessor(service).Any())
					return Enumerable.Empty<IComponentRegistration>();

				var type = typed.ServiceType;
				if (type.IsGenericType && CollectionTypes.Contains(type.GetGenericTypeDefinition()))
					return Enumerable.Empty<IComponentRegistration>();

				return new[]
				{
					RegistrationBuilder.ForDelegate(type, (c, p) => ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))).Object)
						.As(service).InstancePerLifetimeScope().CreateRegistration()
				};
			}
		}
	}
}
