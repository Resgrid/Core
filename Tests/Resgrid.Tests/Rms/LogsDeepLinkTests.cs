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
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Logs;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Deep-link tests (RMS plan section 7): after a department activates Records, every old Logs and Unit Logs
	/// URL a member may have bookmarked or been emailed still resolves — list, detail, export, attachment, the
	/// list JSON and the training chart — while every old new/edit/delete link redirects without touching a
	/// service write. Both halves are asserted against the real controllers with the cutover engaged, so a
	/// hidden button is never the evidence.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class LogsDeepLinkTests
	{
		private const int Dept = 3;
		private const string Me = "member";

		private Mock<IWorkLogsService> _workLogs;
		private Mock<IUnitsService> _units;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IDepartmentsService> _departments;
		private Mock<Resgrid.Model.Services.IAuthorizationService> _authorization;
		private Mock<IProtectedReadService> _protectedRead;
		private LogsController _logs;
		private UnitsController _unitsController;
		private DefaultHttpContext _http;

		[SetUp]
		public void SetUp()
		{
			_workLogs = new Mock<IWorkLogsService>();
			_units = new Mock<IUnitsService>();
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.AreLegacyWritesBlockedAsync(Dept)).ReturnsAsync(true);
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Test FD", TimeZone = "UTC" });
			_departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>());
			_authorization = new Mock<Resgrid.Model.Services.IAuthorizationService>();
			_authorization.Setup(a => a.CanUserDeleteWorkLogAsync(Me, It.IsAny<int>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewUnitAsync(Me, It.IsAny<int>())).ReturnsAsync(true);
			_protectedRead = new Mock<IProtectedReadService>();
			_protectedRead.Setup(p => p.ResolveCallLogsForReadAsync(Dept, It.IsAny<IReadOnlyList<CallLog>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());
			_protectedRead.Setup(p => p.ResolveLogsForReadAsync(Dept, It.IsAny<IReadOnlyList<Log>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());
			_protectedRead.Setup(p => p.ResolveUnitLogsForReadAsync(Dept, It.IsAny<IReadOnlyList<UnitLog>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedReadResult());

			var log = new Log { LogId = 41, DepartmentId = Dept, LogType = (int)LogTypes.Training, Narrative = "Legacy drill", Course = "CPR", LoggedOn = new DateTime(2025, 3, 1), Users = new List<LogUser>(), Units = new List<LogUnit>() };
			_workLogs.Setup(w => w.GetWorkLogByIdAsync(41)).ReturnsAsync(log);
			_workLogs.Setup(w => w.GetWorkLogByIdAsync(99)).ReturnsAsync(new Log { LogId = 99, DepartmentId = Dept + 1, Users = new List<LogUser>(), Units = new List<LogUnit>() });
			_workLogs.Setup(w => w.GetAttachmentsForLogAsync(41)).ReturnsAsync(new List<LogAttachment>());
			_workLogs.Setup(w => w.GetAttachmentByIdAsync(7)).ReturnsAsync(new LogAttachment { LogAttachmentId = 7, LogId = 41, FileName = "roster.pdf", Type = "application/pdf", Data = new byte[] { 1, 2, 3 } });
			_workLogs.Setup(w => w.GetAllCallLogsForUserAsync(Me)).ReturnsAsync(new List<CallLog>());
			_workLogs.Setup(w => w.GetAllLogsForUserAsync(Me)).ReturnsAsync(new List<Log> { log });
			_workLogs.Setup(w => w.GetLogYearsByDeptartmentAsync(Dept)).ReturnsAsync(new List<string> { "2025" });
			_workLogs.Setup(w => w.GetAllLogsForDepartmentAsync(Dept)).ReturnsAsync(new List<Log> { log });
			_workLogs.Setup(w => w.GetAllLogsForDepartmentAndYearAsync(Dept, "2025")).ReturnsAsync(new List<Log> { log });
			_workLogs.Setup(w => w.PopulateLogData(It.IsAny<Log>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync((Log l, bool a, bool b) => l);
			_workLogs.Setup(w => w.GetAllLogsByDepartmentDateRangeAsync(Dept, LogTypes.Training, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(new List<Log> { log });

			_units.Setup(u => u.GetUnitsForDepartmentAsync(Dept)).ReturnsAsync(new List<Unit>());
			_units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5" });
			_units.Setup(u => u.GetLogsForUnitAsync(5)).ReturnsAsync(new List<UnitLog> { new UnitLog { UnitLogId = 1, UnitId = 5, Narrative = "Legacy unit log", Timestamp = new DateTime(2025, 3, 1) } });

			_http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, Me),
					new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()),
					new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.View),
					new Claim(ResgridClaimTypes.Resources.Log, ResgridClaimTypes.Actions.Delete),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			_http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = _http };

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup>());
			_logs = new LogsController(_departments.Object, Mock.Of<IUsersService>(), Mock.Of<ICallsService>(), groups.Object, Mock.Of<ICommunicationService>(), Mock.Of<IQueueService>(),
				_authorization.Object, _workLogs.Object, Mock.Of<IEventAggregator>(), _units.Object, _protectedRead.Object, _cutover.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };

			_unitsController = new UnitsController(_departments.Object, Mock.Of<IUsersService>(), _units.Object, _authorization.Object, Mock.Of<ILimitsService>(), groups.Object, Mock.Of<ICallsService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<ICustomStateService>(), Mock.Of<IGeoService>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<INovuProvider>(),
				Mock.Of<IMappingService>(), Mock.Of<IUserDefinedFieldsService>(), Mock.Of<IUdfRenderingService>(), Mock.Of<IStringLocalizer<Resgrid.Localization.Common>>(), Mock.Of<IPersonnelRolesService>(),
				_protectedRead.Object, _cutover.Object)
			{ ControllerContext = new ControllerContext { HttpContext = _http } };
		}

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		#region Reads still resolve

		[Test]
		public async Task The_logs_list_still_resolves_and_announces_read_only()
		{
			var view = (await _logs.Index()).Should().BeOfType<ViewResult>().Which;
			var model = view.Model.Should().BeOfType<LogsIndexView>().Which;
			model.LegacyReadOnly.Should().BeTrue();
			model.WorkLogs.Should().ContainSingle(l => l.LogId == 41);
			model.Years.Select(y => y.Value).Should().Equal("2025");
		}

		[Test]
		public async Task A_bookmarked_log_detail_still_renders_and_offers_no_delete()
		{
			var view = (await _logs.View(41)).Should().BeOfType<ViewResult>().Which;
			view.ViewName.Should().Be("ViewLog");
			var model = view.Model.Should().BeOfType<ViewLogsView>().Which;
			model.WorkLog.LogId.Should().Be(41);
			model.WorkLog.Narrative.Should().Be("Legacy drill");
			// The member holds Log:Delete and is a department admin, which is exactly the case the plan calls out:
			// "even by a department administrator or previous creator".
			model.CanDelete.Should().BeFalse("legacy rows cannot be deleted after activation, whoever the viewer is");
		}

		[Test]
		public async Task A_bookmarked_export_and_attachment_still_resolve()
		{
			var export = (await _logs.LogExport(41)).Should().BeOfType<ViewResult>().Which;
			export.Model.Should().BeOfType<LogExportView>().Which.WorkLog.LogId.Should().Be(41);

			var file = (await _logs.GetAttachment(41, 7)).Should().BeOfType<FileContentResult>().Which;
			file.FileDownloadName.Should().Be("roster.pdf");
			file.FileContents.Should().Equal(1, 2, 3);
		}

		[Test]
		public async Task The_list_json_and_training_chart_still_resolve()
		{
			(await _logs.GetLogsList("2025")).Should().BeOfType<JsonResult>();
			(await _logs.GetLogsList(null)).Should().BeOfType<JsonResult>();
			(await _logs.TrainingPerMonth()).Should().BeOfType<JsonResult>();
		}

		[Test]
		public async Task Another_departments_log_never_renders_through_an_old_link()
		{
			(await _logs.View(99)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			(await _logs.LogExport(99)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
		}

		[Test]
		public async Task Unit_logs_still_render_read_only()
		{
			var view = (await _unitsController.ViewLogs(5)).Should().BeOfType<ViewResult>().Which;
			var model = view.Model.Should().BeOfType<Resgrid.Web.Areas.User.Models.Units.ViewLogsView>().Which;
			model.LegacyReadOnly.Should().BeTrue();
			model.Logs.Should().ContainSingle(l => l.Narrative == "Legacy unit log");
		}

		#endregion

		#region Writes cannot mutate anything

		[Test]
		public async Task Old_new_log_links_redirect_before_any_write()
		{
			(await _logs.NewLog()).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			var posted = await _logs.NewLog(new NewLogView { Log = new Log { Narrative = "Late entry" }, LogType = LogTypes.Work }, null, null, CancellationToken.None);
			posted.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");

			_workLogs.Verify(w => w.SaveLogAsync(It.IsAny<Log>(), It.IsAny<CancellationToken>()), Times.Never);
			_workLogs.Verify(w => w.SaveLogAttachmentAsync(It.IsAny<LogAttachment>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Old_delete_links_redirect_before_any_write()
		{
			(await _logs.DeleteWorkLog(41, CancellationToken.None)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
			_workLogs.Verify(w => w.DeleteLogAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			_authorization.Verify(a => a.CanUserDeleteWorkLogAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never, "the guard runs before the per-log authorization question");
		}

		[Test]
		public async Task Old_unit_log_create_links_redirect_before_any_write()
		{
			(await _unitsController.AddLog(5)).Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ViewLogs");
			var posted = await _unitsController.AddLog(new Resgrid.Web.Areas.User.Models.Units.AddLogView { Log = new UnitLog { UnitId = 5, Narrative = "Late unit entry" } }, CancellationToken.None);
			posted.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("ViewLogs");
			_units.Verify(u => u.SaveUnitLogAsync(It.IsAny<UnitLog>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Before_activation_the_same_links_still_write()
		{
			_cutover.Setup(c => c.AreLegacyWritesBlockedAsync(Dept)).ReturnsAsync(false);
			_workLogs.Setup(w => w.DeleteLogAsync(41, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			await _logs.DeleteWorkLog(41, CancellationToken.None);
			_workLogs.Verify(w => w.DeleteLogAsync(41, It.IsAny<CancellationToken>()), Times.Once, "the guard is the cutover, not the controller");
			((ViewLogsView)((ViewResult)await _logs.View(41)).Model).CanDelete.Should().BeTrue("before activation an administrator may still delete");
		}

		#endregion

		#region The route surface itself is pinned

		/// <summary>
		/// Every legacy action a deep link can point at, with the policy it must keep. Renaming or dropping one
		/// silently breaks bookmarks and emailed links inside the read-only module; this list makes that a test
		/// failure instead of a support ticket.
		/// </summary>
		[TestCase(typeof(LogsController), "Index", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "View", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "LogExport", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "GetAttachment", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "GetLogsList", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "TrainingPerMonth", ResgridResources.Log_View)]
		[TestCase(typeof(LogsController), "NewLog", ResgridResources.Log_Create)]
		[TestCase(typeof(LogsController), "DeleteWorkLog", ResgridResources.Log_Delete)]
		[TestCase(typeof(UnitsController), "ViewLogs", ResgridResources.UnitLog_View)]
		[TestCase(typeof(UnitsController), "AddLog", ResgridResources.UnitLog_Create)]
		public void Legacy_deep_link_actions_keep_their_names_and_policies(Type controller, string action, string policy)
		{
			var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == action && typeof(Task<IActionResult>).IsAssignableFrom(m.ReturnType)).ToList();
			methods.Should().NotBeEmpty($"{controller.Name}.{action} is a legacy deep-link target");
			foreach (var method in methods)
				method.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Contain(policy);
		}

		[Test]
		public void Every_legacy_mutation_action_is_guarded_by_the_cutover_before_its_body()
		{
			// The write guard is a service-boundary rule too (LegacyWriteGuardTests); this pins the controller half so
			// a future edit cannot move the redirect below a write.
			var source = ReadControllerSource("LogsController.cs");
			foreach (var action in new[] { "NewLog()", "NewLog(NewLogView model", "DeleteWorkLog(int logId" })
			{
				var start = source.IndexOf("Task<IActionResult> " + action, StringComparison.Ordinal);
				start.Should().BeGreaterThan(0, action);
				var body = source.Substring(start, Math.Min(600, source.Length - start));
				var guard = body.IndexOf("await _recordsCutoverService.AreLegacyWritesBlockedAsync", StringComparison.Ordinal);
				guard.Should().BeGreaterThan(0, $"{action} must check the cutover");
				body.IndexOf("await ", StringComparison.Ordinal).Should().Be(guard, $"{action} must check the cutover before any other await");
			}
		}

		private static string ReadControllerSource(string file)
		{
			var directory = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null)
			{
				var candidate = System.IO.Path.Combine(directory.FullName, "Web", "Resgrid.Web", "Areas", "User", "Controllers", file);
				if (System.IO.File.Exists(candidate))
					return System.IO.File.ReadAllText(candidate);
				directory = directory.Parent;
			}
			Assert.Inconclusive($"{file} not found relative to the test assembly.");
			return null;
		}

		#endregion
	}
}
