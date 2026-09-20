using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Services.CostRecovery
{
	/// <summary>
	/// Pure CFAA expected-reimbursement arithmetic (Workforce &amp; Business Operations plan, C4; decision 37). Rules,
	/// in order: (1) personnel — portal-to-portal pays every committed hour, actual-hours pays the DTR hours; overtime
	/// per the agreement's method (after 8 / after 12 hours per day, none, or per-agreement which is flagged);
	/// (2) official apparatus and support vehicles — hourly or daily rate line by resource code; (3) POV mileage by
	/// the per-mile line; (4) special equipment by FEMA code; (5) rental lines and (6) expenses — eligible only with a
	/// receipt (uncertain when pre-approval is missing); (7) administrative — the profile's percentage of the eligible
	/// personnel total. A missing rate leaves an Excluded line with the reason so the manager sees the gap instead of
	/// a silent zero. No Phase E cost, depreciation, maintenance or overhead enters any line.
	/// </summary>
	public sealed class CalOesMarsReimbursementCalculator : ICalOesMarsReimbursementCalculator
	{
		public CalOesMarsReimbursementResult Calculate(CalOesMarsReimbursementInput input)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var result = new CalOesMarsReimbursementResult();
			var rates = input.RateLines ?? new List<CalOesMarsRateLine>();
			var sort = 0;

			if (input.F42 != null)
			{
				var agreement = input.Agreement;
				if (agreement == null) result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoAgreement, "No MOU/MOA/GBR compensation method covers the dispatch date; personnel are paid on actual hours without overtime until one is recorded."));
				var portalToPortal = agreement?.CompensationMethod == (int)CalOesMarsCompensationMethods.PortalToPortal;
				var overtime = agreement == null ? CalOesMarsOvertimeMethods.None : (CalOesMarsOvertimeMethods)agreement.OvertimeMethod;
				if (overtime == CalOesMarsOvertimeMethods.PerAgreement) result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.OvertimePerAgreement, "The agreement's overtime method is not one the calculator models; every hour is estimated at the straight rate."));

				foreach (var person in input.F42.Personnel ?? new List<CalOesMarsF42Person>())
				{
					var rate = FindSalary(rates, person.ClassificationCode);
					if (rate == null)
					{
						result.Lines.Add(Line(CalOesMarsLineKinds.Personnel, person, 0, "hour", 0, null, CalOesMarsEligibilityStates.Excluded, CalOesMarsExceptionCodes.NoSalaryRate, ref sort, input));
						result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoSalaryRate, $"No Salary Survey / Attachment A line for classification '{person.ClassificationCode ?? "(none)"}' ({person.Name})."));
						continue;
					}
					var (straight, ot) = Hours(person, portalToPortal && rate.PortalToPortalEligible, overtime, rate.OvertimeEligible);
					if (straight == 0 && ot == 0 && !portalToPortal)
						result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoActualHours, $"{person.Name}: no daily time report hours; nothing to reimburse under an actual-hours agreement."));
					var straightRate = rate.StraightRate ?? 0m;
					var overtimeRate = rate.OvertimeRate ?? (straightRate * 1.5m);
					result.Lines.Add(Line(CalOesMarsLineKinds.Personnel, person, straight, "hour", straightRate, rate, CalOesMarsEligibilityStates.Eligible, null, ref sort, input));
					if (ot > 0) result.Lines.Add(Line(CalOesMarsLineKinds.Personnel, person, ot, "overtime hour", overtimeRate, rate, CalOesMarsEligibilityStates.Eligible, "overtime", ref sort, input));
				}

				foreach (var vehicle in input.F42.Vehicles ?? new List<CalOesMarsF42Vehicle>())
				{
					switch ((vehicle.Kind ?? string.Empty).ToLowerInvariant())
					{
						case "apparatus":
							AddVehicle(result, vehicle, rates, CalOesMarsRateLineKinds.OfficialApparatus, CalOesMarsLineKinds.Apparatus, CalOesMarsExceptionCodes.NoApparatusRate, ref sort, input);
							break;
						case "support":
							AddVehicle(result, vehicle, rates, CalOesMarsRateLineKinds.OfficialSupportVehicle, CalOesMarsLineKinds.SupportVehicle, CalOesMarsExceptionCodes.NoSupportRate, ref sort, input);
							break;
						case "pov":
							{
								var miles = vehicle.Miles ?? (vehicle.StartOdometer.HasValue && vehicle.EndOdometer.HasValue ? Math.Max(0, vehicle.EndOdometer.Value - vehicle.StartOdometer.Value) : (decimal?)null);
								var rate = rates.FirstOrDefault(r => r.LineKind == (int)CalOesMarsRateLineKinds.PrivatelyOwnedVehicle && r.Basis == (int)CalOesMarsRateBases.PerMile);
								if (rate == null)
								{
									result.Lines.Add(VehicleLine(CalOesMarsLineKinds.PovMileage, vehicle, miles ?? 0, "mile", 0, null, CalOesMarsEligibilityStates.Excluded, CalOesMarsExceptionCodes.NoPovRate, ref sort, input));
									result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoPovRate, $"No POV mileage line in the effective Rate Letter ({vehicle.Designator})."));
								}
								else if (!miles.HasValue)
								{
									result.Lines.Add(VehicleLine(CalOesMarsLineKinds.PovMileage, vehicle, 0, "mile", rate.StraightRate ?? 0, rate, CalOesMarsEligibilityStates.Uncertain, CalOesMarsExceptionCodes.MileageWithoutOdometer, ref sort, input));
									result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.MileageWithoutOdometer, $"{vehicle.Designator}: no odometer readings or miles recorded."));
								}
								else result.Lines.Add(VehicleLine(CalOesMarsLineKinds.PovMileage, vehicle, miles.Value, "mile", rate.StraightRate ?? 0, rate, CalOesMarsEligibilityStates.Eligible, null, ref sort, input));
								break;
							}
						case "equipment":
							{
								var rate = rates.FirstOrDefault(r => r.LineKind == (int)CalOesMarsRateLineKinds.SpecialEquipment && (Same(r.FemaCode, vehicle.FemaCode) || Same(r.ResourceCode, vehicle.ResourceCode)));
								if (rate == null)
								{
									result.Lines.Add(VehicleLine(CalOesMarsLineKinds.SpecialEquipment, vehicle, vehicle.CommittedHours, "hour", 0, null, CalOesMarsEligibilityStates.Excluded, CalOesMarsExceptionCodes.NoSpecialEquipmentRate, ref sort, input));
									result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoSpecialEquipmentRate, $"No special-equipment / FEMA line for '{vehicle.FemaCode ?? vehicle.ResourceCode ?? vehicle.Designator}'."));
								}
								else
								{
									var (qty, unit) = rate.Basis == (int)CalOesMarsRateBases.Daily ? (vehicle.CommittedDays, "day") : (vehicle.CommittedHours, "hour");
									result.Lines.Add(VehicleLine(CalOesMarsLineKinds.SpecialEquipment, vehicle, qty, unit, rate.StraightRate ?? 0, rate, CalOesMarsEligibilityStates.Eligible, null, ref sort, input));
								}
								break;
							}
					}
				}
			}

			if (input.Expenses != null)
			{
				foreach (var expense in input.Expenses.Lines ?? new List<CalOesMarsExpenseLine>())
				{
					var kind = string.Equals(expense.Category, "Rental", StringComparison.OrdinalIgnoreCase) ? CalOesMarsLineKinds.Rental : CalOesMarsLineKinds.Expense;
					var state = CalOesMarsEligibilityStates.Eligible;
					string reason = null;
					if (!expense.ReceiptAttachmentId.HasValue) { state = CalOesMarsEligibilityStates.Excluded; reason = CalOesMarsExceptionCodes.ExpenseWithoutReceipt; result.Exceptions.Add(Exception(reason, $"{expense.Date:yyyy-MM-dd} {expense.Category} {expense.Amount:N2}: no receipt attached.")); }
					else if (!expense.PreApproved && kind == CalOesMarsLineKinds.Expense && !string.Equals(expense.Category, "Meal", StringComparison.OrdinalIgnoreCase)) { state = CalOesMarsEligibilityStates.Uncertain; reason = CalOesMarsExceptionCodes.ExpenseNotPreApproved; result.Exceptions.Add(Exception(reason, $"{expense.Date:yyyy-MM-dd} {expense.Category} {expense.Amount:N2}: not pre-approved; MARS may require ICS-213 evidence.")); }
					result.Lines.Add(new CalOesMarsReimbursementLine
					{
						LineKind = (int)kind, LineDate = expense.Date, SourceExpenseId = expense.DeploymentExpenseId, Quantity = 1, Unit = "each", Rate = expense.Amount,
						ExpectedAmount = Round(expense.Amount), EligibilityState = (int)state, EligibilityReason = reason, SortOrder = sort++, SourceVersions = input.RateProfileVersion, SubjectName = expense.Description
					});
				}
			}

			var personnelEligible = result.Lines.Where(l => l.LineKind == (int)CalOesMarsLineKinds.Personnel && l.EligibilityState == (int)CalOesMarsEligibilityStates.Eligible).Sum(l => l.ExpectedAmount);
			if (personnelEligible > 0)
			{
				if (input.AdministrativeRatePercent.HasValue && input.AdministrativeRatePercent.Value > 0)
				{
					result.Lines.Add(new CalOesMarsReimbursementLine
					{
						LineKind = (int)CalOesMarsLineKinds.Administrative, Quantity = personnelEligible, Unit = "personnel $", Rate = input.AdministrativeRatePercent.Value / 100m,
						ExpectedAmount = Round(personnelEligible * input.AdministrativeRatePercent.Value / 100m), EligibilityState = (int)CalOesMarsEligibilityStates.Eligible, SortOrder = sort++, SourceVersions = input.RateProfileVersion, SubjectName = "Administrative rate"
					});
				}
				else result.Exceptions.Add(Exception(CalOesMarsExceptionCodes.NoAdministrativeRate, "No administrative rate is recorded for the dispatch date; no administrative line is estimated."));
			}

			return result;
		}

		/// <summary>Straight and overtime hours for one person under the agreement.</summary>
		public static (decimal Straight, decimal Overtime) Hours(CalOesMarsF42Person person, bool portalToPortal, CalOesMarsOvertimeMethods overtime, bool overtimeEligible)
		{
			var threshold = overtime switch
			{
				CalOesMarsOvertimeMethods.AfterEightHoursPerDay => 8m,
				CalOesMarsOvertimeMethods.AfterTwelveHoursPerDay => 12m,
				_ => (decimal?)null
			};
			if (portalToPortal)
			{
				// Every committed hour is paid; overtime accrues per calendar day of the commitment when the agreement says so.
				var total = person.CommittedHours > 0 ? person.CommittedHours : (person.CommittedOn.HasValue && person.ReleasedOn.HasValue ? (decimal)(person.ReleasedOn.Value - person.CommittedOn.Value).TotalHours : 0m);
				total = Math.Max(0, Math.Round(total, 2));
				if (!threshold.HasValue || !overtimeEligible) return (total, 0);
				var days = Math.Max(1, (int)Math.Ceiling(total / 24m));
				var straight = Math.Min(total, days * threshold.Value);
				return (straight, Math.Max(0, total - straight));
			}
			decimal s = 0, o = 0;
			foreach (var day in (person.ActualHours ?? new List<CalOesMarsDailyHours>()).GroupBy(h => h.Date.Date))
			{
				var hours = Math.Max(0, day.Sum(h => h.Hours));
				if (!threshold.HasValue || !overtimeEligible) { s += hours; continue; }
				s += Math.Min(hours, threshold.Value);
				o += Math.Max(0, hours - threshold.Value);
			}
			return (Math.Round(s, 2), Math.Round(o, 2));
		}

		private static void AddVehicle(CalOesMarsReimbursementResult result, CalOesMarsF42Vehicle vehicle, List<CalOesMarsRateLine> rates, CalOesMarsRateLineKinds rateKind, CalOesMarsLineKinds lineKind, string missingCode, ref int sort, CalOesMarsReimbursementInput input)
		{
			var rate = rates.FirstOrDefault(r => r.LineKind == (int)rateKind && Same(r.ResourceCode, vehicle.ResourceCode)) ?? rates.FirstOrDefault(r => r.LineKind == (int)rateKind && string.IsNullOrWhiteSpace(r.ResourceCode));
			if (rate == null)
			{
				result.Lines.Add(VehicleLine(lineKind, vehicle, vehicle.CommittedHours, "hour", 0, null, CalOesMarsEligibilityStates.Excluded, missingCode, ref sort, input));
				result.Exceptions.Add(Exception(missingCode, $"No {rateKind} rate line for resource code '{vehicle.ResourceCode ?? "(none)"}' ({vehicle.Designator})."));
				return;
			}
			var (qty, unit) = rate.Basis == (int)CalOesMarsRateBases.Daily ? (vehicle.CommittedDays, "day") : (vehicle.CommittedHours, "hour");
			result.Lines.Add(VehicleLine(lineKind, vehicle, qty, unit, rate.StraightRate ?? 0, rate, CalOesMarsEligibilityStates.Eligible, null, ref sort, input));
		}

		private static CalOesMarsRateLine FindSalary(List<CalOesMarsRateLine> rates, string classification) =>
			rates.FirstOrDefault(r => (r.LineKind == (int)CalOesMarsRateLineKinds.SalarySurvey || r.LineKind == (int)CalOesMarsRateLineKinds.AttachmentANonSuppression) && Same(r.ClassificationCode, classification));

		private static bool Same(string a, string b) => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		private static CalOesMarsReimbursementLine Line(CalOesMarsLineKinds kind, CalOesMarsF42Person person, decimal quantity, string unit, decimal rate, CalOesMarsRateLine rateLine, CalOesMarsEligibilityStates state, string reason, ref int sort, CalOesMarsReimbursementInput input) => new CalOesMarsReimbursementLine
		{
			LineKind = (int)kind, SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, SubjectId = person.DeploymentPersonnelId ?? person.UserId, SubjectName = person.Name, Quantity = quantity, Unit = unit, Rate = rate,
			RateLineId = rateLine?.CalOesMarsRateLineId, RateLineVersion = rateLine?.RowVersion, ExpectedAmount = Round(quantity * rate), EligibilityState = (int)state, EligibilityReason = reason, SortOrder = sort++,
			SourceVersions = input.RateProfileVersion, LineDate = person.CommittedOn
		};

		private static CalOesMarsReimbursementLine VehicleLine(CalOesMarsLineKinds kind, CalOesMarsF42Vehicle vehicle, decimal quantity, string unit, decimal rate, CalOesMarsRateLine rateLine, CalOesMarsEligibilityStates state, string reason, ref int sort, CalOesMarsReimbursementInput input) => new CalOesMarsReimbursementLine
		{
			LineKind = (int)kind, SubjectType = string.IsNullOrWhiteSpace(vehicle.DeploymentEquipmentId) ? (int)DeploymentTimeSubjectTypes.Unit : (int)DeploymentTimeSubjectTypes.Equipment, SubjectId = vehicle.DeploymentUnitId ?? vehicle.DeploymentEquipmentId ?? vehicle.Designator,
			SubjectName = vehicle.Designator, Quantity = quantity, Unit = unit, Rate = rate, RateLineId = rateLine?.CalOesMarsRateLineId, RateLineVersion = rateLine?.RowVersion, ExpectedAmount = Round(quantity * rate),
			EligibilityState = (int)state, EligibilityReason = reason, SortOrder = sort++, SourceVersions = input.RateProfileVersion
		};

		private static CalOesMarsValidationIssue Exception(string code, string detail) => new CalOesMarsValidationIssue { Code = code, Detail = detail };

		public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
	}

	/// <summary>P0 gateway: the reviewed portal address only. Stores no credential; performs zero external writes.</summary>
	public sealed class ManualCalOesMarsGateway : ICalOesMarsExternalGateway
	{
		public string Name => "manual";
		public bool SupportsExternalWrites => false;
		public string GetPortalUrl(CalOesMarsWorkItem workItem) => Config.CostRecoveryConfig.CalOesMarsPortalUrl;
	}
}
