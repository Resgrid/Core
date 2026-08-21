using System;
using Autofac;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChatProvisioningEventServiceTests
	{
		[Test]
		public void constructor_should_leave_incident_authorization_updates_to_the_eventing_worker()
		{
			var eventAggregator = new Mock<IEventAggregator>();
			var lifetimeScope = new Mock<ILifetimeScope>();

			_ = new ChatProvisioningEventService(eventAggregator.Object, lifetimeScope.Object);

			eventAggregator.Verify(x => x.AddListener(
				It.IsAny<Action<IncidentCommandUpdatedEvent>>()), Times.Never);
		}
	}
}
