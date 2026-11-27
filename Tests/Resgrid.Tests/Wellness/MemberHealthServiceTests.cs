using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Wellness;
using Resgrid.Workers.Framework.Logic;
using static Resgrid.Tests.Wellness.MemberHealthTestWorld;

namespace Resgrid.Tests.Wellness
{
	/// <summary>BBP / infectious-disease exposure cases: report, the designated-officer workflow, follow-ups and the restricted tier.</summary>
	[TestFixture]
	public class ExposureCaseServiceTests
	{
		private static readonly DateTime Yesterday = DateTime.UtcNow.AddDays(-1);

		[Test]
		public async Task A_member_reports_an_exposure_which_logs_a_bloodborne_entry_and_tells_only_the_designated_officers()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();

			var incident = await service.ReportExposureAsync(DepartmentId, Member, new ExposureIncidentReport
			{
				Route = (int)ExposureIncidentRoutes.Percutaneous,
				OccurredOn = Yesterday,
				Narrative = "Needlestick while capping",
				PpeWorn = "Gloves"
			});

			incident.Stage.Should().Be((int)ExposureIncidentStages.Reported);
			incident.UserId.Should().Be(Member);
			var entry = world.Exposures.Single();
			entry.Category.Should().Be((int)ResponderExposureCategories.BloodbornePathogen);
			entry.Status.Should().Be((int)ResponderExposureStatuses.Confirmed);
			incident.ResponderExposureId.Should().Be(entry.RmsResponderExposureId);

