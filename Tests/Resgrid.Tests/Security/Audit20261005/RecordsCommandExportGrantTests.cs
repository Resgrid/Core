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
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services.Records;
using Resgrid.Tests.Rms;
using MvcControllers = Resgrid.Web.Areas.User.Controllers;
using V4Controllers = Resgrid.Web.Services.Controllers.v4;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 3.21: the saved-report CSV is a file that leaves Resgrid, so it needs ExportRecords on top of
	/// running the report (MVC and v4), and mailing a bulk packet to a typed address is external sharing that needs
	/// ManageRecordReports or a department administrator on top of ExportRecords.
	/// </summary>
	[TestFixture]
	[NonParallelizable]
	public class RecordsCommandExportGrantTests
	{
		private const int Dept = 9;
		private const string Member = "member";
		private const string ReportId = "report-1";

		private Mock<IRecordSavedReportsService> _reports;
		private Mock<IRecordsAuthorizationService> _authorization;
		private System.Diagnostics.Activity _activity;

		[SetUp]
		public void SetUp()
		{
			_reports = new Mock<IRecordSavedReportsService>();
			_reports.Setup(r => r.RunAsync(Dept, Member, ReportId, It.IsAny<CancellationToken>())).ReturnsAsync(new RecordReportResult { Name = "Monthly runs" });
			_reports.Setup(r => r.ToCsv(It.IsAny<RecordReportResult>())).Returns("a,b");
			_authorization = new Mock<IRecordsAuthorizationService>();
			_activity = new System.Diagnostics.Activity(nameof(RecordsCommandExportGrantTests)).Start();
		}

		[TearDown]
		public void TearDown()
		{
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
			_activity?.Stop();
		}

		private static DefaultHttpContext Http()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.PrimarySid, Member), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return http;
		}

		private T Build<T>(params object[] overrides) where T : ControllerBase
		{
			var constructor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				overrides.FirstOrDefault(o => p.ParameterType.IsInstanceOfType(o)) ??
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			var controller = (T)constructor.Invoke(arguments);
			controller.ControllerContext = new ControllerContext { HttpContext = Http() };
			return controller;
		}

		private IRecordsCutoverService Cutover()
		{
			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true });
			return cutover.Object;
		}

		[Test]
		public async Task Mvc_saved_report_csv_is_refused_without_ExportRecords()
		{
			var result = await Build<MvcControllers.RecordSavedReportsController>(_reports.Object, _authorization.Object).RunCsv(ReportId, CancellationToken.None);

			result.Should().BeOfType<ForbidResult>();
			_reports.Verify(r => r.RunAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Mvc_saved_report_csv_downloads_with_ExportRecords()
		{
			_authorization.Setup(a => a.HasPermissionAsync(Member, Dept, PermissionTypes.ExportRecords)).ReturnsAsync(true);

			var result = await Build<MvcControllers.RecordSavedReportsController>(_reports.Object, _authorization.Object).RunCsv(ReportId, CancellationToken.None);

			result.Should().BeOfType<FileContentResult>().Which.ContentType.Should().Be("text/csv");
		}

		[Test]
		public async Task V4_saved_report_csv_is_refused_without_ExportRecords()
		{
			var result = await Build<V4Controllers.RecordSavedReportsController>(_reports.Object, _authorization.Object, Cutover()).RunCsv(ReportId, CancellationToken.None);

			result.Should().BeOfType<ForbidResult>();
			_reports.Verify(r => r.RunAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task V4_saved_report_csv_downloads_with_ExportRecords()
		{
			_authorization.Setup(a => a.HasPermissionAsync(Member, Dept, PermissionTypes.ExportRecords)).ReturnsAsync(true);

			var result = await Build<V4Controllers.RecordSavedReportsController>(_reports.Object, _authorization.Object, Cutover()).RunCsv(ReportId, CancellationToken.None);

			result.Should().BeOfType<FileContentResult>();
		}

		[Test]
		public void Both_csv_endpoints_carry_the_export_policy()
		{
			Policies(typeof(MvcControllers.RecordSavedReportsController), "RunCsv").Should().Contain(ResgridResources.Record_Export);
			Policies(typeof(V4Controllers.RecordSavedReportsController), "RunCsv").Should().Contain(ResgridResources.Record_Export);
		}

		private static IEnumerable<string> Policies(Type controller, string action)
			=> controller.GetMethod(action).GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy);

		#region Bulk packet delivery

		private const string Exporter = "exporter";

		private RecordsBulkPacketService PacketService(Mock<IRecordsAuthorizationService> authorization, Mock<IEmailService> email)
		{
			var documents = new Mock<IRecordsDocumentService>();
			documents.Setup(d => d.GetAsync(Dept, Exporter, "r1", RmsRecordKind.Operational, It.IsAny<string>(), true))
				.ReturnsAsync(new RecordDocument { RecordId = "r1", RecordNumber = "RG-1", RevisionId = "rev-r1", RevisionNumber = 1, FinalizedOn = new DateTime(2026, 9, 1), OriginalChecksum = "chk", ContentChecksum = "chk", ContentJson = "{}" });
			documents.Setup(d => d.RenderHtmlAsync(Dept, Exporter, It.IsAny<RecordDocument>())).ReturnsAsync("<html><body>RG-1</body></html>");
			var rows = new Mock<IRmsOperationalRecordsRepository>();
			rows.Setup(r => r.GetByIdsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<RmsOperationalRecord>
			{
				new RmsOperationalRecord { RmsOperationalRecordId = "r1", DepartmentId = Dept, RecordNumber = "RG-1", DefinitionKey = "shift-log", DefinitionVersion = 1, CurrentRevisionId = "rev-r1", State = (int)RmsRecordState.Finalized }
			});
			var pdf = new Mock<IPdfProvider>();
			pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns(System.Text.Encoding.UTF8.GetBytes("%PDF-packet"));
			var runs = new Mock<IRmsExportRunsRepository>();
			runs.Setup(r => r.InsertAsync(It.IsAny<RmsExportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExportRun run, CancellationToken c, bool b) => run);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Pine Valley Fire" });
			email.Setup(e => e.SendReportDeliveryEmail(It.IsAny<EmailNotification>())).ReturnsAsync(true);

			authorization.Setup(a => a.HasPermissionAsync(Exporter, Dept, PermissionTypes.ExportRecords)).ReturnsAsync(true);
			authorization.Setup(a => a.CanUserViewRecordAsync(Exporter, "r1", Dept)).ReturnsAsync(true);
			return new RecordsBulkPacketService(documents.Object, Mock.Of<IRecordsService>(), rows.Object, runs.Object, authorization.Object, new PassthroughRecordsProtection(),
				Mock.Of<IRmsAccessAuditsRepository>(), departments.Object, email.Object, pdf.Object, Mock.Of<IUnitOfWork>());
		}

		private static RecordsBulkPacketRequest Request(string deliverTo) => new RecordsBulkPacketRequest { RecordIds = new List<string> { "r1" }, Title = "Insurance", DeliverToEmail = deliverTo };

		[Test]
		public async Task Mailing_a_packet_out_is_refused_to_an_exporter_without_ManageRecordReports()
		{
			var authorization = new Mock<IRecordsAuthorizationService>();
			var email = new Mock<IEmailService>();

			Func<Task> act = () => PacketService(authorization, email).BuildPacketAsync(Dept, Exporter, Request("claims@example.org"));

			await act.Should().ThrowAsync<UnauthorizedAccessException>();
			email.Verify(e => e.SendReportDeliveryEmail(It.IsAny<EmailNotification>()), Times.Never);
		}

		[Test]
		public async Task A_packet_without_delivery_still_needs_only_ExportRecords()
		{
			var authorization = new Mock<IRecordsAuthorizationService>();
			var result = await PacketService(authorization, new Mock<IEmailService>()).BuildPacketAsync(Dept, Exporter, Request(null));

			result.Processed.Should().Be(1);
		}

		[TestCase(PermissionTypes.ManageRecordReports)]
		[TestCase(null)]
		public async Task Mailing_a_packet_out_works_for_a_report_manager_or_a_department_admin(PermissionTypes? grant)
		{
			var authorization = new Mock<IRecordsAuthorizationService>();
			if (grant.HasValue)
				authorization.Setup(a => a.HasPermissionAsync(Exporter, Dept, grant.Value)).ReturnsAsync(true);
			else
				authorization.Setup(a => a.IsDepartmentAdminAsync(Exporter, Dept)).ReturnsAsync(true);
			var email = new Mock<IEmailService>();

			var result = await PacketService(authorization, email).BuildPacketAsync(Dept, Exporter, Request("claims@example.org"));

			result.Delivered.Should().BeTrue();
			email.Verify(e => e.SendReportDeliveryEmail(It.Is<EmailNotification>(n => n.To == "claims@example.org")), Times.Once);
		}

		#endregion
	}
}
