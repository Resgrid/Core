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
	/// Audit 2026-10-05, 3.8 (non-Sync): Command_View is a plan-level claim, so every read on the incident command surface
	/// passes the department's own commander gate (CommandAppLogin) the board reads already apply. The assistant's incident
	/// answers read the same board and take the same gate.
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

		[Test]
		public void The_sweep_covers_the_reads_the_audit_named()
		{
			ViewOnlyActions().Select(m => m.Name).Should().Contain(new[]
			{
				"GetNotes", "GetAttachments", "DownloadAttachment", "GetWeather", "GetAccountability", "GetNeeds", "GetNeedUpdates", "GetNeedEntities",
				"GetIncidentMaps", "GetTimeline", "GetAdHocUnits", "GetAdHocPersonnel", "GetRoles", "GetChannelsForCall", "GetTransmissionLog"
			});
		}

		[Test]
		public async Task Every_command_view_read_refuses_a_member_the_department_has_not_made_a_commander()
		{
			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(false);

			foreach (var method in ViewOnlyActions().Where(m => m.Name != "GetResourceIncidentView"))
			{
				var result = await InvokeAsync(Build(method.DeclaringType), method);
				result.Should().BeOfType<UnauthorizedResult>($"{method.DeclaringType.Name}.{method.Name} is a command-board read");
			}

			_incidentCommand.Verify(s => s.GetNotesForCallAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			_incidentCommand.Verify(s => s.GetTimelineForCallAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public async Task A_commander_still_reads_the_notes()
		{
			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(true);

			var result = await ((IncidentCommandController)Build(typeof(IncidentCommandController))).GetNotes(CallId);

			result.Value.Data.Should().ContainSingle(n => n.Body == "Primary search complete");
		}

		[Test]
		public async Task The_assistant_reads_no_board_for_a_member_who_may_not_work_command()
		{
			_commandAccess.Setup(c => c.CanUseCommandAsync(Dept, Member)).ReturnsAsync(false);
			var calls = new Mock<ICallsService>();
			var resolver = new IncidentContextResolver(calls.Object, _incidentCommand.Object, Mock.Of<IIncidentResourcesService>(), Mock.Of<Resgrid.Model.Services.IAuthorizationService>(),
				Mock.Of<IDispatchScopeService>(), _commandAccess.Object);
			var session = new ChatbotSession { DepartmentId = Dept, UserId = Member, Context = new Dictionary<string, string> { [IncidentContextResolver.IncidentCallIdContextKey] = CallId.ToString() } };

			var context = await resolver.ResolveAsync(new ChatbotIntent { Type = ChatbotIntentType.IncidentPar }, session);

			context.IsUnauthorized.Should().BeTrue();
			context.Board.Should().BeNull();
			_incidentCommand.Verify(s => s.GetCommandBoardAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			calls.Verify(c => c.GetCallByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
		}
	}
}
