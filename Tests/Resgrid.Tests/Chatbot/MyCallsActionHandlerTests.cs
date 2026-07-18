using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	public class MyCallsActionHandlerTests
	{
		[Test]
		public async Task HandleAsync_WhenUserDispatchIsOlderThanFormerScanLimit_IncludesCall()
		{
			// Arrange
			var activeCalls = CreateActiveCalls();
			var oldestCall = activeCalls.Last();
			oldestCall.Name = "Older User Call";
			oldestCall.Dispatches.Add(new CallDispatch { UserId = "user-1" });
			var calls = CreateCallsService(activeCalls);
			var handler = new MyCallsActionHandler(calls.Object, Mock.Of<IUnitsService>());
			var intent = new ChatbotIntent { Type = ChatbotIntentType.MyCalls };
			var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1 };

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage { Text = "what calls am I on" }, intent, session);

			// Assert
			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("Older User Call");
			calls.Verify(x => x.PopulateCallData(It.IsAny<Call>(), true, false, false, false, false, false, false, false, false, false),
				Times.Exactly(activeCalls.Count));
		}

		[Test]
		public async Task HandleAsync_WhenUnitDispatchIsOlderThanFormerScanLimit_IncludesCall()
		{
			// Arrange
			var activeCalls = CreateActiveCalls();
			var oldestCall = activeCalls.Last();
			oldestCall.Name = "Older Unit Call";
			oldestCall.UnitDispatches.Add(new CallDispatchUnit { UnitId = 7 });
			var calls = CreateCallsService(activeCalls);
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitsForDepartmentAsync(1)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 7, DepartmentId = 1, Name = "Rescue 7" }
			});
			var handler = new MyCallsActionHandler(calls.Object, units.Object);
			var intent = new ChatbotIntent { Type = ChatbotIntentType.UnitCalls };
			intent.Parameters["unitName"] = "Rescue 7";
			var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1 };

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage { Text = "what calls is Rescue 7 on" }, intent, session);

			// Assert
			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("Older Unit Call");
			calls.Verify(x => x.PopulateCallData(It.IsAny<Call>(), false, false, false, false, true, false, false, false, false, false),
				Times.Exactly(activeCalls.Count));
		}

		private static List<Call> CreateActiveCalls()
		{
			var now = DateTime.UtcNow;
			return Enumerable.Range(1, 26).Select(index => new Call
			{
				CallId = index,
				DepartmentId = 1,
				State = (int)CallStates.Active,
				LoggedOn = now.AddMinutes(-index),
				Name = $"Call {index}",
				Dispatches = new List<CallDispatch>(),
				UnitDispatches = new List<CallDispatchUnit>()
			}).ToList();
		}

		private static Mock<ICallsService> CreateCallsService(List<Call> activeCalls)
		{
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetActiveCallsByDepartmentAsync(1)).ReturnsAsync(activeCalls);
			calls.Setup(x => x.PopulateCallData(
				It.IsAny<Call>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>(),
				It.IsAny<bool>()))
				.ReturnsAsync((Call call, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _, bool _) => call);
			return calls;
		}
	}
}
