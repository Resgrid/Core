using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Services.CostRecovery;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase C-M3: the pure CFAA expected-reimbursement calculator (plan C4,
	/// decision 37; C11 acceptance 6) and the pure F-42 checklist / administrative-rate worksheet.
	/// </summary>
	[TestFixture]
	public class CalOesMarsCalculatorTests
	{
		private static readonly DateTime Dispatch = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

		private static List<CalOesMarsRateLine> Rates() => new List<CalOesMarsRateLine>
		{
			new CalOesMarsRateLine { CalOesMarsRateLineId = "sal-capt", LineKind = (int)CalOesMarsRateLineKinds.SalarySurvey, ClassificationCode = "Captain", StraightRate = 60, OvertimeRate = 90, OvertimeEligible = true, PortalToPortalEligible = true, RowVersion = 2 },
			new CalOesMarsRateLine { CalOesMarsRateLineId = "sal-ff", LineKind = (int)CalOesMarsRateLineKinds.SalarySurvey, ClassificationCode = "Firefighter", StraightRate = 40, OvertimeEligible = true, PortalToPortalEligible = true },
			new CalOesMarsRateLine { CalOesMarsRateLineId = "app-t3", LineKind = (int)CalOesMarsRateLineKinds.OfficialApparatus, ResourceCode = "Type 3 Engine", Basis = (int)CalOesMarsRateBases.Hourly, StraightRate = 85 },
			new CalOesMarsRateLine { CalOesMarsRateLineId = "sup", LineKind = (int)CalOesMarsRateLineKinds.OfficialSupportVehicle, Basis = (int)CalOesMarsRateBases.Daily, StraightRate = 150 },
			new CalOesMarsRateLine { CalOesMarsRateLineId = "pov", LineKind = (int)CalOesMarsRateLineKinds.PrivatelyOwnedVehicle, Basis = (int)CalOesMarsRateBases.PerMile, StraightRate = 0.67m },
			new CalOesMarsRateLine { CalOesMarsRateLineId = "fema", LineKind = (int)CalOesMarsRateLineKinds.SpecialEquipment, FemaCode = "8720", Basis = (int)CalOesMarsRateBases.Hourly, StraightRate = 22 }
		};

		private static CalOesMarsF42Snapshot Engine() => new CalOesMarsF42Snapshot
		{
			DispatchedOn = Dispatch, CommittedOn = Dispatch, ReturnedOn = Dispatch.AddHours(48),
			Vehicles =
			{
				new CalOesMarsF42Vehicle { Kind = "Apparatus", Designator = "E-31", ResourceCode = "Type 3 Engine", CommittedHours = 48, CommittedDays = 2 },
				new CalOesMarsF42Vehicle { Kind = "Support", Designator = "U-1", CommittedHours = 48, CommittedDays = 2 },
				new CalOesMarsF42Vehicle { Kind = "POV", Designator = "POV Smith", StartOdometer = 1000, EndOdometer = 1120 },
				new CalOesMarsF42Vehicle { Kind = "Equipment", Designator = "Pump", FemaCode = "8720", CommittedHours = 10 }
			},
			Personnel =
			{
				new CalOesMarsF42Person { DeploymentPersonnelId = "p1", Name = "A. Captain", ClassificationCode = "Captain", CommittedOn = Dispatch, ReleasedOn = Dispatch.AddHours(48), CommittedHours = 48, ActualHours = { new CalOesMarsDailyHours { Date = Dispatch.Date, Hours = 14 }, new CalOesMarsDailyHours { Date = Dispatch.Date.AddDays(1), Hours = 10 } } },
				new CalOesMarsF42Person { DeploymentPersonnelId = "p2", Name = "B. Firefighter", ClassificationCode = "Firefighter", CommittedOn = Dispatch, ReleasedOn = Dispatch.AddHours(48), CommittedHours = 48, ActualHours = { new CalOesMarsDailyHours { Date = Dispatch.Date, Hours = 8 } } },
				new CalOesMarsF42Person { DeploymentPersonnelId = "p3", Name = "C. Unknown", ClassificationCode = "Chaplain", CommittedHours = 48 }
			}
		};

		[Test]
		public void Actual_hours_agreement_pays_dtr_hours_with_overtime_after_eight_per_day()
		{
			var result = new CalOesMarsReimbursementCalculator().Calculate(new CalOesMarsReimbursementInput
			{
				F42 = Engine(), RateLines = Rates(), RateProfileVersion = "rp:1", AdministrativeRatePercent = 10,
				Agreement = new CalOesMarsAgreementSnapshot { CompensationMethod = (int)CalOesMarsCompensationMethods.ActualHours, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterEightHoursPerDay }
			});

			// Captain: day 1 = 8 straight + 6 OT, day 2 = 8 straight + 2 OT → 16 × 60 + 8 × 90.
			var captain = result.Lines.Where(l => l.SubjectId == "p1").ToList();
			captain.Should().HaveCount(2);
			captain[0].Quantity.Should().Be(16); captain[0].Rate.Should().Be(60); captain[0].ExpectedAmount.Should().Be(960);
			captain[1].Quantity.Should().Be(8); captain[1].Rate.Should().Be(90); captain[1].ExpectedAmount.Should().Be(720);
			captain[0].RateLineId.Should().Be("sal-capt"); captain[0].RateLineVersion.Should().Be(2); captain[0].SourceVersions.Should().Be("rp:1");
			// Firefighter without an overtime rate: OT at 1.5× straight; 8 hours only → no OT line.
			result.Lines.Where(l => l.SubjectId == "p2").Should().ContainSingle().Which.ExpectedAmount.Should().Be(320);
			// Unknown classification: an Excluded line, never a silent zero.
			var unknown = result.Lines.Single(l => l.SubjectId == "p3");
			unknown.EligibilityState.Should().Be((int)CalOesMarsEligibilityStates.Excluded);
			unknown.EligibilityReason.Should().Be(CalOesMarsExceptionCodes.NoSalaryRate);
			result.Exceptions.Should().Contain(e => e.Code == CalOesMarsExceptionCodes.NoSalaryRate && e.Detail.Contains("Chaplain"));

			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.Apparatus).ExpectedAmount.Should().Be(48 * 85);
			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.SupportVehicle).Should().Match<CalOesMarsReimbursementLine>(l => l.Quantity == 2 && l.Unit == "day" && l.ExpectedAmount == 300);
			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.PovMileage).Should().Match<CalOesMarsReimbursementLine>(l => l.Quantity == 120 && l.ExpectedAmount == 80.40m);
			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.SpecialEquipment).ExpectedAmount.Should().Be(220);
			// Administrative: 10 % of the eligible personnel total (960 + 720 + 320 = 2000).
			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.Administrative).ExpectedAmount.Should().Be(200);
			result.ExpectedTotal.Should().Be(960 + 720 + 320 + 4080 + 300 + 80.40m + 220 + 200);
		}

		[Test]
		public void Portal_to_portal_pays_every_committed_hour_and_flags_missing_rates_and_agreement()
		{
			var f42 = Engine();
			f42.Vehicles.RemoveAll(v => v.Kind != "Apparatus");
			f42.Vehicles[0].ResourceCode = "Type 1 Engine";
			f42.Personnel.RemoveAll(p => p.DeploymentPersonnelId != "p1");
			var result = new CalOesMarsReimbursementCalculator().Calculate(new CalOesMarsReimbursementInput
			{
				F42 = f42, RateLines = Rates(),
				Agreement = new CalOesMarsAgreementSnapshot { CompensationMethod = (int)CalOesMarsCompensationMethods.PortalToPortal, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterTwelveHoursPerDay }
			});
			// 48 committed hours over 2 days: 24 straight (12/day) + 24 overtime.
			var captain = result.Lines.Where(l => l.SubjectId == "p1").ToList();
			captain[0].Quantity.Should().Be(24); captain[1].Quantity.Should().Be(24);
			result.Lines.Single(l => l.LineKind == (int)CalOesMarsLineKinds.Apparatus).EligibilityState.Should().Be((int)CalOesMarsEligibilityStates.Excluded, "no Type 1 rate line");
			result.Exceptions.Select(e => e.Code).Should().Contain(new[] { CalOesMarsExceptionCodes.NoApparatusRate, CalOesMarsExceptionCodes.NoAdministrativeRate });

			var noAgreement = new CalOesMarsReimbursementCalculator().Calculate(new CalOesMarsReimbursementInput { F42 = Engine(), RateLines = Rates() });
			noAgreement.Exceptions.Should().Contain(e => e.Code == CalOesMarsExceptionCodes.NoAgreement);
			noAgreement.Lines.Where(l => l.SubjectId == "p1").Should().ContainSingle("without an agreement every DTR hour is straight time");
		}

		[Test]
		public void Expenses_are_evidence_gated_and_never_carry_internal_cost()
		{
			var result = new CalOesMarsReimbursementCalculator().Calculate(new CalOesMarsReimbursementInput
			{
				Expenses = new CalOesMarsExpenseClaimSnapshot
				{
					Lines =
					{
						new CalOesMarsExpenseLine { DeploymentExpenseId = "e1", Date = Dispatch.Date, Category = "Meal", Amount = 18.5m, ReceiptAttachmentId = 1 },
						new CalOesMarsExpenseLine { DeploymentExpenseId = "e2", Date = Dispatch.Date, Category = "Lodging", Amount = 140, ReceiptAttachmentId = 2, PreApproved = false },
						new CalOesMarsExpenseLine { DeploymentExpenseId = "e3", Date = Dispatch.Date, Category = "Miscellaneous", Amount = 30 },
						new CalOesMarsExpenseLine { DeploymentExpenseId = "e4", Date = Dispatch.Date, Category = "Rental", Amount = 500, ReceiptAttachmentId = 3, PreApproved = true }
					}
				},
				AdministrativeRatePercent = 10
			});
			result.Lines.Single(l => l.SourceExpenseId == "e1").EligibilityState.Should().Be((int)CalOesMarsEligibilityStates.Eligible, "meals need no pre-approval");
			result.Lines.Single(l => l.SourceExpenseId == "e2").EligibilityState.Should().Be((int)CalOesMarsEligibilityStates.Uncertain, "lodging without pre-approval may need ICS-213 evidence");
			result.Lines.Single(l => l.SourceExpenseId == "e3").EligibilityState.Should().Be((int)CalOesMarsEligibilityStates.Excluded, "no receipt");
			result.Lines.Single(l => l.SourceExpenseId == "e4").LineKind.Should().Be((int)CalOesMarsLineKinds.Rental);
			result.Lines.Should().NotContain(l => l.LineKind == (int)CalOesMarsLineKinds.Administrative, "the administrative rate applies to personnel, not expenses");
			result.ExpectedTotal.Should().Be(518.5m);
			result.UncertainTotal.Should().Be(140);
		}

		[Test]
		public void F42_checklist_blocks_bad_prefix_missing_signatures_duplicates_and_undocumented_rotations()
		{
			var authority = CalOesMarsAuthorityProfile.Current;
			var s = new CalOesMarsF42Snapshot
			{
				IncidentNumber = "CA-LNU-001234", OrderNumber = "O-1", RequestNumber = "X-12", ResourceType = "Type 3 Engine", DispatchedOn = Dispatch, ReleasedOn = Dispatch.AddHours(30),
				Vehicles = { new CalOesMarsF42Vehicle { Kind = "Apparatus", Designator = "E-31", LicensePlate = "ABC123" }, new CalOesMarsF42Vehicle { Kind = "Support", Designator = "E-31 (dup)", LicensePlate = "abc123" } },
				Personnel = { new CalOesMarsF42Person { Name = "A", CommittedOn = Dispatch.AddHours(-5) } },
				Rotations = { new CalOesMarsF42Rotation { On = Dispatch.Date.AddDays(1) } }
			};
			var result = new CalOesMarsValidationResult();
			CalOesMarsService.ValidateF42(result, s, authority, Array.Empty<Resgrid.Model.Invoicing.DeploymentAttachment>());
			result.Errors.Select(e => e.Code).Should().Contain(new[]
			{
				CalOesMarsValidationCodes.RequestPrefixInvalid, CalOesMarsValidationCodes.DuplicateVehicle, CalOesMarsValidationCodes.RotationUndocumented,
				CalOesMarsValidationCodes.RespondingSignatureMissing, CalOesMarsValidationCodes.IncidentAuthorizationMissing, CalOesMarsValidationCodes.PaperFallbackMissing
			});
			result.Warnings.Select(w => w.Code).Should().Contain(new[] { CalOesMarsValidationCodes.ReleaseIsNotReturn, CalOesMarsValidationCodes.PersonnelIntervalOutside });
			result.Errors.Single(e => e.Code == CalOesMarsValidationCodes.RequestPrefixInvalid).Box.Should().Be("request");
			result.IsReadyForPortal.Should().BeFalse();

			CalOesMarsService.IsValidRequestNumber("E-12", authority).Should().BeTrue();
			CalOesMarsService.IsValidRequestNumber("O 3", authority).Should().BeTrue();
			CalOesMarsService.IsValidRequestNumber("c-5.1", authority).Should().BeTrue();
			CalOesMarsService.IsValidRequestNumber("X-12", authority).Should().BeFalse();
			CalOesMarsService.IsValidRequestNumber("E", authority).Should().BeFalse();
			CalOesMarsService.IsValidRequestNumber("E-abc", authority).Should().BeFalse();

			// A return before the dispatch is an error; documentation-only softens the authorization to a warning.
			s.RequestNumber = "E-12"; s.ReturnedOn = Dispatch.AddHours(-1); s.DocumentationOnly = true; s.Vehicles.RemoveAt(1); s.Rotations.Clear(); s.RespondingSignerName = "Chief";
			var second = new CalOesMarsValidationResult();
			CalOesMarsService.ValidateF42(second, s, authority, new[] { new Resgrid.Model.Invoicing.DeploymentAttachment { DeploymentAttachmentId = 5, AttachmentType = (int)Resgrid.Model.Invoicing.DeploymentAttachmentTypes.PaperF42 } });
			second.Errors.Select(e => e.Code).Should().BeEquivalentTo(new[] { CalOesMarsValidationCodes.ReturnBeforeDispatch });
			second.Warnings.Select(w => w.Code).Should().Contain(new[] { CalOesMarsValidationCodes.IncidentAuthorizationMissing, CalOesMarsValidationCodes.DocumentationOnly });
		}

		[Test]
		public void Administrative_rate_worksheet_excludes_unallowable_and_incident_direct_and_compares_with_de_minimis()
		{
			var profile = new CalOesMarsRateProfile
			{
				CalOesMarsRateProfileId = "rp-admin", AdministrativeInputs =
				{
					new CalOesMarsAdministrativeRateInput { FiscalYear = 2025, Classification = (int)CalOesMarsCostClassifications.Direct, ActualAmount = "1000000", ReviewStatus = (int)CalOesMarsInputReviewStatuses.Accepted },
					new CalOesMarsAdministrativeRateInput { FiscalYear = 2025, Classification = (int)CalOesMarsCostClassifications.Indirect, ActualAmount = "150000", ReviewStatus = (int)CalOesMarsInputReviewStatuses.Accepted },
					new CalOesMarsAdministrativeRateInput { FiscalYear = 2025, Classification = (int)CalOesMarsCostClassifications.Unallowable, ActualAmount = "40000", ReviewStatus = (int)CalOesMarsInputReviewStatuses.Accepted },
					new CalOesMarsAdministrativeRateInput { FiscalYear = 2025, Classification = (int)CalOesMarsCostClassifications.Direct, ActualAmount = "25000", IncidentDirectExclusion = true, ReviewStatus = (int)CalOesMarsInputReviewStatuses.Accepted },
					new CalOesMarsAdministrativeRateInput { FiscalYear = 2025, Classification = (int)CalOesMarsCostClassifications.Indirect, ActualAmount = "9000", DoubleCountMarker = true, ReviewStatus = (int)CalOesMarsInputReviewStatuses.Pending }
				}
			};
			var blocked = CalOesMarsService.BuildAdministrativeRateDraft(profile, CalOesMarsAuthorityProfile.Current);
			blocked.IsReady.Should().BeFalse();
			blocked.Blockers.Should().Contain("double_count_unresolved");

			profile.AdministrativeInputs.Last().ReviewStatus = (int)CalOesMarsInputReviewStatuses.Excluded;
			var draft = CalOesMarsService.BuildAdministrativeRateDraft(profile, CalOesMarsAuthorityProfile.Current);
			draft.IsReady.Should().BeTrue();
			draft.AllowableDirect.Should().Be(1_000_000);
			draft.AllowableIndirect.Should().Be(150_000);
			draft.ExcludedUnallowable.Should().Be(40_000);
			draft.ExcludedIncidentDirect.Should().Be(25_000);
			draft.CalculatedPercent.Should().Be(15m);
			draft.DeMinimisPercent.Should().Be(10m);
			draft.MethodChosen.Should().Be((int)CalOesMarsAdministrativeRateMethods.Calculated);
			draft.ChosenPercent.Should().Be(15m);

			var empty = CalOesMarsService.BuildAdministrativeRateDraft(new CalOesMarsRateProfile { CalOesMarsRateProfileId = "rp-empty" }, CalOesMarsAuthorityProfile.Current);
			empty.IsReady.Should().BeFalse();
			empty.MethodChosen.Should().Be((int)CalOesMarsAdministrativeRateMethods.DeMinimis, "with no actuals the de-minimis option is the only choice");
		}

		[Test]
		public void Authority_profile_maps_external_statuses_and_pins_its_sources()
		{
			var profile = CalOesMarsAuthorityProfile.Current;
			profile.Code.Should().Be("CFAA-2026-08-21");
			profile.IsReviewed.Should().BeTrue();
			profile.MapRecordStatus("agency review").Should().Be(CalOesMarsLocalStates.ReturnedForAgencyReview);
			profile.MapRecordStatus("Documentation Only").Should().Be(CalOesMarsLocalStates.DocumentationOnly);
			profile.MapRecordStatus("Rejected").Should().BeNull("unknown vocabulary never guesses a local state");
			profile.MapInvoiceStatus("Pending Paying Entity Approval").Should().Be(CalOesMarsLocalStates.PendingPayingEntityApproval);
			profile.F42Boxes.Select(b => b.Id).Should().Contain(new[] { "request", "responding-signature", "incident-signature", "attachments" });
			profile.RequestPrefixes.Should().BeEquivalentTo(new[] { "E", "O", "C", "S", "A" });
			CalOesMarsAuthorityProfile.ForDispatch(new DateTime(2019, 1, 1)).Should().BeNull("nothing covers a dispatch before the profile's effective date");
			CalOesMarsAuthorityProfile.ForDispatch(Dispatch).Should().BeSameAs(profile);
		}
	}
}
