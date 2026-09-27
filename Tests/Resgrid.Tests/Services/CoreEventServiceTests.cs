using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Covers the IC real-time publish side. IncidentCommandUpdatedAsync must raise an IncidentCommandUpdatedEvent
	/// onto the eventing/topic rail (OutboundEventProvider -> RabbitTopicProvider -> EventingTopic -> Eventing Worker
	/// -> SignalR "incidentCommandUpdated"), mirroring how CallUpdatedEvent drives "callsUpdated" — NOT the CQRS rail.
	/// </summary>
	[TestFixture]
	public class CoreEventServiceTests
	{
		[Test]
		public async Task IncidentCommandUpdatedAsync_RaisesIncidentCommandUpdatedEvent_WithDeptAndCall()
		{
			var eventAggregator = new Mock<IEventAggregator>();
			var service = new CoreEventService(eventAggregator.Object, Mock.Of<ILifetimeScope>());

			await service.IncidentCommandUpdatedAsync(42, 1001);

			eventAggregator.Verify(x => x.SendMessage(
				It.Is<IncidentCommandUpdatedEvent>(e => e.DepartmentId == 42 && e.CallId == 1001)),
				Times.Once);
		}

		/// <summary>
		/// The service is a singleton. The timestamp save runs inside an audited configuration transaction, so each
		/// event must get its own settings service (and unit of work) from a child scope, never one shared root instance.
		/// </summary>
		[Test]
		public void DepartmentSettingsUpdateEvent_SavesTheTimestamp_InItsOwnScopePerEvent()
		{
			var created = new List<Mock<IDepartmentSettingsService>>();
			var builder = new ContainerBuilder();
			builder.Register(_ =>
			{
				var settings = new Mock<IDepartmentSettingsService>();
				settings.Setup(s => s.SaveOrUpdateSettingAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DepartmentSettingTypes>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new DepartmentSetting());
				created.Add(settings);
				return settings.Object;
			}).As<IDepartmentSettingsService>().InstancePerLifetimeScope();
			using var container = builder.Build();

			var bus = new EventAggregator();
			_ = new CoreEventService(bus, container);

			bus.SendMessage(new DepartmentSettingsUpdateEvent { DepartmentId = 42 });
			bus.SendMessage(new DepartmentSettingsUpdateEvent { DepartmentId = 43 });

			created.Should().HaveCount(2);
			created[0].Verify(s => s.SaveOrUpdateSettingAsync(42, It.IsAny<string>(), DepartmentSettingTypes.UpdateTimestamp, It.IsAny<CancellationToken>()), Times.Once);
			created[1].Verify(s => s.SaveOrUpdateSettingAsync(43, It.IsAny<string>(), DepartmentSettingTypes.UpdateTimestamp, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public void DepartmentSettingsUpdateEvent_FailedSave_DoesNotReachThePublisher()
		{
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.SaveOrUpdateSettingAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DepartmentSettingTypes>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("settings store unavailable"));
			var builder = new ContainerBuilder();
			builder.RegisterInstance(settings.Object).As<IDepartmentSettingsService>();
			using var container = builder.Build();

			var bus = new EventAggregator();
			_ = new CoreEventService(bus, container);

			bus.Invoking(b => b.SendMessage(new DepartmentSettingsUpdateEvent { DepartmentId = 42 })).Should().NotThrow();
			settings.Verify(s => s.SaveOrUpdateSettingAsync(42, It.IsAny<string>(), DepartmentSettingTypes.UpdateTimestamp, It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
