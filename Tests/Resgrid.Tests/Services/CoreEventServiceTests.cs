using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
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
			var service = new CoreEventService(eventAggregator.Object);

			await service.IncidentCommandUpdatedAsync(42, 1001);

			eventAggregator.Verify(x => x.SendMessage(
				It.Is<IncidentCommandUpdatedEvent>(e => e.DepartmentId == 42 && e.CallId == 1001)),
				Times.Once);
		}
	}
}
