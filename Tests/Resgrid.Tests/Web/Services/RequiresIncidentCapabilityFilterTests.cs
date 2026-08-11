using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authorization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Filters;

namespace Resgrid.Tests.Web.Services
{
	/// <summary>
	/// Unit tests for <see cref="RequiresIncidentCapabilityAttribute"/> — the per-endpoint incident-capability gate
	/// layered on top of the broad Command_* claims. Verifies the three Call-resolution strategies, the
	/// allow/deny decision, the ownership check, and the fail-open behaviour when the target Call can't be classified.
	/// </summary>
	[TestFixture]
	public class RequiresIncidentCapabilityFilterTests
	{
		private const int DepartmentId = 10;
		private const string UserId = "user-1";

		private Mock<IIncidentCommandService> _service;

		[SetUp]
		public void SetUp()
		{
			_service = new Mock<IIncidentCommandService>(MockBehavior.Loose);
		}

		[Test]
		public async Task AllowsRequest_WhenUserHasRequiredCapability_ViaIncidentCommandId()
		{
			// Arrange — SaveNode-style: body carries IncidentCommandId, which resolves to its owned Call.
			_service.Setup(s => s.GetCommandByIdAsync("cmd-1"))
				.ReturnsAsync(new IncidentCommand { CallId = 5, DepartmentId = DepartmentId });
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 5, UserId))
				.ReturnsAsync(IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageStructure);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageStructure);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["node"] = new CommandStructureNode { IncidentCommandId = "cmd-1" } });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
		}

		[Test]
		public async Task Returns403_WhenUserLacksRequiredCapability_ViaIncidentCommandId()
		{
			// Arrange — same path, but the user's role only grants ViewBoard.
			_service.Setup(s => s.GetCommandByIdAsync("cmd-1"))
				.ReturnsAsync(new IncidentCommand { CallId = 5, DepartmentId = DepartmentId });
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 5, UserId))
				.ReturnsAsync(IncidentCapabilities.ViewBoard);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageStructure);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["node"] = new CommandStructureNode { IncidentCommandId = "cmd-1" } });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeFalse();
			context.Result.Should().BeOfType<ObjectResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
		}

		[Test]
		public async Task AllowsRequest_WhenCapabilityPresent_ViaExplicitRouteCallId()
		{
			// Arrange — EvaluateAccountability/{callId}: callId comes from the route, no body.
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 7, UserId))
				.ReturnsAsync(IncidentCapabilities.All);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageAccountability);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["callId"] = 7 },
				routeValues: new Dictionary<string, object> { ["callId"] = 7 });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
			_service.Verify(s => s.GetCommandByIdAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task AllowsRequest_WhenCapabilityPresent_ViaBodyCallId()
		{
			// Arrange — CreateAdHocUnit-style: the body object exposes CallId (no IncidentCommandId).
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 9, UserId))
				.ReturnsAsync(IncidentCapabilities.ViewBoard | IncidentCapabilities.ManageResources);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageResources);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["unit"] = new IncidentAdHocUnit { CallId = 9 } });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
		}

		[Test]
		public async Task FailsOpen_WhenCallCannotBeResolved()
		{
			// Arrange — a release-style entity-id route (no callId / IncidentCommandId / CallId on the request).
			// The filter must NOT block; the broad Command_* claim still governs. Capabilities are never evaluated.
			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.AssignResources);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["resourceAssignmentId"] = "ra-1" });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
			_service.Verify(s => s.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task FailsOpen_WhenIncidentCommandBelongsToAnotherDepartment()
		{
			// Arrange — a forged IncidentCommandId pointing at another department must not classify the request,
			// so the filter falls through to fail-open (the service-layer ownership guard then rejects it).
			_service.Setup(s => s.GetCommandByIdAsync("cmd-foreign"))
				.ReturnsAsync(new IncidentCommand { CallId = 5, DepartmentId = 999 });

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageStructure);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["node"] = new CommandStructureNode { IncidentCommandId = "cmd-foreign" } });

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
			_service.Verify(s => s.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task FailsOpen_WhenCallerHasNoIdentity()
		{
			// Arrange — no claims principal; identity can't be established, so defer to the [Authorize] gate.
			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.ManageCommand);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["callId"] = 7 },
				routeValues: new Dictionary<string, object> { ["callId"] = 7 },
				authenticated: false);

			// Act
			var nextCalled = await Invoke(filter, context);

			// Assert
			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
			_service.Verify(s => s.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public void EstablishCommand_IsNotCapabilityGated_SoBootstrapCanCreateTheCommand()
		{
			// EstablishCommand bootstraps the command — before it runs there is no command/commander/role and thus no
			// IncidentCapabilities, so it must NOT carry [RequiresIncidentCapability]; it stays on the Command_Create claim.
			var method = typeof(IncidentCommandController).GetMethod(nameof(IncidentCommandController.EstablishCommand));
			method.Should().NotBeNull();
			method.GetCustomAttributes(typeof(RequiresIncidentCapabilityAttribute), true).Should().BeEmpty();
			method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Should().NotBeEmpty();
		}

		#region Helpers

		private ActionExecutingContext BuildContext(
			IDictionary<string, object> args,
			IDictionary<string, object> routeValues = null,
			bool authenticated = true,
			ICommandAccessService commandAccessService = null,
			bool withoutCommandAccessService = false)
		{
			// Tests that don't exercise the commander gate get an allow-all gate so they keep testing
			// only capabilities; withoutCommandAccessService simulates a misconfigured host (filter must 500).
			var httpContext = new DefaultHttpContext
			{
				RequestServices = new StubServiceProvider(_service.Object,
					withoutCommandAccessService ? null : commandAccessService ?? CommandGate(true))
			};

			if (authenticated)
			{
				var identity = new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "TestAuth");
				httpContext.User = new ClaimsPrincipal(identity);
			}

			var routeData = new RouteData();
			if (routeValues != null)
			{
				foreach (var kvp in routeValues)
					routeData.Values[kvp.Key] = kvp.Value;
			}

			var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
			return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), args, controller: new object());
		}

		private static async Task<bool> Invoke(RequiresIncidentCapabilityAttribute filter, ActionExecutingContext context)
		{
			var nextCalled = false;

			Task<ActionExecutedContext> Next()
			{
				nextCalled = true;
				return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), context.Controller));
			}

			await filter.OnActionExecutionAsync(context, Next);
			return nextCalled;
		}

		private sealed class StubServiceProvider : IServiceProvider
		{
			private readonly IIncidentCommandService _service;
			private readonly ICommandAccessService _commandAccessService;

			public StubServiceProvider(IIncidentCommandService service, ICommandAccessService commandAccessService = null)
			{
				_service = service;
				_commandAccessService = commandAccessService;
			}

			public object GetService(Type serviceType)
			{
				if (serviceType == typeof(IIncidentCommandService))
					return _service;

				if (serviceType == typeof(ICommandAccessService))
					return _commandAccessService;

				return null;
			}
		}

		private static ICommandAccessService CommandGate(bool allowed)
		{
			var mock = new Mock<ICommandAccessService>();
			mock.Setup(x => x.CanUseCommandAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(allowed);
			return mock.Object;
		}

		#endregion Helpers

		#region Commander permission gate

		[Test]
		public async Task Returns500_WhenTheCommanderGateServiceCannotBeResolved()
		{
			// The department gate is mandatory; a host that cannot resolve it is misconfigured and must
			// deny loudly instead of silently dropping the check.
			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.AssignResources);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["callId"] = 7 },
				routeValues: new Dictionary<string, object> { ["callId"] = 7 },
				withoutCommandAccessService: true);

			var nextCalled = await Invoke(filter, context);

			nextCalled.Should().BeFalse();
			context.Result.Should().BeOfType<StatusCodeResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
			_service.Verify(s => s.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Returns403_WhenTheDepartmentHasNotAuthorizedTheUserAsACommander()
		{
			// The capability check never even runs: a member the department hasn't authorized has no
			// business on the board surface, whatever ICS role happens to be recorded against them.
			_service.Setup(s => s.GetCommandByIdAsync("cmd-1"))
				.ReturnsAsync(new IncidentCommand { CallId = 5, DepartmentId = DepartmentId });
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 5, UserId)).ReturnsAsync(IncidentCapabilities.All);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.AssignResources);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["node"] = new CommandStructureNode { IncidentCommandId = "cmd-1" } },
				commandAccessService: CommandGate(false));

			var nextCalled = await Invoke(filter, context);

			nextCalled.Should().BeFalse();
			context.Result.Should().BeOfType<ObjectResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			_service.Verify(s => s.GetCapabilitiesForUserAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task AllowsRequest_WhenAuthorizedAsACommanderAndHoldingTheCapability()
		{
			// A dispatcher granted the command permission assists through the same path: the gate passes,
			// then the assist capability set carries the action.
			_service.Setup(s => s.GetCommandByIdAsync("cmd-1"))
				.ReturnsAsync(new IncidentCommand { CallId = 5, DepartmentId = DepartmentId });
			_service.Setup(s => s.GetCapabilitiesForUserAsync(DepartmentId, 5, UserId))
				.ReturnsAsync(IncidentRoleCapabilityMap.CommandAssistCapabilities);

			var filter = new RequiresIncidentCapabilityAttribute(IncidentCapabilities.AssignResources);
			var context = BuildContext(
				args: new Dictionary<string, object> { ["node"] = new CommandStructureNode { IncidentCommandId = "cmd-1" } },
				commandAccessService: CommandGate(true));

			var nextCalled = await Invoke(filter, context);

			nextCalled.Should().BeTrue();
			context.Result.Should().BeNull();
		}

		[Test]
		public async Task TheAssistSetCoversWhatDispatchNeedsAndNothingMore()
		{
			await Task.CompletedTask;
			var assist = IncidentRoleCapabilityMap.CommandAssistCapabilities;

			// What a dispatcher assisting an incident actually does.
			assist.Should().HaveFlag(IncidentCapabilities.ViewBoard);
			assist.Should().HaveFlag(IncidentCapabilities.AssignResources);
			assist.Should().HaveFlag(IncidentCapabilities.ManageResources);
			assist.Should().HaveFlag(IncidentCapabilities.ManageTimers);
			assist.Should().HaveFlag(IncidentCapabilities.ManageAccountability);

            // Assisting is not commanding: the shape of the incident and its lifecycle stay with whoever
            // actually holds command.
			assist.Should().NotHaveFlag(IncidentCapabilities.ManageCommand);
			assist.Should().NotHaveFlag(IncidentCapabilities.ManageStructure);
			assist.Should().NotHaveFlag(IncidentCapabilities.ManagePublicInformation);
			assist.Should().NotHaveFlag(IncidentCapabilities.ManageDocuments);
		}

		#endregion Commander permission gate
	}
}
