using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Providers.Neris;
using Resgrid.Services;
using Resgrid.Services.Records;
using Resgrid.Tests.Rms;
using Resgrid.Tests.Rms.Parity;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 3.16 and GAP-6: DeleteRecord and FinalizeRecords default to Everyone, so the grant alone must not
	/// let a member void, cancel or finalize somebody else's Record or incident report. The legacy Logs rule (an
	/// administrator or the member who logged it) is the floor, widened to the people the lifecycle names on the row: the
	/// owner, the reviewer and approver, the record's group administrator for void/cancel, and the ReviewRecords /
	/// AmendRecords / ApproveRecords holders who finalize out of review, amendment or approval. CorrectAndResubmit is a
	/// signature too, so it needs FinalizeRecords.
	/// </summary>
	[TestFixture]
	public class RecordsCommandLifecycleOwnershipTests
	{
		private const int Dept = RecordsParityHarness.Dept;
		private const string Author = RecordsParityHarness.Author;
		private const string Other = "other-member";

		private RecordsParityHarness _h;

		[SetUp]
		public void SetUp()
		{
			_h = new RecordsParityHarness();
			// A plain member: holds DeleteRecord / FinalizeRecords (the Everyone defaults) but none of the review grants.
			foreach (var grant in new[] { PermissionTypes.ReviewRecords, PermissionTypes.AmendRecords, PermissionTypes.ApproveRecords })
				_h.Authorization.Setup(a => a.HasPermissionAsync(Other, Dept, grant)).ReturnsAsync(false);
		}

		private static RecordDraftInput Training() => new RecordDraftInput
		{
			DefinitionKey = RmsDefinitionKeys.Training,
			StartedOn = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
			EndedOn = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc),
			Details = new RmsOperationalRecordDetail { Narrative = "Hose evolutions", Course = "Engine Ops", CourseCode = "ENG-101" }
		};

		private async Task<RecordAggregate> FinalizedAsync()
		{
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, Training());
			return await _h.Records.FinalizeAsync(Dept, Author, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);
		}

		#region Records (RecordsService)

		[Test]
		public async Task A_member_cannot_void_a_record_somebody_else_wrote()
		{
			var finalized = await FinalizedAsync();

			Func<Task> act = () => _h.Records.VoidAsync(Dept, Other, finalized.Record.RmsOperationalRecordId, "duplicate", "Filed twice");

			await act.Should().ThrowAsync<UnauthorizedAccessException>();
			(await _h.Records.GetAsync(Dept, finalized.Record.RmsOperationalRecordId)).Record.State.Should().Be((int)RmsRecordState.Finalized);
		}

		[Test]
		public async Task The_author_and_a_department_admin_can_void()
		{
			var mine = await FinalizedAsync();
			(await _h.Records.VoidAsync(Dept, Author, mine.Record.RmsOperationalRecordId, "duplicate", null)).Record.State.Should().Be((int)RmsRecordState.Voided);

			var theirs = await FinalizedAsync();
			_h.Authorization.Setup(a => a.IsDepartmentAdminAsync("chief", Dept)).ReturnsAsync(true);
			(await _h.Records.VoidAsync(Dept, "chief", theirs.Record.RmsOperationalRecordId, "duplicate", null)).Record.State.Should().Be((int)RmsRecordState.Voided);
		}

		[Test]
		public async Task A_member_cannot_cancel_somebody_elses_draft_but_the_author_can()
		{
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, Training());

			Func<Task> act = () => _h.Records.CancelAsync(Dept, Other, draft.Record.RmsOperationalRecordId);
			await act.Should().ThrowAsync<UnauthorizedAccessException>();

			(await _h.Records.CancelAsync(Dept, Author, draft.Record.RmsOperationalRecordId)).Record.State.Should().Be((int)RmsRecordState.Cancelled);
		}

		[Test]
		public async Task A_member_cannot_finalize_somebody_elses_draft()
		{
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, Training());

			Func<Task> act = () => _h.Records.FinalizeAsync(Dept, Other, draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);

			await act.Should().ThrowAsync<UnauthorizedAccessException>();
			(await _h.Records.GetAsync(Dept, draft.Record.RmsOperationalRecordId)).Record.State.Should().Be((int)RmsRecordState.Draft);
		}

		[Test]
		public async Task A_reviewer_still_finalizes_another_members_draft()
		{
			var draft = await _h.Records.CreateDraftAsync(Dept, Author, Training());
			_h.Authorization.Setup(a => a.HasPermissionAsync("lieutenant", Dept, PermissionTypes.ReviewRecords)).ReturnsAsync(true);

			var finalized = await _h.Records.FinalizeAsync(Dept, "lieutenant", draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);

			finalized.Record.State.Should().Be((int)RmsRecordState.Finalized);
		}

		#endregion

		#region The shared rule

		[Test]
		public async Task The_group_admin_of_the_records_group_may_void_or_cancel()
		{
			var authorization = new Mock<IRecordsAuthorizationService>();
			var groups = new Mock<IDepartmentGroupsService>();
			var scopes = new Mock<Resgrid.Model.Repositories.IRmsRecordGroupScopesRepository>();
			groups.Setup(g => g.GetGroupForUserAsync("captain", Dept)).ReturnsAsync(new DepartmentGroup
			{
				DepartmentGroupId = 11, DepartmentId = Dept, Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "captain", IsAdmin = true } }
			});
			scopes.Setup(s => s.GetForRecordAsync(Dept, "rec-2")).ReturnsAsync(new List<RmsRecordGroupScope> { new RmsRecordGroupScope { DepartmentId = Dept, RecordId = "rec-2", DepartmentGroupId = 11 } });

			(await RecordsLifecycleAuthority.CanVoidOrCancelAsync(authorization.Object, groups.Object, scopes.Object, "captain", Dept, "rec-1", 11, Author, Author, null, null))
				.Should().BeTrue("the record's station is the captain's group");
			(await RecordsLifecycleAuthority.CanVoidOrCancelAsync(authorization.Object, groups.Object, scopes.Object, "captain", Dept, "rec-2", 12, Author, Author, null, null))
				.Should().BeTrue("the record is scoped to the captain's group");
			(await RecordsLifecycleAuthority.CanVoidOrCancelAsync(authorization.Object, groups.Object, scopes.Object, "captain", Dept, "rec-3", 12, Author, Author, null, null))
				.Should().BeFalse("another group's record is outside the captain's group");
		}

		[Test]
		public async Task The_reviewer_and_approver_named_on_the_row_may_void()
		{
			var authorization = new Mock<IRecordsAuthorizationService>();

			(await RecordsLifecycleAuthority.CanVoidOrCancelAsync(authorization.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<Resgrid.Model.Repositories.IRmsRecordGroupScopesRepository>(),
				"reviewer", Dept, "rec-1", null, Author, Author, "reviewer", null)).Should().BeTrue();
			(await RecordsLifecycleAuthority.CanVoidOrCancelAsync(authorization.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<Resgrid.Model.Repositories.IRmsRecordGroupScopesRepository>(),
				"approver", Dept, "rec-1", null, Author, Author, null, "approver")).Should().BeTrue();
		}

		[Test]
		public async Task Amendment_and_approval_holders_finalize_only_where_that_grant_applies()
		{
			var authorization = new Mock<IRecordsAuthorizationService>();
			authorization.Setup(a => a.HasPermissionAsync("amender", Dept, PermissionTypes.AmendRecords)).ReturnsAsync(true);
			authorization.Setup(a => a.HasPermissionAsync("approver", Dept, PermissionTypes.ApproveRecords)).ReturnsAsync(true);

			(await RecordsLifecycleAuthority.CanFinalizeAsync(authorization.Object, "amender", Dept, RmsRecordState.Finalized, true, Author, Author, null, null)).Should().BeTrue();
			(await RecordsLifecycleAuthority.CanFinalizeAsync(authorization.Object, "amender", Dept, RmsRecordState.Draft, false, Author, Author, null, null)).Should().BeFalse();
			(await RecordsLifecycleAuthority.CanFinalizeAsync(authorization.Object, "approver", Dept, RmsRecordState.Approved, false, Author, Author, null, null)).Should().BeTrue();
			(await RecordsLifecycleAuthority.CanFinalizeAsync(authorization.Object, "approver", Dept, RmsRecordState.Draft, false, Author, Author, null, null)).Should().BeFalse();
		}

		#endregion

		#region Incident reports (IncidentReportsService)

		private const string ReportId = "report-1";

		private (IncidentReportsService Service, FakeIncidentStore Store, Mock<IRecordsAuthorizationService> Authorization) IncidentService(RmsRecordState state)
		{
			var store = new FakeIncidentStore();
			store.Reports.Add(new RmsIncidentReport
			{
				RmsIncidentReportId = ReportId, DepartmentId = Dept, CallId = 77, State = (int)state, LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, RowVersion = 3,
				AuthorUserId = Author, OwnerUserId = Author, DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, DefinitionVersion = 1, CurrentRevisionId = state == RmsRecordState.Draft ? null : "rev-1"
			});

			var authorization = new Mock<IRecordsAuthorizationService>();
			authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);

			var known = new Dictionary<Type, object>
			{
				[typeof(IDomainEventOutboxService)] = new DomainEventOutboxService(store.Shared.OutboxRepo.Object, Mock.Of<IEventAggregator>()),
				[typeof(IRecordsProtectionService)] = new PassthroughRecordsProtection(),
				[typeof(Resgrid.Model.Repositories.Queries.IUnitOfWork)] = store.UnitOfWork.Object,
				[typeof(IRecordsAuthorizationService)] = authorization.Object,
				[typeof(INerisMappingService)] = new NerisMappingService()
			};
			var mocks = typeof(FakeIncidentStore).GetProperties().Concat(typeof(FakeRmsStore).GetProperties())
				.Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(Mock<>))
				.Select(p => (Mock)p.GetValue(p.DeclaringType == typeof(FakeIncidentStore) ? (object)store : store.Shared))
				.ToList();

			var constructor = typeof(IncidentReportsService).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
			var arguments = constructor.GetParameters().Select(p =>
				known.TryGetValue(p.ParameterType, out var value) ? value :
				mocks.FirstOrDefault(m => p.ParameterType.IsInstanceOfType(m.Object))?.Object ??
				((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))).Object).ToArray();
			return ((IncidentReportsService)constructor.Invoke(arguments), store, authorization);
		}

		[Test]
		public async Task A_member_cannot_cancel_or_void_an_incident_report_somebody_else_wrote()
		{
			var draft = IncidentService(RmsRecordState.Draft);
			Func<Task> cancel = () => draft.Service.CancelAsync(Dept, Other, ReportId);
			await cancel.Should().ThrowAsync<UnauthorizedAccessException>();
			draft.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Draft);

			var finalized = IncidentService(RmsRecordState.Finalized);
			Func<Task> voiding = () => finalized.Service.VoidAsync(Dept, Other, ReportId, "duplicate", "Entered twice");
			await voiding.Should().ThrowAsync<UnauthorizedAccessException>();
			finalized.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Finalized);
		}

		[Test]
		public async Task The_author_and_a_department_admin_can_cancel_an_incident_report()
		{
			var mine = IncidentService(RmsRecordState.Draft);
			await mine.Service.CancelAsync(Dept, Author, ReportId);
			mine.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Cancelled);

			var theirs = IncidentService(RmsRecordState.Draft);
			theirs.Authorization.Setup(a => a.IsDepartmentAdminAsync("chief", Dept)).ReturnsAsync(true);
			await theirs.Service.CancelAsync(Dept, "chief", ReportId);
			theirs.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Cancelled);
		}

		[Test]
		public async Task A_member_cannot_finalize_somebody_elses_incident_report()
		{
			var draft = IncidentService(RmsRecordState.Draft);
			draft.Authorization.Setup(a => a.HasPermissionAsync(Other, Dept, PermissionTypes.FinalizeRecords)).ReturnsAsync(true);

			Func<Task> act = () => draft.Service.FinalizeAsync(Dept, Other, ReportId, 3, "1", null, null, null);

			await act.Should().ThrowAsync<UnauthorizedAccessException>();
			draft.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Draft);
		}

		[Test]
		public async Task Correct_and_resubmit_needs_FinalizeRecords_even_for_the_author()
		{
			var rejected = IncidentService(RmsRecordState.Rejected);

			Func<Task> act = () => rejected.Service.CorrectAndResubmitAsync(Dept, Author, ReportId, 3, "1", null, "destination-rejection", "Answered time added");

			(await act.Should().ThrowAsync<UnauthorizedAccessException>()).Which.Message.Should().Contain("FinalizeRecords");
			rejected.Store.Reports.Single().State.Should().Be((int)RmsRecordState.Rejected);
		}

		[Test]
		public void Correct_and_resubmit_carries_the_finalize_policy_on_both_surfaces()
		{
			Policies(typeof(Resgrid.Web.Areas.User.Controllers.IncidentReportsController), "CorrectAndResubmit").Should().Contain(ResgridResources.Record_Finalize);
			Policies(typeof(Resgrid.Web.Services.Controllers.v4.IncidentReportsController), "CorrectAndResubmit").Should().Contain(ResgridResources.Record_Finalize);
		}

		private static IEnumerable<string> Policies(Type controller, string action)
			=> controller.GetMethods().Where(m => m.Name == action).SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>()).Select(a => a.Policy);

		#endregion
	}
}
