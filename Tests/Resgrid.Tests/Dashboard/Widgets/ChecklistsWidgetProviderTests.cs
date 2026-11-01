using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Readiness;
using static Resgrid.Tests.Dashboard.Widgets.ReadinessRecordsWidgetTestSupport;

namespace Resgrid.Tests.Dashboard.Widgets
{
	/// <summary>checklists.due and checklists.compliance (plan §7.7, §9A.4 mixed-content sentinel).</summary>
	[TestFixture]
	public class ChecklistsWidgetProviderTests
	{
		private const string Sentinel = "Unlock protected data to use this checklist.";

		private static bool IsViewerActor(ChecklistActor actor, string grant = null) =>
			actor.DepartmentId == DepartmentId && actor.UserId == UserId && actor.GrantToken == grant;

		private static ChecklistOccurrenceView Occurrence(string id, int state, DateTime start, DateTime end, bool assigned = true, bool canStart = false, string name = "Engine check") =>
			new ChecklistOccurrenceView
			{
				Occurrence = new ChecklistOccurrence { Id = id, DepartmentId = DepartmentId, State = state, PeriodStartUtc = start, WindowEndUtc = end, TargetType = (int)ChecklistTargetType.Unit, TargetId = "7" },
				Name = name,
				Target = new ChecklistTarget { Type = ChecklistTargetType.Unit, Id = "7", Name = "Engine 1" },
				Assigned = assigned,
				CanStart = canStart
			};

		[Test]
		public async Task Due_lists_the_viewers_assigned_occurrences_most_urgent_first()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.DueAsync(It.Is<ChecklistActor>(a => IsViewerActor(a, Grant)), 0, true)).ReturnsAsync(new List<ChecklistOccurrenceView>
			{
				Occurrence("upcoming", (int)ChecklistOccurrenceState.Scheduled, Now.AddHours(5), Now.AddHours(6)),
				Occurrence("now", (int)ChecklistOccurrenceState.Scheduled, Now.AddHours(-1), Now.AddHours(1), canStart: true),
				Occurrence("missed", (int)ChecklistOccurrenceState.Missed, Now.AddDays(-1), Now.AddHours(-20)),
				Occurrence("someone-else", (int)ChecklistOccurrenceState.Scheduled, Now.AddHours(-1), Now.AddHours(1), assigned: false),
				Occurrence("redacted", (int)ChecklistOccurrenceState.Scheduled, Now.AddHours(8), Now.AddHours(9), name: ProtectedDataEnvelope.RedactionValue)
			});

			var data = (ChecklistsDueWidgetData)await new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.due", grant: Grant), CancellationToken.None);

