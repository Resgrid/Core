using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Models;
using Resgrid.Chatbot.Services;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Filters;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 3.8 (non-Sync), revised for View Command Boards: Command_View is a plan-level claim, so every read on
	/// the incident command surface passes the per-incident board read gate (IIncidentCommandService.CanViewBoardAsync: the
	/// department's View Command Boards permission, or being on the incident). Every board write is gated live on the
	/// department's run-command permission and the member's incident capabilities, never on the admin-only Command_Update /
	/// Command_Create claims. The assistant's incident answers read the same board and take the same gate.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RecordsCommandBoardGateTests
	{
		private const int Dept = 9;
		private const string Member = "member";
		private const int CallId = 77;

		private static readonly Type[] CommandControllers =
		{
			typeof(IncidentCommandController), typeof(IncidentResourcesController), typeof(IncidentRolesController), typeof(IncidentVoiceController),
			typeof(IncidentReportingController), typeof(MutualAidController)
		};

		private Mock<ICommandAccessService> _commandAccess;
		private Mock<IIncidentCommandService> _incidentCommand;
		private System.Diagnostics.Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_commandAccess = new Mock<ICommandAccessService>();
			_incidentCommand = new Mock<IIncidentCommandService>();
			_incidentCommand.Setup(s => s.GetNotesForCallAsync(Dept, CallId, It.IsAny<bool>())).ReturnsAsync(new List<IncidentNote> { new IncidentNote { Body = "Primary search complete" } });
			_activity = new System.Diagnostics.Activity(nameof(RecordsCommandBoardGateTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private object Build(Type type)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				p.ParameterType == typeof(ICommandAccessService) ? _commandAccess.Object :
				p.ParameterType == typeof(IIncidentCommandService) ? (object)_incidentCommand.Object :
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (ControllerBase)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = http };
			return controller;
		}

		/// <summary>Every action whose only gate is the plan-level Command_View claim (no incident capability filter).</summary>
		private static IEnumerable<MethodInfo> ViewOnlyActions()
			=> CommandControllers.SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				.Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy == ResgridResources.Command_View))
				.Where(m => !m.GetCustomAttributes<RequiresIncidentCapabilityAttribute>().Any());

		/// <summary>The incident reads: refused outright to a member who may not read the board. The list filters instead; capabilities answer 0.</summary>
		private static IEnumerable<MethodInfo> BoardReads()
			=> ViewOnlyActions().Where(m => (m.Name.StartsWith("Get") || m.Name.StartsWith("Download") || m.Name.StartsWith("Export"))
				&& m.Name != "GetCommandList" && m.Name != "GetMyCapabilities" && m.Name != "GetResourceIncidentView" && m.DeclaringType != typeof(MutualAidController));

		/// <summary>Writes that run before any incident capability can exist, or that anyone on the incident may make.</summary>
		private static readonly string[] UngatedWrites = { "EstablishCommand", "ReopenCommand", "SendMessageToCommand", "LogTransmission" };

		private static object[] Arguments(MethodInfo method)
			=> method.GetParameters().Select(p =>
				p.ParameterType == typeof(int) ? CallId :
				p.ParameterType == typeof(bool) ? false :
				p.ParameterType == typeof(string) ? "id-1" :
				p.ParameterType == typeof(CancellationToken) ? CancellationToken.None :
				Activator.CreateInstance(p.ParameterType)).ToArray();

		private static async Task<IActionResult> InvokeAsync(object controller, MethodInfo method)
		{
			var task = (Task)method.Invoke(controller, Arguments(method));
			await task;
			var value = task.GetType().GetProperty("Result").GetValue(task);
			return value is IConvertToActionResult convertible ? convertible.Convert() : (IActionResult)value;
		}

		private void MemberMayNotReadBoards()
		{
			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(false);
			_commandAccess.Setup(c => c.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_incidentCommand.Setup(s => s.CanViewBoardAsync(Dept, It.IsAny<int>(), Member)).ReturnsAsync(false);
			_incidentCommand.Setup(s => s.ResolveCallIdForEntityAsync(Dept, It.IsAny<IncidentEntityKind>(), It.IsAny<string>())).ReturnsAsync(CallId);
		}

		[Test]
		public void The_sweep_covers_the_reads_the_audit_named()
		{
			BoardReads().Select(m => m.Name).Should().Contain(new[]
			{
				"GetNotes", "GetAttachments", "DownloadAttachment", "GetWeather", "GetAccountability", "GetNeeds", "GetNeedUpdates", "GetNeedEntities",
				"GetIncidentMaps", "GetTimeline", "GetAdHocUnits", "GetAdHocPersonnel", "GetRoles", "GetChannelsForCall", "GetTransmissionLog",
				"GetCommandBoard", "GetCommandBoardById", "GetCommandForCall", "GetIncidentSummary", "ExportIncident"
			});
		}

		[Test]
		public async Task Every_board_read_refuses_a_member_who_may_not_read_boards_and_is_not_on_the_incident()
		{
			MemberMayNotReadBoards();

			foreach (var method in BoardReads())
			{
				var result = await InvokeAsync(Build(method.DeclaringType), method);
				result.Should().BeOfType<ObjectResult>($"{method.DeclaringType.Name}.{method.Name} is a command-board read")
					.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden, $"{method.DeclaringType.Name}.{method.Name} refuses with 403, never 401");
			}

			_incidentCommand.Verify(s => s.GetNotesForCallAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			_incidentCommand.Verify(s => s.GetTimelineForCallAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_member_on_the_incident_reads_its_notes_without_the_department_wide_permission()
		{
			_commandAccess.Setup(c => c.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_incidentCommand.Setup(s => s.CanViewBoardAsync(Dept, CallId, Member)).ReturnsAsync(true);

			var result = await ((IncidentCommandController)Build(typeof(IncidentCommandController))).GetNotes(CallId);

			result.Value.Data.Should().ContainSingle(n => n.Body == "Primary search complete");
		}

		[Test]
		public async Task The_command_list_shows_a_member_only_the_incidents_they_are_on()
		{
			_commandAccess.Setup(c => c.CanViewCommandBoardsAsync(Dept, Member)).ReturnsAsync(false);
			_incidentCommand.Setup(s => s.GetCommandSummariesForDepartmentAsync(Dept, false)).ReturnsAsync(new List<IncidentCommandSummary>
			{
				new IncidentCommandSummary { CallId = CallId }, new IncidentCommandSummary { CallId = 78 }
			});
			_incidentCommand.Setup(s => s.GetViewableBoardCallIdsAsync(Dept, It.Is<IEnumerable<int>>(ids => ids.OrderBy(x => x).SequenceEqual(new[] { CallId, 78 })), Member))
				.ReturnsAsync(new HashSet<int> { CallId });

			var result = await ((IncidentCommandController)Build(typeof(IncidentCommandController))).GetCommandList();

			result.Value.Data.Select(x => x.CallId).Should().Equal(CallId);
			// One batched check for the whole list, never one per incident.
			_incidentCommand.Verify(s => s.CanViewBoardAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Logging_a_transmission_without_a_body_is_a_bad_request_not_an_error()
		{
			var controller = (IncidentVoiceController)Build(typeof(IncidentVoiceController));

			var result = await controller.LogTransmission(null);

			result.Result.Should().BeOfType<BadRequestResult>();
			_incidentCommand.Verify(s => s.CanViewBoardAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task My_capabilities_are_what_the_writes_accept_plus_whether_the_board_can_be_read()
		{
			var controller = (IncidentRolesController)Build(typeof(IncidentRolesController));

			MemberMayNotReadBoards();
			(await controller.GetMyCapabilities(CallId)).Value.Value.Should().Be(0);

			_incidentCommand.Setup(s => s.CanViewBoardAsync(Dept, CallId, Member)).ReturnsAsync(true);
			(await controller.GetMyCapabilities(CallId)).Value.Value.Should().Be((int)IncidentCapabilities.ViewBoard);

			_incidentCommand.Setup(s => s.GetEffectiveCapabilitiesAsync(Dept, CallId, Member)).ReturnsAsync(IncidentCapabilities.AssignResources);
			(await controller.GetMyCapabilities(CallId)).Value.Value.Should().Be((int)(IncidentCapabilities.AssignResources | IncidentCapabilities.ViewBoard));
		}

		[Test]
		public async Task Establishing_command_needs_the_run_command_permission_not_department_admin()
		{
			var controller = (IncidentCommandController)Build(typeof(IncidentCommandController));

			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(false);
			var refused = await controller.EstablishCommand(new Resgrid.Web.Services.Models.v4.IncidentCommand.EstablishCommandInput { CallId = CallId });
			refused.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(true);
			_incidentCommand.Setup(s => s.EstablishCommandAsync(Dept, CallId, Member, null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IncidentCommand { IncidentCommandId = "ic-1", CallId = CallId, DepartmentId = Dept });
			var established = await controller.EstablishCommand(new Resgrid.Web.Services.Models.v4.IncidentCommand.EstablishCommandInput { CallId = CallId });
			established.Value.Data.IncidentCommandId.Should().Be("ic-1");
		}

		[Test]
		public void No_board_endpoint_rides_the_admin_only_command_write_claims()
		{
			var boardControllers = new[] { typeof(IncidentCommandController), typeof(IncidentResourcesController), typeof(IncidentRolesController), typeof(IncidentVoiceController) };
			var actions = boardControllers.SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
				.Where(m => m.GetCustomAttributes<AuthorizeAttribute>().Any()).ToList();

			actions.SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Select(a => a.Policy)
				.Should().NotContain(new[] { ResgridResources.Command_Update, ResgridResources.Command_Create, ResgridResources.Command_Delete });

			var writes = actions.Where(m => m.GetCustomAttributes().Any(a => a is HttpPostAttribute || a is HttpPutAttribute || a is HttpDeleteAttribute));
			foreach (var write in writes.Where(m => !UngatedWrites.Contains(m.Name)))
			{
				write.GetCustomAttributes<RequiresIncidentCapabilityAttribute>().Should()
					.NotBeEmpty($"{write.DeclaringType.Name}.{write.Name} is a board write and must check the member's incident capabilities");
			}
		}

		[Test]
		public void Writes_that_name_a_row_only_by_id_resolve_its_incident()
		{
			var idOnly = new Dictionary<string, IncidentEntityKind>
			{
				["MoveResource"] = IncidentEntityKind.ResourceAssignment, ["ReleaseResource"] = IncidentEntityKind.ResourceAssignment,
				["DeleteNode"] = IncidentEntityKind.CommandStructureNode, ["RemoveNote"] = IncidentEntityKind.IncidentNote,
				["RemoveAttachment"] = IncidentEntityKind.IncidentAttachment, ["UpdateObjectiveProgress"] = IncidentEntityKind.TacticalObjective,
				["CompleteObjective"] = IncidentEntityKind.TacticalObjective, ["SetNeedStatus"] = IncidentEntityKind.IncidentNeed,
				["AcknowledgeTimer"] = IncidentEntityKind.IncidentTimer, ["DeleteAnnotation"] = IncidentEntityKind.IncidentMapAnnotation,
				["DeleteIncidentMap"] = IncidentEntityKind.IncidentMap, ["RemoveRole"] = IncidentEntityKind.IncidentRoleAssignment,
				["ReleaseAdHocUnit"] = IncidentEntityKind.IncidentAdHocUnit, ["ReleaseAdHocPersonnel"] = IncidentEntityKind.IncidentAdHocPersonnel,
				["AssignPersonnelToUnit"] = IncidentEntityKind.IncidentAdHocPersonnel
			};
			var methods = new[] { typeof(IncidentCommandController), typeof(IncidentResourcesController), typeof(IncidentRolesController) }
				.SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).ToList();

			foreach (var pair in idOnly)
			{
				var gate = methods.Single(m => m.Name == pair.Key).GetCustomAttribute<RequiresIncidentCapabilityAttribute>();
				gate.Should().NotBeNull(pair.Key);
				gate.Entity.Should().Be(pair.Value, pair.Key);
				gate.EntityIdName.Should().NotBeNullOrWhiteSpace(pair.Key);
			}
		}

		[Test]
		public async Task The_assistant_reads_no_board_for_a_member_who_may_not_read_it()
		{
			MemberMayNotReadBoards();
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = CallId, DepartmentId = Dept });
			var authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			authorization.Setup(a => a.CanUserViewCallAsync(Member, CallId)).ReturnsAsync(true);
			var resolver = new IncidentContextResolver(calls.Object, _incidentCommand.Object, Mock.Of<IIncidentResourcesService>(), authorization.Object,
				Mock.Of<IDispatchScopeService>());
			var session = new ChatbotSession { DepartmentId = Dept, UserId = Member, Context = new Dictionary<string, string> { [IncidentContextResolver.IncidentCallIdContextKey] = CallId.ToString() } };

			var context = await resolver.ResolveAsync(new ChatbotIntent { Type = ChatbotIntentType.IncidentPar }, session);

			context.IsUnauthorized.Should().BeTrue();
			context.Board.Should().BeNull();
			_incidentCommand.Verify(s => s.GetCommandBoardAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}
	}
}
