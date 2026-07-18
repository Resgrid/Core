using System.Collections.Generic;
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
	public class CallRespondersActionHandlerTests
	{
		[Test]
		public async Task HandleAsync_WhenCallScopedStatesAreNotCurrent_ExcludesPersonnelAndUnits()
		{
			// Arrange
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetCallByIdAsync(42, true)).ReturnsAsync(new Call
			{
				CallId = 42,
				DepartmentId = 1,
				Number = "26-1",
				Name = "Structure Fire"
			});

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewCallAsync("user-1", 42)).ReturnsAsync(true);

			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetActionLogsForCallAsync(1, 42)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog
				{
					ActionLogId = 10,
					DepartmentId = 1,
					UserId = "responder-1",
					ActionTypeId = (int)ActionTypes.OnScene,
					DestinationId = 42
				}
			});
			actionLogs.Setup(x => x.GetLastActionLogsForDepartmentAsync(1, false, false)).ReturnsAsync(new List<ActionLog>
			{
				new ActionLog
				{
					ActionLogId = 11,
					DepartmentId = 1,
					UserId = "responder-1",
					ActionTypeId = (int)ActionTypes.Responding,
					DestinationId = 99
				}
			});

			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitStatesForCallAsync(1, 42)).ReturnsAsync(new List<UnitState>
			{
				new UnitState
				{
					UnitStateId = 20,
					UnitId = 5,
					State = (int)UnitStateTypes.OnScene,
					DestinationId = 42,
					Unit = new Unit { UnitId = 5, DepartmentId = 1, Name = "Engine 1" }
				}
			});
			units.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(1)).ReturnsAsync(new List<UnitState>
			{
				new UnitState
				{
					UnitStateId = 21,
					UnitId = 5,
					State = (int)UnitStateTypes.Responding,
					DestinationId = 99
				}
			});
			units.Setup(x => x.GetUnitsForDepartmentAsync(1)).ReturnsAsync(new List<Unit>
			{
				new Unit { UnitId = 5, DepartmentId = 1, Name = "Engine 1" }
			});

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>
			{
				new UserProfile { UserId = "responder-1", FirstName = "Alex", LastName = "Responder" }
			});

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusAsync(1, It.IsAny<ActionLog>()))
				.ReturnsAsync(new CustomStateDetail { ButtonText = "On Scene" });
			customStates.Setup(x => x.GetCustomUnitStateAsync(It.IsAny<UnitState>()))
				.ReturnsAsync(new CustomStateDetail { ButtonText = "On Scene" });

			var handler = new CallRespondersActionHandler(
				calls.Object,
				actionLogs.Object,
				units.Object,
				customStates.Object,
				profiles.Object,
				authorization.Object);

			var intent = new ChatbotIntent { Type = ChatbotIntentType.CallResponders };
			intent.Parameters["callId"] = "42";
			intent.Parameters["mode"] = "all";
			var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1 };

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage { Text = "who is on call 42" }, intent, session);

			// Assert
			response.Processed.Should().BeTrue();
			response.Text.Should().Contain("No personnel or units");
			response.Text.Should().NotContain("Alex Responder");
			response.Text.Should().NotContain("Engine 1");
			actionLogs.Verify(x => x.GetLastActionLogsForDepartmentAsync(1, false, false), Times.Once);
			units.Verify(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(1), Times.Once);
		}

		[TestCase("enroute", "Alex Enroute", "Blair Onscene")]
		[TestCase("onscene", "Blair Onscene", "Alex Enroute")]
		public async Task HandleAsync_WithResponderMode_FiltersUsingClassifierMode(string mode,
			string expectedResponder, string excludedResponder)
		{
			// Arrange
			var calls = new Mock<ICallsService>();
			calls.Setup(x => x.GetCallByIdAsync(42, true)).ReturnsAsync(new Call
			{
				CallId = 42,
				DepartmentId = 1,
				Number = "26-1",
				Name = "Structure Fire"
			});

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(x => x.CanUserViewCallAsync("user-1", 42)).ReturnsAsync(true);

			var currentLogs = new List<ActionLog>
			{
				new ActionLog
				{
					ActionLogId = 10,
					DepartmentId = 1,
					UserId = "enroute-user",
					ActionTypeId = (int)ActionTypes.Responding,
					DestinationId = 42
				},
				new ActionLog
				{
					ActionLogId = 11,
					DepartmentId = 1,
					UserId = "onscene-user",
					ActionTypeId = (int)ActionTypes.OnScene,
					DestinationId = 42
				}
			};
			var actionLogs = new Mock<IActionLogsService>();
			actionLogs.Setup(x => x.GetActionLogsForCallAsync(1, 42)).ReturnsAsync(currentLogs);
			actionLogs.Setup(x => x.GetLastActionLogsForDepartmentAsync(1, false, false)).ReturnsAsync(currentLogs);

			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitStatesForCallAsync(1, 42)).ReturnsAsync(new List<UnitState>());
			units.Setup(x => x.GetAllLatestStatusForUnitsByDepartmentIdAsync(1)).ReturnsAsync(new List<UnitState>());

			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(x => x.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>
			{
				new UserProfile { UserId = "enroute-user", FirstName = "Alex", LastName = "Enroute" },
				new UserProfile { UserId = "onscene-user", FirstName = "Blair", LastName = "Onscene" }
			});

			var customStates = new Mock<ICustomStateService>();
			customStates.Setup(x => x.GetCustomPersonnelStatusAsync(1, It.IsAny<ActionLog>()))
				.ReturnsAsync((int _, ActionLog log) => new CustomStateDetail
				{
					ButtonText = log.ActionTypeId == (int)ActionTypes.Responding ? "Responding" : "On Scene"
				});

			var handler = new CallRespondersActionHandler(calls.Object, actionLogs.Object, units.Object,
				customStates.Object, profiles.Object, authorization.Object);
			var intent = new ChatbotIntent { Type = ChatbotIntentType.CallResponders };
			intent.Parameters["callId"] = "42";
			intent.Parameters["mode"] = mode;
			var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1, Culture = "en" };

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage(), intent, session);

			// Assert
			response.Processed.Should().BeTrue();
			response.Text.Should().Contain(expectedResponder);
			response.Text.Should().NotContain(excludedResponder);
		}
	}
}
