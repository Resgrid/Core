using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.DispatcherDesk
{
	/// <summary>
	/// Autofac composition for the dispatcher desk (M0324): presence, breaks and conversations reach routing through Lazy&lt;&gt;,
	/// routing reaches calls through Lazy&lt;&gt;, and ShiftsService reaches the roster through Lazy&lt;&gt;. A missing
	/// registration or a construction cycle must fail here (including when a Lazy is first used), not at app or worker start.
	/// </summary>
	[TestFixture]
	public class DispatcherDeskCompositionTests : TestBase
	{
		private static readonly Type[] DeskServices =
		{
			typeof(IDispatcherDeskConfigService), typeof(IShiftDispatchersService), typeof(IDispatcherDeskNotifier), typeof(IDispatcherPresenceService),
			typeof(IDispatcherRoutingService), typeof(IDispatcherBreaksService), typeof(IDispatcherConversationService), typeof(IDispatcherVoiceService),
			typeof(IDispatcherDeskWorkerService), typeof(IDispatcherDeskPushProvider), typeof(IDispatcherVoiceProvider), typeof(IShiftsService),
			typeof(IIntakeConversationRouter), typeof(IDispatcherDeskPendingCalls)
		};

		[Test]
		public void Desk_services_resolve_and_their_lazy_dependencies_activate()
		{
			// The AI enrichment service's own graph (audit/config repositories, LLM clients) is registered by DataModule and the AI
			// modules in every production host but not in the shared test container; it has its own tests, so the scope stands it in.
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope(builder =>
				builder.RegisterInstance(Moq.Mock.Of<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>()).As<Resgrid.Model.AiDispatch.IAiDispatchEnrichmentService>());

			foreach (var type in DeskServices)
			{
				var service = scope.Resolve(type);
				service.Should().NotBeNull(type.Name);

				// Force every Lazy<T> field the implementation holds: a cycle or missing registration shows up only here.
				foreach (var field in service.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
					.Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Lazy<>)))
				{
					var lazy = field.GetValue(service);
					if (lazy == null)
						continue;

					var value = field.FieldType.GetProperty("Value").GetValue(lazy);
					value.Should().NotBeNull($"{type.Name}.{field.Name}");
				}
			}

			// Intake resolves every router: the desk's is the one (plan F2).
			scope.Resolve<IEnumerable<IIntakeConversationRouter>>().Should().ContainSingle(x => x is DispatcherIntakeConversationRouter);
		}
	}
}
