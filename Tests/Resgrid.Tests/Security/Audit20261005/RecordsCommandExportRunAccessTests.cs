using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Tests.Rms;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 2.16: a stored export run is only the caller's to read when they rendered it, or when it is a
	/// template run (scheduled or a colleague's) and they hold ManageRecordReports while the template still exists. A bulk
	/// packet stays its builder's alone, and a run that carries restricted columns also needs ViewRestrictedRecords.
	/// </summary>
	[TestFixture]
	public class RecordsCommandExportRunAccessTests
	{
		private const int Dept = 9;
		private const string Manager = "manager";
		private const string Builder = "builder";
		private const string TemplateId = "tpl-1";
		private const string RestrictedTemplateId = "tpl-restricted";

		private Mock<IRmsExportTemplatesRepository> _templates;
		private Mock<IRmsExportRunsRepository> _runs;
		private Mock<IRecordsAuthorizationService> _authorization;
		private List<RmsExportRun> _stored;
		private RecordsExportService _service;

		[SetUp]
		public void SetUp()
		{
			_stored = new List<RmsExportRun>();
			_templates = new Mock<IRmsExportTemplatesRepository>();
			_templates.Setup(t => t.GetByIdForDepartmentAsync(Dept, TemplateId)).ReturnsAsync(new RmsExportTemplate
			{
				RmsExportTemplateId = TemplateId, DepartmentId = Dept, TemplateKey = "monthly", ColumnsJson = JsonConvert.SerializeObject(new[] { "record.id", "record.number" })
			});
			_templates.Setup(t => t.GetByIdForDepartmentAsync(Dept, RestrictedTemplateId)).ReturnsAsync(new RmsExportTemplate
			{
				RmsExportTemplateId = RestrictedTemplateId, DepartmentId = Dept, TemplateKey = "dor", IncludeRestricted = true,
				ColumnsJson = JsonConvert.SerializeObject(new[] { "record.id", "details.case_number" })
			});

			_runs = new Mock<IRmsExportRunsRepository>();
			_runs.Setup(r => r.GetWithDataAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _stored.FirstOrDefault(r => r.RmsExportRunId == id));
			_runs.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _stored.FirstOrDefault(r => r.RmsExportRunId == id));
			_runs.Setup(r => r.GetForTemplateAsync(Dept, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((int d, string template, int take) => _stored.Where(r => r.TemplateId == template).ToList());

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.HasPermissionAsync(Manager, Dept, PermissionTypes.ManageRecordReports)).ReturnsAsync(true);

			_service = new RecordsExportService(_templates.Object, _runs.Object, Mock.Of<IRecordsService>(), Mock.Of<IIncidentReportsService>(), Mock.Of<IRmsOperationalRecordsRepository>(),
				Mock.Of<IRmsIncidentReportsRepository>(), _authorization.Object, new PassthroughRecordsProtection(), Mock.Of<IDomainEventOutboxService>(), Mock.Of<IRmsAccessAuditsRepository>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentGroupsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IPdfProvider>(), Mock.Of<IUnitOfWork>());
		}

		private RmsExportRun Run(string id, string templateId, string generatedBy, string redactedFieldsJson = null)
		{
			var run = new RmsExportRun
			{
				RmsExportRunId = id, DepartmentId = Dept, TemplateId = templateId, TemplateKey = templateId == RecordsBulkPacketService.PacketTemplateKey ? RecordsBulkPacketService.PacketTemplateKey : "monthly",
				GeneratedByUserId = generatedBy, ExpiresOn = DateTime.UtcNow.AddDays(10), Data = new byte[] { 1, 2, 3 }, FileName = id + ".csv", RedactedFieldsJson = redactedFieldsJson
			};
			_stored.Add(run);
			return run;
		}

		[Test]
		public async Task A_bulk_packet_opens_only_for_the_member_who_built_it()
		{
			Run("packet", RecordsBulkPacketService.PacketTemplateKey, Builder);

			(await _service.GetRunAsync(Dept, Manager, "packet", true)).Should().BeNull("ManageRecordReports does not open another member's packet");
			(await _service.GetRunAsync(Dept, Builder, "packet", true)).Should().NotBeNull();
			(await _service.GetRunsAsync(Dept, Manager, RecordsBulkPacketService.PacketTemplateKey, 50)).Should().BeEmpty();
		}

		[Test]
		public async Task A_scheduled_run_is_visible_to_a_report_manager_while_its_template_exists()
		{
			Run("scheduled", TemplateId, null);

			(await _service.GetRunAsync(Dept, Manager, "scheduled", true)).Should().NotBeNull();
			(await _service.GetRunAsync(Dept, "member", "scheduled", true)).Should().BeNull("a member without ManageRecordReports only sees runs they rendered");
		}

		[Test]
		public async Task A_run_of_a_deleted_template_is_refused_to_a_manager_who_did_not_render_it()
		{
			Run("orphan", "tpl-deleted", Builder);

			(await _service.GetRunAsync(Dept, Manager, "orphan", true)).Should().BeNull();
		}

		[Test]
		public async Task A_member_reads_their_own_rendered_run()
		{
			Run("mine", TemplateId, Builder);

			(await _service.GetRunAsync(Dept, Builder, "mine", true)).Should().NotBeNull();
		}

		[Test]
		public async Task A_run_carrying_restricted_columns_needs_the_restricted_grant()
		{
			Run("restricted", RestrictedTemplateId, null);

			(await _service.GetRunAsync(Dept, Manager, "restricted", true)).Should().BeNull();

			_authorization.Setup(a => a.HasPermissionAsync(Manager, Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
			(await _service.GetRunAsync(Dept, Manager, "restricted", true)).Should().NotBeNull();
		}

		[Test]
		public async Task A_run_that_withheld_every_restricted_column_needs_no_restricted_grant()
		{
			Run("withheld", RestrictedTemplateId, Builder, JsonConvert.SerializeObject(new { protected_fields = new string[0], withheld_columns = new[] { "details.case_number" } }));

			(await _service.GetRunAsync(Dept, Manager, "withheld", true)).Should().NotBeNull();
		}

		[Test]
		public async Task The_runs_list_keeps_only_the_runs_the_caller_may_open()
		{
			Run("scheduled", TemplateId, null);
			Run("colleague", TemplateId, Builder);

			(await _service.GetRunsAsync(Dept, Manager, TemplateId, 50)).Select(r => r.RmsExportRunId).Should().BeEquivalentTo("scheduled", "colleague");
			(await _service.GetRunsAsync(Dept, Builder, TemplateId, 50)).Select(r => r.RmsExportRunId).Should().BeEquivalentTo("colleague");
			(await _service.GetRunsAsync(Dept, "member", TemplateId, 50)).Should().BeEmpty();
		}
	}
}
