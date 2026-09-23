using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// M0227 crew and individual time reports: a unit's Crew Time Report covers only that unit, its crew and its equipment, a
	/// single resource files its own report, a subject bills once a day across reports, and a scoped writer never erases the
	/// entries of subjects it may not write.
	/// </summary>
	[TestFixture]
	public class TimeReportScopeTests
	{
		private const int DeptId = 7;
		private static readonly DateTime Day = new DateTime(2026, 9, 21);

		private List<DeploymentTimeReport> _storedReports;
		private List<DeploymentTimeEntry> _storedEntries;
		private Deployment _deployment;
		private int _nextNumber;
		private TimeTrackingService _service;

		[SetUp]
		public void SetUp()
		{
			_storedReports = new List<DeploymentTimeReport>();
			_storedEntries = new List<DeploymentTimeEntry>();
			_nextNumber = 1;
			// Engine 1 (alice, bob, pump), Engine 2 (carol), dave a single resource with no unit.
			_deployment = new Deployment
			{
				DeploymentId = "dep-1", DepartmentId = DeptId, Name = "Ridge Fire", Status = (int)DeploymentStatuses.Active, Currency = "USD", LocalTimeZoneId = "UTC",
				Units =
				{
					new DeploymentUnit { DeploymentUnitId = "unit-1", DeploymentId = "dep-1", DepartmentId = DeptId, UnitId = 1, UnitName = "Engine 1" },
					new DeploymentUnit { DeploymentUnitId = "unit-2", DeploymentId = "dep-1", DepartmentId = DeptId, UnitId = 2, UnitName = "Engine 2" }
				},
				Personnel =
				{
					new DeploymentPersonnel { DeploymentPersonnelId = "per-a", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "alice", DeploymentUnitId = "unit-1", DisplayName = "Alice" },
					new DeploymentPersonnel { DeploymentPersonnelId = "per-b", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "bob", DeploymentUnitId = "unit-1", DisplayName = "Bob" },
					new DeploymentPersonnel { DeploymentPersonnelId = "per-c", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "carol", DeploymentUnitId = "unit-2", DisplayName = "Carol" },
					new DeploymentPersonnel { DeploymentPersonnelId = "per-d", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "dave", DisplayName = "Dave" }
				},
				Equipment = { new DeploymentEquipment { DeploymentEquipmentId = "eq-1", DeploymentId = "dep-1", DepartmentId = DeptId, DeploymentUnitId = "unit-1", FreeTextName = "Pump" } }
			};

			var deploymentService = new Mock<IDeploymentService>();
			deploymentService.Setup(s => s.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(() => _deployment);
			var deployments = new Mock<IDeploymentRepository>();
			deployments.Setup(r => r.GetByIdForDepartmentAsync("dep-1", DeptId)).ReturnsAsync(() => _deployment);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, TimeZone = "UTC" });
			var sequence = new Mock<ITimeReportNumberSequenceRepository>();
			sequence.Setup(s => s.GetNextNumberAsync(DeptId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _nextNumber++);
			var reports = new Mock<IDeploymentTimeReportRepository>();
			reports.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentTimeReport t, CancellationToken _, bool __) => { t.DeploymentTimeReportId ??= Guid.NewGuid().ToString(); _storedReports.RemoveAll(x => x.DeploymentTimeReportId == t.DeploymentTimeReportId); _storedReports.Add(t); return t; });
			reports.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _storedReports.FirstOrDefault(t => t.DeploymentTimeReportId == id));
			reports.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedReports.Where(t => t.DeploymentId == id && !t.IsDeleted).OrderBy(t => t.ReportDate).ToList());
			var entries = new Mock<IDeploymentTimeEntryRepository>();
			entries.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentTimeEntry e, CancellationToken _, bool __) => { e.DeploymentTimeEntryId ??= Guid.NewGuid().ToString(); _storedEntries.RemoveAll(x => x.DeploymentTimeEntryId == e.DeploymentTimeEntryId); _storedEntries.Add(e); return e; });
			entries.Setup(r => r.GetByReportAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedEntries.Where(e => e.DeploymentTimeReportId == id).OrderBy(e => e.SortOrder).ToList());
			entries.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedEntries.Where(e => e.DeploymentId == id).ToList());
			entries.Setup(r => r.DeleteAsync(It.IsAny<DeploymentTimeEntry>(), It.IsAny<CancellationToken>())).ReturnsAsync((DeploymentTimeEntry e, CancellationToken _) => _storedEntries.RemoveAll(x => x.DeploymentTimeEntryId == e.DeploymentTimeEntryId) > 0);

			_service = new TimeTrackingService(deployments.Object, new Mock<IDeploymentPersonnelRepository>().Object, new Mock<IDeploymentUnitRepository>().Object, new Mock<IDeploymentEquipmentRepository>().Object,
				reports.Object, entries.Object, new Mock<IDeploymentExpenseRepository>().Object, new Mock<IDeploymentAttachmentRepository>().Object, sequence.Object, deploymentService.Object, departments.Object,
				new Mock<IUserProfileService>().Object, new Mock<IUnitsService>().Object, new Mock<IEventAggregator>().Object, new Mock<IPdfProvider>().Object, null);
		}

		private static DeploymentTimeEntry Entry(string subject, int hourStart, int hourEnd, string id = null) => new DeploymentTimeEntry
		{
			DeploymentTimeEntryId = id,
			DeploymentPersonnelId = subject.StartsWith("per") ? subject : null, DeploymentUnitId = subject.StartsWith("unit") ? subject : null, DeploymentEquipmentId = subject.StartsWith("eq") ? subject : null,
			EntryType = (int)DeploymentTimeEntryTypes.Deployment, StartTime = Day.AddHours(hourStart), EndTime = Day.AddHours(hourEnd), UnpaidBreakMinutes = 30
		};

		private static DeploymentTimeAccess CrewOf(params string[] subjects)
		{
			var access = new DeploymentTimeAccess { CrewUnitIds = subjects.Where(s => s.StartsWith("unit")).ToList() };
			foreach (var subject in subjects) access.WritableSubjectIds.Add(subject);
			return access;
		}

		[Test]
		public async Task A_crew_report_prefills_only_its_unit_crew_and_equipment()
		{
			var crew = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, "unit-1", null, "alice", null, null);

			crew.Scope.Should().Be(DeploymentTimeReportScopes.Crew);
			crew.DeploymentUnitId.Should().Be("unit-1");
			crew.Entries.Select(e => e.SubjectId).Should().BeEquivalentTo(new[] { "per-a", "per-b", "unit-1", "eq-1" });
			crew.Entries.Single(e => e.SubjectId == "unit-1").CrewSizeSnapshot.Should().Be(2);

			var other = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, "unit-2", null, "carol", null, null);
			other.Entries.Select(e => e.SubjectId).Should().BeEquivalentTo(new[] { "per-c", "unit-2" }, "each deployed unit files its own crew time report the same day");

			(await FluentActions.Awaiting(() => _service.CreateTimeReportAsync("dep-1", DeptId, Day, "unit-1", null, "bob", null, null)).Should().ThrowAsync<InvalidOperationException>())
				.Which.Message.Should().Be("timereports_date_exists");
		}

		[Test]
		public async Task A_single_resource_files_an_individual_report_and_a_crew_member_already_covered_cannot()
		{
			var dave = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, "per-d", "dave", null, null);
			dave.Scope.Should().Be(DeploymentTimeReportScopes.Individual);
			dave.Entries.Select(e => e.SubjectId).Should().Equal("per-d");

			await _service.CreateTimeReportAsync("dep-1", DeptId, Day, "unit-1", null, "alice", null, null);
			(await FluentActions.Awaiting(() => _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, "per-a", "alice", null, null)).Should().ThrowAsync<InvalidOperationException>())
				.Which.Message.Should().Be("timereports_subject_covered", "alice's time is already on Engine 1's crew report");

			var wide = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, null, "chief", null, null);
			wide.Entries.Select(e => e.SubjectId).Should().BeEquivalentTo(new[] { "per-c", "unit-2" }, "the deployment-wide report skips subjects already on a crew or individual report");
		}

		[Test]
		public async Task Scope_validation_refuses_foreign_subjects_and_double_billing()
		{
			var crew = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, "unit-1", null, "alice", null, null);
			var outside = await _service.SaveTimeEntriesAsync(crew.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry> { Entry("per-a", 6, 18), Entry("per-c", 6, 18) }, null, "chief", null, null);
			outside.Validation.Errors.Should().ContainSingle(e => e.Code == TimeReportValidation.SubjectOutsideScope && e.SubjectId == "per-c");

			var dave = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, "per-d", "dave", null, null);
			var wide = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, null, "chief", null, null);
			var twice = await _service.SaveTimeEntriesAsync(wide.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry> { Entry("per-c", 6, 18), Entry("per-d", 6, 18) }, null, "chief", null, null);
			twice.Validation.Errors.Should().ContainSingle(e => e.Code == TimeReportValidation.SubjectOnOtherReport && e.SubjectId == "per-d");
			_storedEntries.Where(e => e.DeploymentTimeReportId == dave.DeploymentTimeReportId).Should().ContainSingle();
		}

		[Test]
		public async Task A_scoped_writer_replaces_only_its_own_subjects_and_keeps_everyone_else()
		{
			var wide = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, null, null, "chief", null, null);
			var stored = _storedEntries.Where(e => e.DeploymentTimeReportId == wide.DeploymentTimeReportId).ToDictionary(e => e.SubjectId, e => e.DeploymentTimeEntryId);
			var carolBefore = _storedEntries.Single(e => e.SubjectId == "per-c");
			var carolStart = carolBefore.StartTime;

			// Engine 1's crew sends its own rows plus a tampered copy of Carol's; only its own land and nothing else is deleted.
			var result = await _service.SaveTimeEntriesAsync(wide.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry>
			{
				Entry("per-a", 5, 17, stored["per-a"]), Entry("unit-1", 5, 17, stored["unit-1"]), Entry("per-c", 1, 2, stored["per-c"])
			}, CrewOf("unit-1", "per-a", "per-b", "eq-1"), "alice", null, null);

			result.Validation.IsValid.Should().BeTrue();
			var after = _storedEntries.Where(e => e.DeploymentTimeReportId == wide.DeploymentTimeReportId).ToList();
			after.Single(e => e.SubjectId == "per-a").StartTime.Should().Be(Day.AddHours(5));
			after.Single(e => e.SubjectId == "per-c").StartTime.Should().Be(carolStart, "another crew's entry is kept exactly as stored");
			after.Should().Contain(e => e.SubjectId == "unit-2" && e.DeploymentTimeEntryId == stored["unit-2"]);
			after.Should().NotContain(e => e.SubjectId == "per-b" || e.SubjectId == "eq-1", "the crew removed its own rows it did not send");
			after.Should().Contain(e => e.SubjectId == "per-d", "subjects the writer may not touch are never deleted");
		}

		[Test]
		public void Acting_on_a_report_needs_its_scope()
		{
			var crew = CrewOf("unit-1", "per-a", "per-b", "eq-1");
			crew.PersonnelId = "per-a";
			crew.CanActOn(new DeploymentTimeReport { DeploymentUnitId = "unit-1" }).Should().BeTrue();
			crew.CanActOn(new DeploymentTimeReport { DeploymentUnitId = "unit-2" }).Should().BeFalse();
			crew.CanActOn(new DeploymentTimeReport { DeploymentPersonnelId = "per-b" }).Should().BeTrue("a crew boss may file for a member of the crew");
			crew.CanActOn(new DeploymentTimeReport { DeploymentPersonnelId = "per-d" }).Should().BeFalse();
			crew.CanActOn(new DeploymentTimeReport { Entries = { Entry("per-a", 6, 8), Entry("per-c", 6, 8) } }).Should().BeFalse("a deployment-wide report with another crew's rows is not theirs to submit");
			new DeploymentTimeAccess { CanManage = true }.CanActOn(new DeploymentTimeReport { DeploymentUnitId = "unit-2" }).Should().BeTrue();
		}
	}
}