			data.AssignedOnly.Should().BeTrue();
			data.Total.Should().Be(4, "the unassigned occurrence is not the viewer's");
			data.Overdue.Should().Be(1);
			data.DueNow.Should().Be(1);
			data.Upcoming.Should().Be(2);
			data.NextDueUtc.Should().Be(Now.AddHours(5));
			data.Items.Select(i => i.OccurrenceId).Should().ContainInOrder("missed", "now", "upcoming", "redacted");
			data.Items.First().Tone.Should().Be("danger");
			data.Items.Single(i => i.OccurrenceId == "now").CanStart.Should().BeTrue();
			data.Items.Single(i => i.OccurrenceId == "redacted").Name.Should().BeNull("a redaction placeholder is never passed on");
		}

		[Test]
		public async Task Due_can_show_every_visible_occurrence_and_honours_max_rows()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.DueAsync(It.IsAny<ChecklistActor>(), 0, true)).ReturnsAsync(Enumerable.Range(0, 51)
				.Select(i => Occurrence("o" + i, (int)ChecklistOccurrenceState.Scheduled, Now.AddHours(i + 1), Now.AddHours(i + 2), assigned: i % 2 == 0)).ToList());

			var context = Context("checklists.due", new Dictionary<string, object> { ["assignedOnly"] = false, ["maxRows"] = 3 });
			var data = (ChecklistsDueWidgetData)await new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(context, CancellationToken.None);

			data.HasMore.Should().BeTrue();
			data.Total.Should().Be(50, "counts cover the Due page's first page");
			data.Items.Should().HaveCount(3);
		}

		[Test]
		public async Task Due_maps_the_protected_sentinel_to_verify_to_view()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.DueAsync(It.IsAny<ChecklistActor>(), 0, true)).ThrowsAsync(new ChecklistException(403, Sentinel));

			var state = await StateAsync(() => new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.due"), CancellationToken.None));

			state.Code.Should().Be(DashboardWidgetErrorCodes.Protected);
			state.MessageKey.Should().Be("WidgetErrorProtected");
		}

		[Test]
		public async Task Due_maps_other_refusals_to_widget_states()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.DueAsync(It.IsAny<ChecklistActor>(), 0, true)).ThrowsAsync(new ChecklistException(403, "Department membership is required."));
			(await StateAsync(() => new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.due"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.Forbidden);

			checklists.Setup(c => c.DueAsync(It.IsAny<ChecklistActor>(), 0, true)).ThrowsAsync(new ChecklistException(404, "Checklists are disabled for this department."));
			(await StateAsync(() => new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.due"), CancellationToken.None))).Code.Should().Be(DashboardWidgetErrorCodes.NotFound);
		}

		[Test]
		public async Task Due_for_an_empty_department_is_empty()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.DueAsync(It.IsAny<ChecklistActor>(), 0, true)).ReturnsAsync(new List<ChecklistOccurrenceView>());

			var data = (ChecklistsDueWidgetData)await new ChecklistsDueWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.due"), CancellationToken.None);

			data.Total.Should().Be(0);
			data.Items.Should().BeEmpty();
			data.NextDueUtc.Should().BeNull();
		}

		[Test]
		public async Task Compliance_reads_the_compliance_window_and_lists_the_weakest_targets_first()
		{
			ChecklistReportQuery seen = null;
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.GetComplianceSummaryAsync(It.Is<ChecklistActor>(a => IsViewerActor(a, null)), It.IsAny<ChecklistReportQuery>()))
				.Callback<ChecklistActor, ChecklistReportQuery>((_, q) => seen = q)
				.ReturnsAsync(new ChecklistComplianceSummary
				{
					Groups = new List<ChecklistComplianceGroup>
					{
						new ChecklistComplianceGroup { Target = new ChecklistTarget { Type = ChecklistTargetType.Unit, Id = "1", Name = "Engine 1" }, Expected = 10, Completed = 10, OnTime = 9 },
						new ChecklistComplianceGroup { Target = new ChecklistTarget { Type = ChecklistTargetType.Unit, Id = "2", Name = "Ladder 2" }, Expected = 10, Completed = 5, OnTime = 5, Missed = 5 },
						new ChecklistComplianceGroup { Target = new ChecklistTarget { Type = ChecklistTargetType.Unit, Id = "3", Name = "Idle" }, Expected = 0 }
					},
					Entries = new List<ChecklistReportEntry>
					{
						// 03:30 UTC on 31 Oct is still 30 Oct in Eastern time: bucketed by the department's day, not UTC's.
						new ChecklistReportEntry { Expected = true, Missed = true, DueUtc = new DateTime(2026, 10, 31, 3, 30, 0, DateTimeKind.Utc) },
						new ChecklistReportEntry { Expected = true, DueUtc = new DateTime(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc) },
						new ChecklistReportEntry { Expected = false, DueUtc = new DateTime(2026, 10, 31, 14, 0, 0, DateTimeKind.Utc) }
					},
					UnavailableSources = new List<string> { "RestrictedChecklistResults" }
				});

			var context = Context("checklists.compliance", timeZone: "Eastern Standard Time");
			var data = (ChecklistsComplianceWidgetData)await new ChecklistsComplianceWidgetProvider(checklists.Object).GetDataAsync(context, CancellationToken.None);

			seen.Should().NotBeNull();
			seen.UntilUtc.Should().Be(new DateTime(2026, 11, 1, 4, 0, 0), "tomorrow's local midnight in EDT");
			(seen.UntilUtc - seen.FromUtc).TotalDays.Should().BeApproximately(31, 0.05);
			seen.FromUtc.Kind.Should().Be(DateTimeKind.Utc);

			data.Expected.Should().Be(20);
			data.Completed.Should().Be(15);
			data.CompletionRate.Should().Be(75m);
			data.PartiallyRestricted.Should().BeTrue();
			data.Targets.Select(t => t.Name).Should().Equal("Ladder 2", "Engine 1");
			data.Trend.Should().HaveCount(2);
			data.Trend[0].Day.Should().Be(new DateTime(2026, 10, 30));
			data.Trend[0].Missed.Should().Be(1);
			data.Trend[1].Day.Should().Be(new DateTime(2026, 10, 31));
		}

		[Test]
		public async Task Compliance_maps_the_protected_sentinel_to_verify_to_view()
		{
			var checklists = new Mock<IChecklistsService>(MockBehavior.Strict);
			checklists.Setup(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>())).ThrowsAsync(new ChecklistException(403, Sentinel));

			var state = await StateAsync(() => new ChecklistsComplianceWidgetProvider(checklists.Object).GetDataAsync(Context("checklists.compliance"), CancellationToken.None));

			state.Code.Should().Be(DashboardWidgetErrorCodes.Protected);
		}
	}
}