			world.Sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { Designated }, "the designated officer role is told; the admin and the wellness officer are not");
			world.Sent.Single().EventCode.Should().Be("EI:" + incident.RmsExposureIncidentId);
			world.Sent.Single().Message.Should().NotContain("Needlestick").And.NotContain("Gloves", "a notice names no health detail");
			world.Audits.Select(a => a.Type).Should().Contain(new[] { AuditLogTypes.ResponderExposureRecorded, AuditLogTypes.ExposureIncidentReported });
			world.Audits.Should().NotContain(a => (a.After ?? string.Empty).Contains("Needlestick"));
		}

		[Test]
		public async Task An_airborne_disease_is_logged_as_an_other_exposure_and_the_wellness_role_serves_when_no_designated_role_is_named()
		{
			var world = new MemberHealthTestWorld();
			world.Settings.DesignatedOfficerRoleId = 0;
			var service = world.Service();

			await service.ReportExposureAsync(DepartmentId, Member, new ExposureIncidentReport { Route = (int)ExposureIncidentRoutes.Airborne, OccurredOn = Yesterday });

			world.Exposures.Single().Category.Should().Be((int)ResponderExposureCategories.Other);
			world.Sent.Select(s => s.UserId).Should().BeEquivalentTo(new[] { WellnessOfficer });
			(await service.IsDesignatedOfficerAsync(DepartmentId, WellnessOfficer)).Should().BeTrue();
			(await service.IsDesignatedOfficerAsync(DepartmentId, Designated)).Should().BeFalse();
		}

		[Test]
		public async Task A_case_follows_only_a_confirmed_bloodborne_entry_of_the_member_and_only_once()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var carcinogen = world.AddExposure(Member, ResponderExposureCategories.Carcinogen, ResponderExposureStatuses.Confirmed, Yesterday);
			var suggested = world.AddExposure(Member, ResponderExposureCategories.BloodbornePathogen, ResponderExposureStatuses.Suggested, Yesterday);
			var bbp = world.AddExposure(Member, ResponderExposureCategories.BloodbornePathogen, ResponderExposureStatuses.Confirmed, Yesterday, 55);
			var othersEntry = world.AddExposure(Other, ResponderExposureCategories.BloodbornePathogen, ResponderExposureStatuses.Confirmed, Yesterday);

			async Task Report(string exposureId) => await service.ReportExposureAsync(DepartmentId, Member, new ExposureIncidentReport { ResponderExposureId = exposureId, Route = 1 });

			(await FluentActions.Awaiting(() => Report(carcinogen.RmsResponderExposureId)).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("entry_not_bloodborne");
			(await FluentActions.Awaiting(() => Report(suggested.RmsResponderExposureId)).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("entry_not_bloodborne");
			(await FluentActions.Awaiting(() => Report(othersEntry.RmsResponderExposureId)).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("entry_not_found");

			await Report(bbp.RmsResponderExposureId);
			world.Incidents.Single().CallId.Should().Be(55, "the case points at the entry's call");
			world.Exposures.Should().HaveCount(4, "no second entry is logged");
			(await FluentActions.Awaiting(() => Report(bbp.RmsResponderExposureId)).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("case_exists");
		}

		[Test]
		public async Task Only_the_member_and_the_designated_officers_read_a_case_and_administration_alone_opens_nothing()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var incident = world.AddIncident(Member, ExposureIncidentStages.Reported, Yesterday);
			incident.MedicalNotes = "Baseline drawn";

			(await service.GetCaseAsync(DepartmentId, Member, incident.RmsExposureIncidentId)).Should().NotBeNull();
			(await service.GetCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId)).MedicalNotes.Should().Be("Baseline drawn");
			(await service.GetCaseAsync(DepartmentId, Admin, incident.RmsExposureIncidentId)).Should().BeNull("department administration alone opens no record");
			(await service.GetCaseAsync(DepartmentId, Other, incident.RmsExposureIncidentId)).Should().BeNull();
			(await service.GetCaseAsync(DepartmentId, WellnessOfficer, incident.RmsExposureIncidentId)).Should().BeNull("a designated role is named, so cases are the designated officer's");

			await FluentActions.Awaiting(() => service.GetDepartmentCasesAsync(DepartmentId, Admin, true)).Should().ThrowAsync<UnauthorizedAccessException>();
			await FluentActions.Awaiting(() => service.AdvanceCaseAsync(DepartmentId, Member, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.Closed })).Should().ThrowAsync<UnauthorizedAccessException>("the member never moves their own case");
			var list = await service.GetDepartmentCasesAsync(DepartmentId, Designated, false);
			list.Single().MedicalNotes.Should().BeNull("the department list carries operational columns only");
		}

		[Test]
		public async Task The_designated_officer_moves_a_case_through_the_ryan_white_steps_and_closes_it_only_when_follow_ups_are_done()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var incident = world.AddIncident(Member, ExposureIncidentStages.Reported, Yesterday.AddDays(-1));

			(await FluentActions.Awaiting(() => service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.SourceFollowUpRequested })).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("facility_required");
			(await FluentActions.Awaiting(() => service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.NotificationReceived })).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("stage_not_allowed");

			await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.SourceFollowUpRequested, SourceFacility = "General Hospital ED", Notes = "Asked ED charge nurse" });
			var requestedOn = DateTime.UtcNow.AddHours(-2);
			await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.NotificationRequested, On = requestedOn });
			incident.NotificationDueOn.Should().BeCloseTo(requestedOn.AddHours(48), TimeSpan.FromSeconds(1));
			await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.NotificationReceived, Notes = "Source negative" });
			await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.FollowUp });

			var task = await service.AddCaseFollowUpAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, ExposureCaseFollowUpKinds.SixWeekTest, null, DateTime.UtcNow.AddDays(42));
			task.Purpose.Should().Be(RmsWorkAssignmentPurposes.ExposureCaseFollowUp);
			task.AssigneeUserId.Should().Be(Member);
			task.Note.Should().Be("six_week_test", "the note is a code, never free text");
			RmsWorkAssignmentPurposes.IsKnown(task.Purpose).Should().BeFalse("the generic Records assignment path cannot create or reach it");
			(await FluentActions.Awaiting(() => service.AddCaseFollowUpAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, ExposureCaseFollowUpKinds.Counselling, Other, null))
				.Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("follow_up_assignee_invalid");

			(await FluentActions.Awaiting(() => service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.Closed })).Should().ThrowAsync<MemberWellnessException>()).Which.Code.Should().Be("follow_ups_open");
			await service.CompleteCaseFollowUpAsync(DepartmentId, Member, task.RmsRecordWorkAssignmentId);
			var closed = await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId,
				new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.Closed, Notes = "All negative" });

			closed.Stage.Should().Be((int)ExposureIncidentStages.Closed);
			closed.SourceFacility.Should().Be("General Hospital ED");
			closed.NotificationResponse.Should().Be("Source negative");
			closed.ClosureNotes.Should().Be("All negative");
			world.Audits.Count(a => a.Type == AuditLogTypes.ExposureIncidentStageChanged).Should().Be(5);
			world.Audits.Should().NotContain(a => (a.Before ?? string.Empty).Contains("Source negative") || (a.After ?? string.Empty).Contains("Source negative"));
		}

		[Test]
		public async Task Withdrawing_a_case_cancels_its_open_follow_ups_and_it_never_moves_again()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var incident = world.AddIncident(Member, ExposureIncidentStages.FollowUp, Yesterday);
			var task = await service.AddCaseFollowUpAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, ExposureCaseFollowUpKinds.BaselineTesting, Designated, null);

			await service.AdvanceCaseAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, new ExposureIncidentStep { ToStage = (int)ExposureIncidentStages.Withdrawn });

			task.State.Should().Be((int)RmsWorkAssignmentState.Cancelled);
			ExposureIncidentWorkflow.NextStages(incident.Stage).Should().BeEmpty();
		}

		[Test]
		public async Task A_withheld_note_posted_back_as_the_placeholder_keeps_the_stored_value()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			var incident = world.AddIncident(Member, ExposureIncidentStages.FollowUp, Yesterday);
			incident.MedicalNotes = "rgdp:v1:sealed";
			incident.SourceFollowUpNotes = "Old";

			await service.UpdateCaseNotesAsync(DepartmentId, Designated, incident.RmsExposureIncidentId, "General", "New source notes",
				null, ProtectedDataEnvelope.RedactionValue, null);

			incident.MedicalNotes.Should().Be("rgdp:v1:sealed", "the officer could not see it, so it is not overwritten");
			incident.SourceFollowUpNotes.Should().Be("New source notes");

			var injury = world.AddInjury(Member, Yesterday);
			injury.TreatmentDetails = "rgdp:v1:sealed-treatment";
			await service.UpdateInjuryAsync(DepartmentId, WellnessOfficer, injury.RmsMemberInjuryId,
				new MemberInjuryInput { Description = "Edited", TreatmentDetails = ProtectedDataEnvelope.RedactionValue }, null);
			injury.TreatmentDetails.Should().Be("rgdp:v1:sealed-treatment");
			injury.Description.Should().Be("Edited");
		}

		[Test]
		public async Task Each_case_shows_the_members_bbp_training_status()
		{
			var world = new MemberHealthTestWorld();
			var service = world.Service();
			(await service.GetBbpTrainingStatusAsync(DepartmentId, Member)).State.Should().Be(BbpTrainingStates.None);

			world.Certifications.Add(new PersonnelCertification { DepartmentId = DepartmentId, UserId = Member, DepartmentCertificationTypeId = BbpTypeId, Status = 0, ExpiresOn = DateTime.UtcNow.AddDays(200) });
			(await service.GetBbpTrainingStatusAsync(DepartmentId, Member)).State.Should().Be(BbpTrainingStates.Current);

			world.Certifications.Single().ExpiresOn = DateTime.UtcNow.AddDays(10);
			(await service.GetBbpTrainingStatusAsync(DepartmentId, Member)).State.Should().Be(BbpTrainingStates.ExpiringSoon);

			world.Certifications.Single().ExpiresOn = DateTime.UtcNow.AddDays(-10);
			(await service.GetBbpTrainingStatusAsync(DepartmentId, Member)).State.Should().Be(BbpTrainingStates.Expired);

			world.Settings.BbpTrainingCertificationTypeId = 0;
			(await service.GetBbpTrainingStatusAsync(DepartmentId, Member)).State.Should().Be(BbpTrainingStates.NotTracked);
		}
	}

	/// <summary>The BBP case state machine (pure).</summary>
	[TestFixture]
	public class ExposureIncidentWorkflowTests
	{
		[TestCase(ExposureIncidentStages.Reported, ExposureIncidentStages.SourceFollowUpRequested, true)]
		[TestCase(ExposureIncidentStages.Reported, ExposureIncidentStages.NotificationRequested, true)]
		[TestCase(ExposureIncidentStages.Reported, ExposureIncidentStages.FollowUp, true)]
		[TestCase(ExposureIncidentStages.Reported, ExposureIncidentStages.Closed, false)]
		[TestCase(ExposureIncidentStages.Reported, ExposureIncidentStages.NotificationReceived, false)]
		[TestCase(ExposureIncidentStages.SourceFollowUpRequested, ExposureIncidentStages.NotificationRequested, true)]
		[TestCase(ExposureIncidentStages.NotificationRequested, ExposureIncidentStages.NotificationReceived, true)]
		[TestCase(ExposureIncidentStages.NotificationRequested, ExposureIncidentStages.FollowUp, true)]
		[TestCase(ExposureIncidentStages.NotificationRequested, ExposureIncidentStages.Closed, false)]
		[TestCase(ExposureIncidentStages.NotificationReceived, ExposureIncidentStages.Closed, true)]
		[TestCase(ExposureIncidentStages.FollowUp, ExposureIncidentStages.Closed, true)]
		[TestCase(ExposureIncidentStages.FollowUp, ExposureIncidentStages.Reported, false)]
		[TestCase(ExposureIncidentStages.Closed, ExposureIncidentStages.FollowUp, true)]
		[TestCase(ExposureIncidentStages.Closed, ExposureIncidentStages.Withdrawn, false)]
		[TestCase(ExposureIncidentStages.Withdrawn, ExposureIncidentStages.FollowUp, false)]
		[TestCase(ExposureIncidentStages.FollowUp, ExposureIncidentStages.Withdrawn, true)]
		public void Moves_follow_the_workflow(ExposureIncidentStages from, ExposureIncidentStages to, bool allowed)
		{
			ExposureIncidentWorkflow.CanMove((int)from, (int)to).Should().Be(allowed);
		}

		[Test]
		public void Unknown_stages_never_move_and_the_answer_is_overdue_after_48_hours()
		{
			ExposureIncidentWorkflow.CanMove(99, 0).Should().BeFalse();
			ExposureIncidentWorkflow.CanMove(0, 99).Should().BeFalse();
			var now = DateTime.UtcNow;
			var incident = new RmsExposureIncident { Stage = (int)ExposureIncidentStages.NotificationRequested, NotificationDueOn = now.AddMinutes(-1) };
			ExposureIncidentWorkflow.IsNotificationOverdue(incident, now).Should().BeTrue();
			incident.Stage = (int)ExposureIncidentStages.NotificationReceived;
			ExposureIncidentWorkflow.IsNotificationOverdue(incident, now).Should().BeFalse();
		}
	}

	/// <summary>
	/// A BBP case points at the call and never at the source patient: no entity, input or API model of WL1 has a patient-identity field.
	/// </summary>
	[TestFixture]
	public class NoPatientIdentityGuardTests
	{
		private static readonly string[] Forbidden = { "patient", "mrn", "medicalrecord", "dateofbirth", "birth", "dob", "ssn", "socialsecurity", "sourcename", "sourceperson", "sourceaddress", "sourcephone" };

		private static IEnumerable<Type> Types()
		{
			yield return typeof(RmsExposureIncident);
			yield return typeof(ExposureIncidentReport);
			yield return typeof(ExposureIncidentStep);
			yield return typeof(RmsMemberInjury);
			yield return typeof(MemberInjuryInput);
			yield return typeof(Osha301Report);
			yield return typeof(Resgrid.Web.Services.Models.v4.Wellness.ReportExposureInput);
			yield return typeof(Resgrid.Web.Services.Models.v4.Wellness.ExposureCaseData);
			yield return typeof(Resgrid.Web.Services.Models.v4.Wellness.ReportInjuryInput);
			yield return typeof(Resgrid.Web.Areas.User.Models.Wellness.ReportExposureView);
			yield return typeof(Resgrid.Web.Areas.User.Models.Wellness.ExposureCaseView);
		}

		[Test]
		public void No_wl1_type_carries_a_patient_identity_field()
		{
			foreach (var type in Types())
				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
					Forbidden.Should().NotContain(f => property.Name.ToLowerInvariant().Contains(f), $"{type.Name}.{property.Name} must not hold patient identity (§13b.5)");
		}

		[Test]
		public void The_migrations_add_no_patient_identity_column()
		{
			var root = new System.IO.DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			foreach (var name in new[] { "M0372_AddExposureIncidents", "M0373_AddMemberInjuries", "M0374_AddMedicalClearances" })
			{
				foreach (var file in new[]
				{
					System.IO.Path.Combine(root!.FullName, "Providers", "Resgrid.Providers.Migrations", "Migrations", name + ".cs"),
					System.IO.Path.Combine(root.FullName, "Providers", "Resgrid.Providers.MigrationsPg", "Migrations", name + "Pg.cs")
				})
				{
					var columns = System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(file), "WithColumn\\(\"([A-Za-z]+)\"\\)").Select(m => m.Groups[1].Value.ToLowerInvariant());
					columns.Should().NotContain(c => Forbidden.Any(f => c.Contains(f)), file);
				}
			}
		}
	}
}
