using System;
using System.Data.SqlTypes;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// A client date outside SQL datetime (a two-digit year sent as 0026) failed the insert with SqlDateTime overflow and a 500
	/// (Sentry RESGRID-WEB-1MY). The services now refuse it with an ArgumentException, which the web and v4 map to a message / 400,
	/// before anything is written or a record number is drawn.
	/// </summary>
	[TestFixture]
	public class RecordsPreventionStorableDateTests
	{
		private static readonly DateTime TwoDigitYear = new DateTime(26, 9, 25, 19, 0, 0, DateTimeKind.Utc);
		private RmsPreventionHarness _h;

		[SetUp]
		public void SetUp() => _h = new RmsPreventionHarness();

		[Test]
		public void The_gate_accepts_the_sql_datetime_range_and_refuses_dates_outside_it()
		{
			RecordsPreventionGate.RequireStorableDate((DateTime)SqlDateTime.MinValue, "bad").Should().Be((DateTime)SqlDateTime.MinValue);
			RecordsPreventionGate.RequireStorableDate((DateTime)SqlDateTime.MaxValue, "bad").Should().Be((DateTime)SqlDateTime.MaxValue);
			RecordsPreventionGate.RequireStorableDate((DateTime?)null, "bad").Should().BeNull();

			foreach (var value in new[] { TwoDigitYear, ((DateTime)SqlDateTime.MinValue).AddTicks(-1), DateTime.MinValue, DateTime.MaxValue })
				FluentActions.Invoking(() => RecordsPreventionGate.RequireStorableDate(value, "bad")).Should().Throw<ArgumentException>().WithMessage("bad");
		}

		[Test]
		public async Task Investigation_note_and_evidence_refuse_the_date_without_writing_or_drawing_an_evidence_number()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, 42, "Origin unknown.");

			Func<Task> note = () => _h.InvestigationsService.AddNoteAsync(Dept, Admin, investigation.RmsInvestigationCaseId, RmsInvestigationNoteKind.Interview, TwoDigitYear, "Interview", "Body");
			await note.Should().ThrowAsync<ArgumentException>().WithMessage("The note date is not valid.");
			Func<Task> evidence = () => _h.InvestigationsService.AddEvidenceAsync(Dept, Admin, investigation.RmsInvestigationCaseId, new RmsInvestigationEvidence { Description = "Photo", CollectedOn = TwoDigitYear });
			await evidence.Should().ThrowAsync<ArgumentException>().WithMessage("The collection date is not valid.");
			_h.CaseNotes.Rows.Should().BeEmpty();
			_h.Evidence.Rows.Should().BeEmpty();
			_h.Custody.Rows.Should().BeEmpty();

			// Unset still means now, and the rejected call did not use up evidence number 1.
			var saved = await _h.InvestigationsService.AddNoteAsync(Dept, Admin, investigation.RmsInvestigationCaseId, RmsInvestigationNoteKind.Interview, default, "Interview", "Body");
			saved.OccurredOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
			var item = await _h.InvestigationsService.AddEvidenceAsync(Dept, Admin, investigation.RmsInvestigationCaseId, new RmsInvestigationEvidence { Description = "Photo" });
			item.EvidenceNumber.Should().EndWith("-0001");
		}

		[Test, NonParallelizable]
		public async Task The_v4_note_endpoint_answers_400_with_the_message_instead_of_a_500()
		{
			var investigation = await _h.InvestigationsService.OpenAsync(Dept, Admin, "Warehouse fire", null, 42, "Origin unknown.");
			var previous = ClaimsAuthorizationHelper._httpContextAccessor;
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, Admin), new Claim(ClaimTypes.PrimaryGroupSid, Dept.ToString()) }, "Test")) };
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			try
			{
				var controller = new RecordInvestigationsController(_h.InvestigationsService, _h.Cutover.Object) { ControllerContext = new ControllerContext { HttpContext = http } };

				var result = await controller.SaveNote(new CaseNoteInput { CaseId = investigation.RmsInvestigationCaseId, Kind = (int)RmsInvestigationNoteKind.Interview, OccurredOn = TwoDigitYear, Subject = "Interview", Body = "Body" }, default);

				var problem = result.Result.Should().BeOfType<ObjectResult>().Subject;
				problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
				problem.Value.Should().BeOfType<ProblemDetails>().Which.Title.Should().Be("The note date is not valid.");
				_h.CaseNotes.Rows.Should().BeEmpty();
			}
			finally { ClaimsAuthorizationHelper._httpContextAccessor = previous; }
		}

		[Test]
		public async Task Hydrant_flow_test_and_maintenance_refuse_the_date_and_leave_the_hydrant_unchanged()
		{
			var hydrant = await _h.HydrantsService.SaveAsync(Dept, Admin, new RmsHydrant { HydrantNumber = "H-101", Latitude = 45.5m, Longitude = -122.6m, Type = (int)RmsHydrantType.DryBarrel, MainSizeInches = 8 });

			Func<Task> test = () => _h.HydrantsService.RecordFlowTestAsync(Dept, Admin, new RmsHydrantFlowTest { RmsHydrantId = hydrant.RmsHydrantId, TestedOn = TwoDigitYear, PitotPressurePsi = 64, OutletDiameterInches = 2.5m });
			await test.Should().ThrowAsync<ArgumentException>().WithMessage("The test date is not valid.");
			Func<Task> maintenance = () => _h.HydrantsService.RecordMaintenanceAsync(Dept, Admin, new RmsHydrantMaintenance { RmsHydrantId = hydrant.RmsHydrantId, PerformedOn = TwoDigitYear });
			await maintenance.Should().ThrowAsync<ArgumentException>().WithMessage("The maintenance date is not valid.");

			_h.FlowTests.Rows.Should().BeEmpty();
			_h.Maintenance.Rows.Should().BeEmpty();
			hydrant.LastTestedOn.Should().BeNull();
			hydrant.LastMaintainedOn.Should().BeNull();
		}

		[Test]
		public async Task Inspection_schedule_violation_and_list_refuse_the_date_without_drawing_an_inspection_number()
		{
			var occupancy = _h.SeedOccupancy();

			Func<Task> schedule = () => _h.InspectionsService.ScheduleAsync(Dept, Admin, occupancy.RmsOccupancyId, null, TwoDigitYear, Admin);
			await schedule.Should().ThrowAsync<ArgumentException>().WithMessage("The scheduled date is not valid.");
			_h.Inspections.Rows.Should().BeEmpty();
			Func<Task> list = () => _h.InspectionsService.ListAsync(Dept, Admin, new RmsInspectionQuery { ScheduledBefore = TwoDigitYear });
			await list.Should().ThrowAsync<ArgumentException>().WithMessage("The scheduled-before date is not valid.");
			Func<Task> count = () => _h.InspectionsService.CountAsync(Dept, Admin, new RmsInspectionQuery { ScheduledBefore = TwoDigitYear });
			await count.Should().ThrowAsync<ArgumentException>().WithMessage("The scheduled-before date is not valid.");

			var inspection = await _h.InspectionsService.ScheduleAsync(Dept, Admin, occupancy.RmsOccupancyId, null, DateTime.UtcNow, Admin);
			inspection.InspectionNumber.Should().EndWith("-0001");
			Func<Task> violation = () => _h.InspectionsService.SaveViolationAsync(Dept, Admin, new RmsViolation { RmsInspectionId = inspection.RmsInspectionId, Description = "Blocked exit", DueOn = TwoDigitYear });
			await violation.Should().ThrowAsync<ArgumentException>().WithMessage("The due date is not valid.");
			_h.Violations.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Permit_update_issue_and_list_refuse_the_date()
		{
			var type = await _h.PermitsService.SaveTypeAsync(Dept, Admin, new RmsPermitType { Name = "Hot work", Code = "HW", DefaultValidityDays = 30, IsActive = true });
			var permit = await _h.PermitsService.ApplyAsync(Dept, Admin, new RmsPermit { RmsPermitTypeId = type.RmsPermitTypeId, ApplicantName = "Welder" });

			Func<Task> update = () => _h.PermitsService.UpdateAsync(Dept, Admin, new RmsPermit { RmsPermitId = permit.RmsPermitId, ApplicantName = "Welder", ExpiresOn = TwoDigitYear });
			await update.Should().ThrowAsync<ArgumentException>().WithMessage("The expiry date is not valid.");
			await _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Approved, null, null, null);
			Func<Task> issue = () => _h.PermitsService.TransitionAsync(Dept, Admin, permit.RmsPermitId, RmsPermitState.Issued, null, TwoDigitYear, null);
			await issue.Should().ThrowAsync<ArgumentException>().WithMessage("The effective date is not valid.");
			Func<Task> list = () => _h.PermitsService.ListAsync(Dept, Admin, new RmsPermitQuery { ExpiresBefore = TwoDigitYear });
			await list.Should().ThrowAsync<ArgumentException>().WithMessage("The expires-before date is not valid.");

			var stored = await _h.PermitsService.GetAsync(Dept, Admin, permit.RmsPermitId);
			stored.Permit.State.Should().Be((int)RmsPermitState.Approved, "the refused issue did not move the permit");
		}

		[Test]
		public async Task Crr_activity_and_its_window_refuse_the_date()
		{
			Func<Task> save = () => _h.CrrService.SaveAsync(Dept, Admin, new RmsCrrActivity { Title = "School visit", OccurredOn = TwoDigitYear });
			await save.Should().ThrowAsync<ArgumentException>().WithMessage("The activity date is not valid.");
			_h.Crr.Rows.Should().BeEmpty();

			Func<Task> list = () => _h.CrrService.ListAsync(Dept, Admin, TwoDigitYear, DateTime.UtcNow, 10);
			await list.Should().ThrowAsync<ArgumentException>().WithMessage("The start date is not valid.");
			Func<Task> summary = () => _h.CrrService.GetSummaryAsync(Dept, Admin, DateTime.UtcNow.AddDays(-30), DateTime.MaxValue);
			await summary.Should().ThrowAsync<ArgumentException>().WithMessage("The end date is not valid.");
		}

		[Test]
		public async Task Occupancy_next_review_date_is_refused_before_an_occupancy_number_is_drawn()
		{
			Func<Task> save = () => _h.OccupancyService.SaveAsync(Dept, Admin, new RmsOccupancy { Name = "Riverside Mill", NextReviewDue = TwoDigitYear });
			await save.Should().ThrowAsync<ArgumentException>().WithMessage("The next review date is not valid.");
			_h.Occupancies.Rows.Should().BeEmpty();
			_h.Sequences.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task Quality_sampling_and_trends_refuse_the_date_before_looking_up_the_rubric()
		{
			Func<Task> sample = () => _h.QualityService.SampleAsync(Dept, Admin, "no-such-rubric", TwoDigitYear);
			await sample.Should().ThrowAsync<ArgumentException>().WithMessage("The sample start date is not valid.");
			Func<Task> trends = () => _h.QualityService.GetTrendsAsync(Dept, Admin, TwoDigitYear);
			await trends.Should().ThrowAsync<ArgumentException>().WithMessage("The start date is not valid.");
		}
	}
}
