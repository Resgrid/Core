using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.IncidentBackOffice;
using Resgrid.Services.IncidentLogistics;
using static Resgrid.Tests.Dashboard.Widgets.AgentDWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>Incident Back Office widgets (plan §7.10): the desk base's gate, actor and activation rules; each desk block to its claim.</summary>
	[TestFixture]
	public class IncidentBackOfficeWidgetProviderTests
	{
		private static IncidentBackOfficeAccess Full(bool business = true, bool coordination = true) =>
			new IncidentBackOfficeAccess { Enabled = true, BusinessEnabled = business, CoordinationEnabled = coordination, Licensed = true, Level = IncidentBackOfficeAccessLevels.Full };

		private static IncidentBackOfficeAccess Lapsed() =>
			new IncidentBackOfficeAccess { Enabled = true, BusinessEnabled = true, CoordinationEnabled = true, Level = IncidentBackOfficeAccessLevels.ReadOnly };

		private static Mock<IIncidentBackOfficeAccessService> Access(IncidentBackOfficeAccess access)
		{
			var mock = new Mock<IIncidentBackOfficeAccessService>(MockBehavior.Strict);
			mock.Setup(a => a.GetAccessAsync(Dept)).ReturnsAsync(access);
			mock.Setup(a => a.IsEnabledAsync(Dept)).ReturnsAsync(access.Enabled);
			return mock;
		}

		private static IncidentActivation Activation(string id, DateTime opened, int department = Dept, int kind = (int)IncidentActivationKinds.Incident,
			int status = (int)IncidentActivationStatuses.Active) =>
			new IncidentActivation { IncidentActivationId = id, DepartmentId = department, Name = "Incident " + id, ShortCode = id.ToUpperInvariant(), Kind = kind, Status = status, OpenedOn = opened, Currency = "USD" };

		private static Mock<IIncidentActivationService> Activations(params IncidentActivation[] open)
		{
			var mock = new Mock<IIncidentActivationService>();
			mock.Setup(a => a.GetActivationsAsync(It.Is<IncidentActor>(x => x.DepartmentId == Dept), false, It.IsAny<CancellationToken>())).ReturnsAsync(open.ToList());
			return mock;
		}

		private static Mock<IIncidentLedgerService<IncidentActivation>> ActivationLedger(params IncidentActivation[] rows)
		{
			var mock = new Mock<IIncidentLedgerService<IncidentActivation>>();
			mock.Setup(l => l.GetAsync(It.IsAny<IncidentActor>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
				.ReturnsAsync((IncidentActor actor, string id, bool reveal, CancellationToken ct) => rows.FirstOrDefault(r => r.IncidentActivationId == id && r.DepartmentId == actor.DepartmentId));
			return mock;
		}

		#region Gates and access

		[Test]
		public void Gates_need_the_module_and_the_desk_read_claim()
		{
			var module = new[] { DashboardAccessChecks.IncidentBackOfficeEnabled };
			Visible("ibo.activations", Viewer(access: module)).Should().BeFalse("IncidentBackOffice_View is the desks' read claim");
			Visible("ibo.activations", Viewer(policies: new[] { "IncidentBackOffice_View" }, access: module)).Should().BeTrue();
			Visible("ibo.checkIn", Viewer(policies: new[] { "IncidentBackOffice_View" }, access: module)).Should().BeFalse("the enrollment queue needs IncidentParticipant_View");
			Visible("ibo.finance", Viewer(policies: new[] { "IncidentBackOffice_View", "IncidentCost_View" }, access: module)).Should().BeFalse("finance needs the Business capability");
			Visible("ibo.coordination", Viewer(policies: new[] { "CoordinationBoard_View" }, access: module.Append(DashboardAccessChecks.IncidentCoordinationEnabled))).Should().BeTrue();
		}

		[Test]
		public async Task Access_level_none_is_refused_like_the_desk_base_redirects_it()
		{
			var none = new IncidentBackOfficeAccess { Enabled = true, Level = IncidentBackOfficeAccessLevels.None };
			var provider = new IncidentActivationsWidgetProvider(Access(none).Object, Activations().Object, null, null, null);

			var state = await StateAsync(() => provider.GetDataAsync(Context("ibo.activations"), CancellationToken.None));

			state.Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		[Test]
		public async Task Coordination_is_refused_when_the_coordination_capability_is_off()
		{
			var provider = new IncidentCoordinationWidgetProvider(Access(Full(coordination: false)).Object, new Mock<IIncidentOperationalIntelligenceService>(MockBehavior.Strict).Object);
			(await StateAsync(() => provider.GetDataAsync(Context("ibo.coordination"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);
		}

		#endregion

		#region ibo.activations

		[Test]
		public async Task Activations_show_the_current_period_and_the_viewers_position_and_render_read_only_when_lapsed()
		{
			var a = Activation("a", Now.AddDays(-2));
			var b = Activation("b", Now.AddDays(-1));
			var periods = new Mock<IIncidentLedgerService<IncidentOperationalPeriod>>();
			periods.Setup(p => p.ListAsync(It.IsAny<IncidentActor>(), It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentOperationalPeriod>
			{
				new IncidentOperationalPeriod { IncidentActivationId = "a", Number = 1, StartOn = Now.AddDays(-2), EndOn = Now.AddDays(-1), Status = (int)IncidentPeriodStatuses.Closed },
				new IncidentOperationalPeriod { IncidentActivationId = "a", Number = 2, StartOn = Now.AddHours(-2), EndOn = Now.AddHours(10), Status = (int)IncidentPeriodStatuses.Planned }
			});
			var assignments = new Mock<IIncidentLedgerService<IncidentPositionAssignment>>();
			assignments.Setup(p => p.ListAsync(It.IsAny<IncidentActor>(), It.Is<IncidentLedgerQuery>(q => (string)q.Equal["UserId"] == User), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<IncidentPositionAssignment>
				{
					new IncidentPositionAssignment { IncidentActivationId = "a", IncidentPositionDefinitionId = "psc", UserId = User, StartOn = Now.AddHours(-1) },
					new IncidentPositionAssignment { IncidentActivationId = "b", IncidentPositionDefinitionId = "ended", UserId = User, StartOn = Now.AddDays(-1), EndOn = Now.AddHours(-1) }
				});
			var positions = new Mock<IIncidentLedgerService<IncidentPositionDefinition>>();
			positions.Setup(p => p.ListAsync(It.IsAny<IncidentActor>(), It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<IncidentPositionDefinition> { new IncidentPositionDefinition { IncidentPositionDefinitionId = "psc", Code = "PSC", Name = "Planning Section Chief" } });

			var data = (IncidentActivationsWidgetData)await new IncidentActivationsWidgetProvider(Access(Lapsed()).Object, Activations(a, b, Activation("x", Now, department: OtherDept)).Object,
				periods.Object, assignments.Object, positions.Object).GetDataAsync(Context("ibo.activations"), CancellationToken.None);

			data.ReadOnly.Should().BeTrue();
			data.OpenCount.Should().Be(2, "another department's activation never shows");
			data.MineCount.Should().Be(1);
			data.Rows.First().ActivationId.Should().Be("a", "incidents where I hold a position come first");
			data.Rows.First().PeriodNumber.Should().Be(2, "no period is marked Current, so the one spanning now is");
			data.Rows.First().MyPositionCode.Should().Be("PSC");
			data.Rows.Last().MyPositionCode.Should().BeNull("an ended assignment is not held");
		}

		#endregion

		#region ibo.desk

		private static IncidentDeskWidgetProvider Desk(IncidentBackOfficeAccess access, Mock<IIncidentActivationService> activations, Mock<IIncidentLedgerService<IncidentActivation>> ledger,
			Mock<IIncidentOperationalIntelligenceService> intelligence = null, Mock<IPermissionsService> permissions = null,
			Mock<IIncidentLedgerService<IncidentCostProjection>> projections = null) =>
			new IncidentDeskWidgetProvider(Access(access).Object, activations.Object, ledger.Object, (intelligence ?? new Mock<IIncidentOperationalIntelligenceService>()).Object,
				(permissions ?? Permissions()).Object, new Mock<IIncidentLedgerService<IncidentBudgetLine>>().Object, new Mock<IIncidentLedgerService<IncidentCommitment>>().Object,
				(projections ?? new Mock<IIncidentLedgerService<IncidentCostProjection>>()).Object, new Mock<IIncidentLedgerService<IncidentTimeRecord>>().Object,
				new Mock<IIncidentLedgerService<VendorInvoiceReference>>().Object);

		[Test]
		public async Task The_desk_defaults_to_the_most_recent_activation_and_shows_only_its_desks_values()
		{
			var older = Activation("older", Now.AddDays(-3));
			var newer = Activation("newer", Now.AddHours(-3));
			var summary = new IncidentDeskSummary { AsOf = Now };
			summary.Values["participants.total"] = 12;
			summary.Values["lodging.needed"] = 4;
			summary.Inbound.Add(new IncidentArrivalBucket { Window = "2h", Count = 3, Personnel = 9 });
			var intelligence = new Mock<IIncidentOperationalIntelligenceService>(MockBehavior.Strict);
			intelligence.Setup(i => i.GetDeskSummaryAsync(It.Is<IncidentActor>(x => x.DepartmentId == Dept && x.UserId == User), "newer", It.IsAny<CancellationToken>())).ReturnsAsync(summary);

			var data = (IncidentDeskWidgetData)await Desk(Full(), Activations(older, newer), ActivationLedger(), intelligence)
				.GetDataAsync(Context("ibo.desk"), CancellationToken.None);

			data.ActivationId.Should().Be("newer");
			data.Desk.Should().Be("operations");
			data.Values.Keys.Should().Equal("participants.total");
			data.Inbound.Should().ContainSingle().Which.Personnel.Should().Be(9);
			data.Outbound.Should().BeEmpty();
		}

		[Test]
		public async Task A_stored_activation_id_is_reloaded_for_the_department_and_never_trusted()
		{
			var foreign = Activation("foreign", Now, department: OtherDept);
			var data = (IncidentDeskWidgetData)await Desk(Full(), Activations(), ActivationLedger(foreign), new Mock<IIncidentOperationalIntelligenceService>(MockBehavior.Strict))
				.GetDataAsync(Context("ibo.desk", settings: new Dictionary<string, object> { ["activation"] = "foreign" }), CancellationToken.None);

			data.HasActivation.Should().BeFalse();
			data.Values.Should().BeEmpty();
		}

		[Test]
		public async Task The_finance_desk_needs_the_business_capability_and_a_finance_claim()
		{
			var a = Activation("a", Now);
			var settings = new Dictionary<string, object> { ["desk"] = "finance" };

			((IncidentDeskWidgetData)await Desk(Full(business: false), Activations(a), ActivationLedger()).GetDataAsync(Context("ibo.desk", settings: settings), CancellationToken.None))
				.DeskUnavailable.Should().BeTrue("the finance desk is a Business area desk");

			((IncidentDeskWidgetData)await Desk(Full(), Activations(a), ActivationLedger()).GetDataAsync(Context("ibo.desk", settings: settings), CancellationToken.None))
				.DeskUnavailable.Should().BeTrue("every finance claim defaults to department admins");

			var projections = new Mock<IIncidentLedgerService<IncidentCostProjection>>();
			projections.Setup(p => p.ListAsync(It.IsAny<IncidentActor>(), It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<IncidentCostProjection> { new IncidentCostProjection { CumulativeToDate = 5000m, DailyBurn = 800m, AddedOn = Now } });
			var costOnly = Permissions(Row(PermissionTypes.ViewIncidentCost, PermissionActions.Everyone));
			var data = (IncidentDeskWidgetData)await Desk(Full(), Activations(a), ActivationLedger(), permissions: costOnly, projections: projections)
				.GetDataAsync(Context("ibo.desk", settings: settings), CancellationToken.None);

			data.DeskUnavailable.Should().BeFalse();
			data.Values.Should().ContainKey("finance.cost_to_date").WhoseValue.Should().Be(5000m);
			data.Values.Keys.Should().NotContain(new[] { "finance.committed", "finance.budget", "finance.time_awaiting_approval", "finance.invoice_exceptions" },
				"each finance block shows only to its own claim");
		}

		#endregion

		[Test]
		public async Task Resources_group_by_kind_and_support_status()
		{
			var resources = new Mock<IIncidentLedgerService<IncidentResource>>();
			resources.Setup(r => r.ListAsync(It.IsAny<IncidentActor>(), It.Is<IncidentLedgerQuery>(q => q.ActivationIds.Single() == "a"), It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentResource>
			{
				new IncidentResource { DepartmentId = Dept, Kind = (int)IncidentResourceKinds.Crew, SupportStatus = (int)IncidentSupportStatuses.Arrived, PersonnelCount = 4 },
				new IncidentResource { DepartmentId = Dept, Kind = (int)IncidentResourceKinds.Crew, SupportStatus = (int)IncidentSupportStatuses.EnRoute, PersonnelCount = 5 },
				new IncidentResource { DepartmentId = Dept, Kind = (int)IncidentResourceKinds.Equipment, SupportStatus = (int)IncidentSupportStatuses.Supported }
			});

			var data = (IncidentResourcesWidgetData)await new IncidentResourcesWidgetProvider(Access(Full()).Object, Activations(Activation("a", Now)).Object, ActivationLedger().Object, resources.Object)
				.GetDataAsync(Context("ibo.resources"), CancellationToken.None);

			data.Total.Should().Be(3);
			data.OnIncident.Should().Be(2);
			data.Inbound.Should().Be(1);
			data.PersonnelOnIncident.Should().Be(4);
			data.ByKind.First().Should().BeEquivalentTo(new IncidentKeyCount { Key = "Crew", Count = 2 });
			data.ByStatus.Select(s => s.Key).Should().Equal("EnRoute", "Arrived", "Supported");
		}

		[Test]
		public async Task Check_in_lists_the_longest_waiting_candidates_and_counts_unchecked_participants()
		{
			var participants = new Mock<IIncidentParticipantsService>(MockBehavior.Strict);
			participants.Setup(p => p.GetEnrollmentQueueAsync(It.IsAny<IncidentActor>(), "a", It.IsAny<CancellationToken>())).ReturnsAsync(new List<IncidentEnrollmentCandidate>
			{
				new IncidentEnrollmentCandidate { Name = "Later", SeenSince = Now.AddMinutes(-5) },
				new IncidentEnrollmentCandidate { Name = "Earlier", SeenSince = Now.AddHours(-1) }
			});
			var ledger = new Mock<IIncidentLedgerService<IncidentParticipant>>();
			ledger.Setup(l => l.CountAsync(It.IsAny<IncidentActor>(), It.Is<IncidentLedgerQuery>(q => false.Equals(q.Equal["IsDraft"]) && q.IsNull.Contains("CheckedInOn")), It.IsAny<CancellationToken>())).ReturnsAsync(6);
			ledger.Setup(l => l.CountAsync(It.IsAny<IncidentActor>(), It.Is<IncidentLedgerQuery>(q => true.Equals(q.Equal["IsDraft"])), It.IsAny<CancellationToken>())).ReturnsAsync(2);

			var data = (IncidentCheckInWidgetData)await new IncidentCheckInWidgetProvider(Access(Full()).Object, Activations(Activation("a", Now)).Object, ActivationLedger().Object,
				participants.Object, ledger.Object).GetDataAsync(Context("ibo.checkIn"), CancellationToken.None);

			data.QueueCount.Should().Be(2);
			data.Candidates.Select(c => c.Name).Should().Equal("Earlier", "Later");
			data.AwaitingCheckIn.Should().Be(6);
			data.Drafts.Should().Be(2);
		}

		[Test]
		public async Task Finance_shows_the_budget_only_to_the_budget_claim()
		{
			var projections = new Mock<IIncidentLedgerService<IncidentCostProjection>>();
			projections.Setup(p => p.ListAsync(It.IsAny<IncidentActor>(), It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<IncidentCostProjection> { new IncidentCostProjection { CumulativeToDate = 2500m, DailyBurn = 500m, ForecastAtCompletion = 9000m, Currency = "CAD", AddedOn = Now } });
			var lines = new Mock<IIncidentLedgerService<IncidentBudgetLine>>(MockBehavior.Strict);

			var hidden = (IncidentFinanceWidgetData)await new IncidentFinanceWidgetProvider(Access(Full()).Object, Activations(Activation("a", Now)).Object, ActivationLedger().Object,
				Permissions().Object, projections.Object, lines.Object).GetDataAsync(Context("ibo.finance"), CancellationToken.None);
			hidden.CostToDate.Should().Be(2500m);
			hidden.Currency.Should().Be("CAD");
			hidden.CanViewBudget.Should().BeFalse();
			hidden.Budget.Should().BeNull();

			lines.Setup(l => l.SumAsync(It.IsAny<IncidentActor>(), "RevisedAmount", It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(10000m);
			var shown = (IncidentFinanceWidgetData)await new IncidentFinanceWidgetProvider(Access(Full()).Object, Activations(Activation("a", Now)).Object, ActivationLedger().Object,
				Permissions(Row(PermissionTypes.AuthorizeIncidentFunding, PermissionActions.DepartmentAndGroupAdmins)).Object, projections.Object, lines.Object)
				.GetDataAsync(Context("ibo.finance", Viewer(groupAdmin: true)), CancellationToken.None);
			shown.Budget.Should().Be(10000m);
			shown.BudgetUsedPercent.Should().Be(25);
		}

		[Test]
		public async Task Coordination_maps_the_summary_and_renders_read_only_when_lapsed()
		{
			var intelligence = new Mock<IIncidentOperationalIntelligenceService>(MockBehavior.Strict);
			intelligence.Setup(i => i.GetCoordinationSummaryAsync(It.Is<IncidentActor>(a => a.DepartmentId == Dept), 5, It.IsAny<CancellationToken>())).ReturnsAsync(new IncidentCoordinationSummary
			{
				Boards = 2, ActiveShares = 1, StaleLanes = 3,
				Recent = new List<IncidentCoordinationBoardRow> { new IncidentCoordinationBoardRow { CoordinationBoardId = "b1", Name = "County EOC", StaleLanes = 3, LastUpdateOn = Now } }
			});

			var data = (IncidentCoordinationWidgetData)await new IncidentCoordinationWidgetProvider(Access(Lapsed()).Object, intelligence.Object)
				.GetDataAsync(Context("ibo.coordination"), CancellationToken.None);

			data.ReadOnly.Should().BeTrue();
			data.Boards.Should().Be(2);
			data.Rows.Should().ContainSingle().Which.Name.Should().Be("County EOC");
		}

		[Test]
		public async Task The_coordination_summary_counts_lanes_and_live_shares_per_board()
		{
			var boards = new Mock<IIncidentLedgerRepository<CoordinationBoard>>(MockBehavior.Strict);
			boards.Setup(b => b.ListAsync(Dept, It.Is<IncidentLedgerQuery>(q => false.Equals(q.Equal["IsArchived"])), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CoordinationBoard>
			{
				new CoordinationBoard { CoordinationBoardId = "b1", DepartmentId = Dept, Name = "Old", AddedOn = Now.AddDays(-5) },
				new CoordinationBoard { CoordinationBoardId = "b2", DepartmentId = Dept, Name = "Busy", AddedOn = Now.AddDays(-9) }
			});
			var lanes = new Mock<IIncidentLedgerRepository<CoordinationBoardLane>>(MockBehavior.Strict);
			lanes.Setup(l => l.ListAsync(Dept, It.IsAny<IncidentLedgerQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CoordinationBoardLane>
			{
				new CoordinationBoardLane { CoordinationBoardId = "b2", LastSourceOn = Now.AddMinutes(-3) },
				new CoordinationBoardLane { CoordinationBoardId = "b2", StaleSince = Now.AddHours(-1) }
			});
			var shares = new Mock<IIncidentLedgerRepository<CoordinationShareGrant>>(MockBehavior.Strict);
			shares.Setup(s => s.ListAsync(Dept, It.Is<IncidentLedgerQuery>(q => q.IsNull.Contains("RevokedOn") && q.RangeColumn == "ExpiresOn"), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<CoordinationShareGrant> { new CoordinationShareGrant { CoordinationBoardId = "b1" } });
			var service = new IncidentOperationalIntelligenceService(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
				boards.Object, lanes.Object, shares.Object);

			var summary = await service.GetCoordinationSummaryAsync(new IncidentActor { DepartmentId = Dept }, 5);

			summary.Boards.Should().Be(2);
			summary.StaleLanes.Should().Be(1);
			summary.ActiveShares.Should().Be(1);
			summary.Recent.Select(r => r.CoordinationBoardId).Should().Equal("b2", "b1");
			summary.Recent[0].LastUpdateOn.Should().Be(Now.AddMinutes(-3), "a lane's source update counts as board activity");
		}

		[Test]
		public async Task The_activation_option_source_leads_with_most_recent_then_open_activations_and_nothing_when_the_module_is_off()
		{
			var strings = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Home.DashboardWidgets>>();
			strings.Setup(s => s[IncidentActivationsOptionSource.MostRecentKey]).Returns(new Microsoft.Extensions.Localization.LocalizedString(IncidentActivationsOptionSource.MostRecentKey, "Most recent"));
			var source = new IncidentActivationsOptionSource(Access(Full()).Object, Activations(Activation("a", Now.AddDays(-1)), Activation("b", Now)).Object, strings.Object);
			(await source.GetOptionsAsync(Dept, User)).Should().Equal(new KeyValuePair<string, string>("recent", "Most recent"),
				new KeyValuePair<string, string>("b", "Incident b (B)"), new KeyValuePair<string, string>("a", "Incident a (A)"));

			var off = new IncidentActivationsOptionSource(Access(new IncidentBackOfficeAccess { Enabled = false }).Object, new Mock<IIncidentActivationService>(MockBehavior.Strict).Object, strings.Object);
			(await off.GetOptionsAsync(Dept, User)).Should().BeEmpty();
		}
	}
}
