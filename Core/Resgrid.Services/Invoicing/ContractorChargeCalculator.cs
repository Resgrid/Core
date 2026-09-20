using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model.Invoicing;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// The pure contractor billing calculator (plan C4, decisions 16/17/21). Turns approved daily time reports into a
	/// charge set: one line per billing day per subject, premium adders per person, out-of-province per diems,
	/// mileage and fuel deductions per vehicle, and billable expenses passed through. No I/O — the engine service
	/// loads the graph and hands it in.
	/// </summary>
	public static class ContractorChargeCalculator
	{
		public static ContractorChargeSet Calculate(ContractorChargeInput input)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			var deployment = input.Deployment ?? throw new ArgumentException("Deployment is required.", nameof(input));
			var set = new ContractorChargeSet
			{
				DeploymentId = deployment.DeploymentId,
				Currency = input.Schedule?.Currency ?? deployment.Currency ?? "USD",
				DiscountPercent = input.DiscountPercent
			};

			if (input.Schedule == null)
			{
				set.Warnings.Add(Warn(ContractorChargeWarningCodes.ScheduleMissing, "No rate schedule applies to this deployment."));
				return set;
			}

			var reports = (input.Reports ?? new List<DeploymentTimeReport>()).Where(r => r != null && !r.IsDeleted).OrderBy(r => r.ReportDate).ThenBy(r => r.ReportNumber).ToList();
			if (reports.Count == 0)
			{
				set.Warnings.Add(Warn(ContractorChargeWarningCodes.NoReports, "No approved, unbilled time reports."));
			}

			var policy = input.Schedule.Policy;
			var sort = 0;
			foreach (var report in reports)
			{
				set.ReportIds.Add(report.DeploymentTimeReportId);
				var day = report.ReportDate.Date;
				var isCancellationDay = input.CancellationDate.HasValue && input.CancellationDate.Value.Date == day;
				var entries = (report.Entries ?? new List<DeploymentTimeEntry>()).OrderBy(e => e.StartTime).ThenBy(e => e.SortOrder).ToList();

				foreach (var group in entries.GroupBy(e => (e.SubjectType, SubjectKey(e))).OrderBy(g => g.Key.SubjectType).ThenBy(g => g.Key.Item2))
				{
					var subjectType = (DeploymentTimeSubjectTypes)group.Key.SubjectType;
					var subjectId = group.Key.Item2;
					if (string.IsNullOrWhiteSpace(subjectId)) continue;
					var subjectName = input.SubjectNames != null && input.SubjectNames.TryGetValue(subjectId, out var name) ? name : subjectId;
					var spans = group.ToList();

					var (entry, entryWarning) = ResolveEntry(input, subjectType, subjectId, spans, day);
					if (entryWarning != null) { entryWarning.DeploymentTimeReportId = report.DeploymentTimeReportId; entryWarning.SubjectId = subjectId; set.Warnings.Add(entryWarning); }
					if (entry == null) continue;

					var lines = new List<ContractorChargeLine>();
					switch ((BillingBases)entry.BillingBasis)
					{
						case BillingBases.Hourly:
							lines.AddRange(HourlyLines(input, report, day, isCancellationDay, subjectType, subjectId, subjectName, entry, spans, policy, set.Warnings));
							break;
						case BillingBases.Daily:
						case BillingBases.PerPersonPerDay:
							lines.AddRange(DailyLines(input, report, day, isCancellationDay, subjectType, subjectId, subjectName, entry, spans, policy, set.Warnings));
							break;
						case BillingBases.Fixed:
							lines.AddRange(FixedLines(report, day, isCancellationDay, subjectType, subjectId, subjectName, entry, spans, set.Warnings));
							break;
						case BillingBases.PerKilometer:
							break; // the mileage rule below is the whole charge
					}

					if (subjectType == DeploymentTimeSubjectTypes.Personnel)
						lines.AddRange(OutOfProvinceLines(input, report, day, subjectId, subjectName, entry, spans));
					if (subjectType != DeploymentTimeSubjectTypes.Personnel)
						lines.AddRange(MileageAndFuelLines(report, day, subjectType, subjectId, subjectName, entry, spans, policy));

					foreach (var line in lines) { line.SortOrder = sort++; set.Lines.Add(line); }
				}
			}

			foreach (var line in ExpenseLines(input, reports, policy, set.Warnings)) { line.SortOrder = sort++; set.Lines.Add(line); }
			return set;
		}

		#region Entry resolution

		private static string SubjectKey(DeploymentTimeEntry e) => (DeploymentTimeSubjectTypes)e.SubjectType switch
		{
			DeploymentTimeSubjectTypes.Personnel => e.DeploymentPersonnelId,
			DeploymentTimeSubjectTypes.Unit => e.DeploymentUnitId,
			_ => e.DeploymentEquipmentId
		};

		private static (RateScheduleEntry Entry, ContractorChargeWarning Warning) ResolveEntry(ContractorChargeInput input, DeploymentTimeSubjectTypes subjectType, string subjectId, List<DeploymentTimeEntry> spans, DateTime day)
		{
			var entries = (input.Schedule.Entries ?? new List<RateScheduleEntry>()).Where(e => e != null && !e.IsDeleted && e.IsActive).ToList();
			RateScheduleEntry Find(string id) => string.IsNullOrWhiteSpace(id) ? null : entries.FirstOrDefault(e => string.Equals(e.RateScheduleEntryId, id, StringComparison.OrdinalIgnoreCase));

			switch (subjectType)
			{
				case DeploymentTimeSubjectTypes.Personnel:
				{
					var person = input.Personnel?.FirstOrDefault(p => string.Equals(p.DeploymentPersonnelId, subjectId, StringComparison.OrdinalIgnoreCase));
					var pinned = Find(person?.RateScheduleEntryId);
					if (pinned != null) return (pinned, null);
					var code = spans.Select(s => s.CertificationCode).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? person?.CertificationCode;
					var byCode = string.IsNullOrWhiteSpace(code) ? null : entries.FirstOrDefault(e => e.EntryType == (int)RateEntryTypes.PersonnelCertification && string.Equals(e.CertificationCode, code, StringComparison.OrdinalIgnoreCase));
					return byCode != null ? (byCode, null) : (null, Warn(ContractorChargeWarningCodes.RateEntryMissing, $"No personnel rate entry for certification '{code ?? "(none)"}'."));
				}
				case DeploymentTimeSubjectTypes.Unit:
				{
					var unit = input.Units?.FirstOrDefault(u => string.Equals(u.DeploymentUnitId, subjectId, StringComparison.OrdinalIgnoreCase));
					var pinned = Find(unit?.RateScheduleEntryId);
					if (pinned == null) return (null, Warn(ContractorChargeWarningCodes.RateEntryMissing, "No crew or vehicle rate entry is pinned to this unit."));
					if (string.IsNullOrWhiteSpace(pinned.GroupKey)) return (pinned, null);

					// Decision 17: the unit bills at the crew size actually filled that day; the family sibling with that size wins.
					var crewSize = spans.Where(s => s.CrewSizeSnapshot.HasValue).Select(s => s.CrewSizeSnapshot.Value).DefaultIfEmpty(0).Max();
					if (crewSize <= 0)
						crewSize = input.Personnel?.Count(p => string.Equals(p.DeploymentUnitId, subjectId, StringComparison.OrdinalIgnoreCase) && p.AddedOn.Date <= day && (!p.RemovedOn.HasValue || p.RemovedOn.Value.Date > day)) ?? 0;
					if (crewSize <= 0) return (pinned, null);

					var family = entries.Where(e => string.Equals(e.GroupKey, pinned.GroupKey, StringComparison.OrdinalIgnoreCase) && e.CrewSize.HasValue).OrderBy(e => e.CrewSize).ToList();
					var exact = family.FirstOrDefault(e => e.CrewSize == crewSize);
					if (exact != null) return (exact, null);
					var lower = family.LastOrDefault(e => e.CrewSize < crewSize);
					if (lower != null) return (lower, Warn(ContractorChargeWarningCodes.CrewSizeFallback, $"No {crewSize}-person entry in family '{pinned.GroupKey}'; billed at the {lower.CrewSize}-person rate."));
					return (pinned, Warn(ContractorChargeWarningCodes.CrewSizeFallback, $"No entry at or below {crewSize} persons in family '{pinned.GroupKey}'; billed at the pinned entry."));
				}
				default:
				{
					var equipment = input.Equipment?.FirstOrDefault(e => string.Equals(e.DeploymentEquipmentId, subjectId, StringComparison.OrdinalIgnoreCase));
					var pinned = Find(equipment?.RateScheduleEntryId);
					if (pinned != null) return (pinned, null);
					var byItem = string.IsNullOrWhiteSpace(equipment?.InventoryItemId) ? null : entries.FirstOrDefault(e => string.Equals(e.InventoryItemId, equipment.InventoryItemId, StringComparison.OrdinalIgnoreCase));
					return byItem != null ? (byItem, null) : (null, Warn(ContractorChargeWarningCodes.RateEntryMissing, "No equipment rate entry is pinned to this item."));
				}
			}
		}

		#endregion

		#region Hourly

		private sealed class HourBuckets
		{
			public decimal Standby;
			public decimal TravelFlat;
			public List<(RateBandTypes Band, decimal Hours)> Deployment = new List<(RateBandTypes, decimal)>();
			public bool Mobilized;
			public bool TravelCapped;
			public decimal DeploymentTotal => Deployment.Sum(d => d.Hours);
		}

		private static IEnumerable<ContractorChargeLine> HourlyLines(ContractorChargeInput input, DeploymentTimeReport report, DateTime day, bool isCancellationDay, DeploymentTimeSubjectTypes subjectType, string subjectId, string subjectName,
			RateScheduleEntry entry, List<DeploymentTimeEntry> spans, RateSchedulePolicy policy, List<ContractorChargeWarning> warnings)
		{
			var buckets = BuildHourBuckets(report, isCancellationDay, entry, spans, policy);
			if (buckets.TravelCapped) warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.TravelCapped, Message = $"Travel hours capped at {policy.TravelDayCapHours:0.##}h.", DeploymentTimeReportId = report.DeploymentTimeReportId, SubjectId = subjectId });

			var line = new ContractorChargeLine
			{
				Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
				SubjectType = (int)subjectType, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.Hourly
			};

			void AddBand(RateBandTypes bandType, decimal hours, string label)
			{
				if (hours <= 0) return;
				var band = entry.Band(bandType) ?? (bandType == RateBandTypes.Overtime2 ? entry.Band(RateBandTypes.Overtime1) : null);
				if (band == null && bandType != RateBandTypes.Deployment) band = entry.Band(RateBandTypes.Deployment);
				if (band == null)
				{
					warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.BandMissing, Message = $"Entry '{entry.Name}' has no {bandType} rate; {hours:0.##}h unbilled.", DeploymentTimeReportId = report.DeploymentTimeReportId, SubjectId = subjectId });
					return;
				}
				line.Bands.Add(new ContractorChargeBand { BandType = (int)bandType, Label = label, Hours = hours, Rate = band.Rate, Amount = Money(hours * band.Rate) });
			}

			foreach (var (bandType, hours) in buckets.Deployment.GroupBy(d => d.Band).Select(g => (g.Key, g.Sum(d => d.Hours))).OrderBy(b => b.Key))
				AddBand(bandType, hours, BandLabel(bandType));
			AddBand(RateBandTypes.Deployment, buckets.TravelFlat, "Travel");
			AddBand(RateBandTypes.Standby, buckets.Standby, "Standby");

			if (line.Bands.Count == 0) yield break;
			line.Quantity = line.Bands.Sum(b => b.Hours);
			line.Amount = line.Bands.Sum(b => b.Amount);
			line.UnitRate = line.Quantity > 0 ? Math.Round(line.Amount / line.Quantity, 4, MidpointRounding.AwayFromZero) : 0;
			line.Description = Describe(day, report, entry.Name, subjectName, $"{line.Quantity:0.##}h" + BandBreakdown(line.Bands));
			yield return line;

			if (subjectType != DeploymentTimeSubjectTypes.Personnel) yield break;
			foreach (var premiumLine in PremiumLines(input, report, day, subjectId, subjectName, entry, line.Bands))
				yield return premiumLine;
		}

		private static HourBuckets BuildHourBuckets(DeploymentTimeReport report, bool isCancellationDay, RateScheduleEntry entry, List<DeploymentTimeEntry> spans, RateSchedulePolicy policy)
		{
			var b = new HourBuckets { Mobilized = spans.Count > 0 };
			decimal Net(DeploymentTimeEntry e) => Math.Max(0m, (decimal)(e.EndTime - e.StartTime).TotalHours - e.UnpaidBreakMinutes / 60m);

			b.Standby = Round(spans.Where(s => s.EntryType == (int)DeploymentTimeEntryTypes.Standby).Sum(Net), policy.RoundingMinutes);
			var travel = spans.Where(s => s.EntryType == (int)DeploymentTimeEntryTypes.Travel).Sum(Net);
			if (policy.TravelDayCapHours > 0 && travel > policy.TravelDayCapHours) { travel = policy.TravelDayCapHours; b.TravelCapped = true; }
			travel = Round(travel, policy.RoundingMinutes);

			// Deployment hours split across Deployment / Overtime1 / Overtime2 by the entry's thresholds. Travel joins them only
			// portal-to-portal (customer contracts); otherwise it bills flat at the deployment rate and never earns overtime.
			var work = spans.Where(s => s.EntryType == (int)DeploymentTimeEntryTypes.Deployment || (policy.PortalToPortal && s.EntryType == (int)DeploymentTimeEntryTypes.Travel)).ToList();
			if (!policy.PortalToPortal) b.TravelFlat = travel;
			// Portal-to-portal still honours the travel cap: the excess above the cap comes off the day's work hours.
			var travelExcess = policy.PortalToPortal ? Math.Max(0m, spans.Where(s => s.EntryType == (int)DeploymentTimeEntryTypes.Travel).Sum(Net) - travel) : 0m;

			var ot1 = entry.Band(RateBandTypes.Overtime1)?.ThresholdStartHours;
			var ot2 = entry.Band(RateBandTypes.Overtime2)?.ThresholdStartHours;
			var carry = report.NoClear8 && policy.NoClear8CarryOver && ot1.HasValue ? ot1.Value : 0m;

			var runs = policy.OvertimeBasis == OvertimeBases.ConsecutiveHours ? ContinuousRuns(work, policy.ContinuousRunGapMinutes) : new List<List<DeploymentTimeEntry>> { work };
			var first = true;
			foreach (var run in runs)
			{
				var hours = Round(run.Sum(Net), policy.RoundingMinutes);
				if (travelExcess > 0) { var cut = Math.Min(hours, Round(travelExcess, policy.RoundingMinutes)); hours -= cut; travelExcess -= cut; }
				if (hours <= 0) continue;
				foreach (var piece in Split(hours, first ? carry : 0m, ot1, ot2)) b.Deployment.Add(piece);
				first = false;
			}

			// Minimums lift the day's deployment hours; the lift bills at the deployment band.
			var actual = b.DeploymentTotal;
			var minimum = 0m;
			if (isCancellationDay) minimum = Math.Max(minimum, policy.CancellationMinimumHours);
			if (report.UnsafeConditionsStandDown) minimum = Math.Max(minimum, policy.UnsafeStandDownHours);
			if (b.Mobilized && policy.DailyGuaranteeHours.HasValue) minimum = Math.Max(minimum, policy.DailyGuaranteeHours.Value);
			if (minimum > actual) b.Deployment.Add((RateBandTypes.Deployment, minimum - actual));
			if (minimum > 0) b.Mobilized = true;
			return b;
		}

		private static List<List<DeploymentTimeEntry>> ContinuousRuns(List<DeploymentTimeEntry> spans, int gapMinutes)
		{
			var runs = new List<List<DeploymentTimeEntry>>();
			List<DeploymentTimeEntry> current = null;
			DateTime? lastEnd = null;
			foreach (var span in spans.OrderBy(s => s.StartTime))
			{
				if (current == null || !lastEnd.HasValue || (span.StartTime - lastEnd.Value).TotalMinutes > Math.Max(0, gapMinutes))
				{
					current = new List<DeploymentTimeEntry>();
					runs.Add(current);
				}
				current.Add(span);
				lastEnd = lastEnd.HasValue && lastEnd.Value > span.EndTime ? lastEnd : span.EndTime;
			}
			return runs;
		}

		/// <summary>Walks <paramref name="hours"/> from <paramref name="offset"/> across the Deployment / Overtime1 / Overtime2 thresholds.</summary>
		public static IEnumerable<(RateBandTypes Band, decimal Hours)> Split(decimal hours, decimal offset, decimal? ot1Start, decimal? ot2Start)
		{
			var position = offset;
			var remaining = hours;
			while (remaining > 0)
			{
				RateBandTypes band;
				decimal? boundary;
				if (ot1Start.HasValue && position < ot1Start.Value) { band = RateBandTypes.Deployment; boundary = ot1Start; }
				else if (ot2Start.HasValue && position < ot2Start.Value) { band = ot1Start.HasValue ? RateBandTypes.Overtime1 : RateBandTypes.Deployment; boundary = ot2Start; }
				else { band = ot2Start.HasValue ? RateBandTypes.Overtime2 : ot1Start.HasValue ? RateBandTypes.Overtime1 : RateBandTypes.Deployment; boundary = null; }
				var take = boundary.HasValue ? Math.Min(remaining, boundary.Value - position) : remaining;
				if (take <= 0) take = remaining;
				yield return (band, take);
				position += take;
				remaining -= take;
			}
		}

		private static IEnumerable<ContractorChargeLine> PremiumLines(ContractorChargeInput input, DeploymentTimeReport report, DateTime day, string subjectId, string subjectName, RateScheduleEntry entry, List<ContractorChargeBand> bands)
		{
			var person = input.Personnel?.FirstOrDefault(p => string.Equals(p.DeploymentPersonnelId, subjectId, StringComparison.OrdinalIgnoreCase));
			if (person == null || string.IsNullOrWhiteSpace(person.PremiumIdsJson)) yield break;
			List<string> ids;
			try { ids = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(person.PremiumIdsJson) ?? new List<string>(); }
			catch (Newtonsoft.Json.JsonException) { yield break; }

			foreach (var premium in (input.Schedule.Premiums ?? new List<RatePremium>()).Where(p => p != null && !p.IsDeleted && p.IsActive && ids.Contains(p.RatePremiumId, StringComparer.OrdinalIgnoreCase)))
			{
				var line = new ContractorChargeLine
				{
					Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
					SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name,
					RatePremiumId = premium.RatePremiumId, Kind = ContractorChargeKinds.Premium
				};
				foreach (var band in bands)
				{
					var adder = premium.AdderFor((RateBandTypes)band.BandType);
					if (adder == 0 || band.Hours <= 0) continue;
					line.Bands.Add(new ContractorChargeBand { BandType = band.BandType, Label = band.Label, Hours = band.Hours, Rate = adder, Amount = Money(band.Hours * adder) });
				}
				if (line.Bands.Count == 0) continue;
				line.Quantity = line.Bands.Sum(b => b.Hours);
				line.Amount = line.Bands.Sum(b => b.Amount);
				line.UnitRate = line.Quantity > 0 ? Math.Round(line.Amount / line.Quantity, 4, MidpointRounding.AwayFromZero) : 0;
				line.Description = Describe(day, report, $"{premium.Name} premium", subjectName, $"{line.Quantity:0.##}h" + BandBreakdown(line.Bands));
				yield return line;
			}
		}

		#endregion

		#region Daily / fixed

		private static IEnumerable<ContractorChargeLine> DailyLines(ContractorChargeInput input, DeploymentTimeReport report, DateTime day, bool isCancellationDay, DeploymentTimeSubjectTypes subjectType, string subjectId, string subjectName,
			RateScheduleEntry entry, List<DeploymentTimeEntry> spans, RateSchedulePolicy policy, List<ContractorChargeWarning> warnings)
		{
			var hasDeployment = spans.Any(s => s.EntryType == (int)DeploymentTimeEntryTypes.Deployment);
			if (spans.Count == 0 && !isCancellationDay) yield break;

			// A cancellation day bills the full deployment day for vehicles/equipment when the policy says so; people always do.
			var fullDay = hasDeployment || (isCancellationDay && (subjectType == DeploymentTimeSubjectTypes.Personnel || policy.CancellationVehiclesFullDay));
			var bandType = fullDay ? RateBandTypes.DailyDeployment : RateBandTypes.DailyStandby;
			var hoursWorked = Round(spans.Where(s => s.EntryType != (int)DeploymentTimeEntryTypes.Standby).Sum(s => Math.Max(0m, (decimal)(s.EndTime - s.StartTime).TotalHours - s.UnpaidBreakMinutes / 60m)), policy.RoundingMinutes);

			var candidates = entry.Bands.Where(b => b.BandType == (int)bandType).OrderBy(b => b.DailyTierMinHours ?? 0).ToList();
			var band = candidates.FirstOrDefault(b => (b.DailyTierMinHours.HasValue || b.DailyTierMaxHours.HasValue) && (b.DailyTierMinHours ?? 0) <= hoursWorked && (!b.DailyTierMaxHours.HasValue || hoursWorked <= b.DailyTierMaxHours.Value))
					   ?? candidates.FirstOrDefault(b => !b.DailyTierMinHours.HasValue && !b.DailyTierMaxHours.HasValue)
					   ?? (bandType == RateBandTypes.DailyStandby ? null : candidates.LastOrDefault());
			if (band == null && bandType == RateBandTypes.DailyStandby) { band = entry.Band(RateBandTypes.DailyDeployment); }
			if (band == null)
			{
				warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.BandMissing, Message = $"Entry '{entry.Name}' has no {bandType} rate.", DeploymentTimeReportId = report.DeploymentTimeReportId, SubjectId = subjectId });
				yield break;
			}

			var quantity = 1m;
			if ((BillingBases)entry.BillingBasis == BillingBases.PerPersonPerDay && subjectType == DeploymentTimeSubjectTypes.Unit)
			{
				var crew = spans.Where(s => s.CrewSizeSnapshot.HasValue).Select(s => s.CrewSizeSnapshot.Value).DefaultIfEmpty(0).Max();
				if (crew <= 0) crew = input.Personnel?.Count(p => string.Equals(p.DeploymentUnitId, subjectId, StringComparison.OrdinalIgnoreCase) && p.AddedOn.Date <= day && (!p.RemovedOn.HasValue || p.RemovedOn.Value.Date > day)) ?? 0;
				quantity = Math.Max(1, crew);
			}

			var label = bandType == RateBandTypes.DailyStandby ? "Standby day" : "Deployment day";
			var line = new ContractorChargeLine
			{
				Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
				SubjectType = (int)subjectType, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.Daily,
				Quantity = quantity, UnitRate = band.Rate, Amount = Money(quantity * band.Rate)
			};
			line.Bands.Add(new ContractorChargeBand { BandType = band.BandType, Label = band.Label ?? label, Hours = hoursWorked, Rate = band.Rate, Amount = line.Amount });
			line.Description = Describe(day, report, entry.Name, subjectName, quantity == 1 ? $"{label.ToLowerInvariant()} @ {band.Rate:0.00}" : $"{quantity:0.##} × {label.ToLowerInvariant()} @ {band.Rate:0.00}" + (hoursWorked > 0 ? $" ({hoursWorked:0.##}h worked)" : string.Empty));
			yield return line;
		}

		private static IEnumerable<ContractorChargeLine> FixedLines(DeploymentTimeReport report, DateTime day, bool isCancellationDay, DeploymentTimeSubjectTypes subjectType, string subjectId, string subjectName, RateScheduleEntry entry, List<DeploymentTimeEntry> spans, List<ContractorChargeWarning> warnings)
		{
			if (spans.Count == 0 && !isCancellationDay) yield break;
			var band = entry.Band(RateBandTypes.Deployment) ?? entry.Band(RateBandTypes.DailyDeployment) ?? entry.Band(RateBandTypes.Custom) ?? entry.Bands.FirstOrDefault();
			if (band == null)
			{
				warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.BandMissing, Message = $"Entry '{entry.Name}' has no rate.", DeploymentTimeReportId = report.DeploymentTimeReportId, SubjectId = subjectId });
				yield break;
			}
			yield return new ContractorChargeLine
			{
				Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
				SubjectType = (int)subjectType, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.Fixed,
				Quantity = 1, UnitRate = band.Rate, Amount = Money(band.Rate), Description = Describe(day, report, entry.Name, subjectName, $"fixed @ {band.Rate:0.00}")
			};
		}

		#endregion

		#region Out-of-province, mileage, fuel

		private static IEnumerable<ContractorChargeLine> OutOfProvinceLines(ContractorChargeInput input, DeploymentTimeReport report, DateTime day, string subjectId, string subjectName, RateScheduleEntry entry, List<DeploymentTimeEntry> spans)
		{
			if (!input.Deployment.OutOfProvince) yield break;
			if (!spans.Any(s => s.EntryType == (int)DeploymentTimeEntryTypes.Deployment)) yield break;
			var band = entry.Band(RateBandTypes.OutOfProvincePerPersonDaily)
					   ?? input.Schedule.Entries?.Where(e => e != null && e.IsActive && !e.IsDeleted).OrderBy(e => e.EntryType == (int)RateEntryTypes.Service ? 0 : 1).Select(e => e.Band(RateBandTypes.OutOfProvincePerPersonDaily)).FirstOrDefault(b => b != null);
			if (band == null) yield break;
			if (band.RequiresAirTravel && !input.Deployment.TravelViaAir) yield break;
			yield return new ContractorChargeLine
			{
				Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
				SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.OutOfProvince,
				Quantity = 1, UnitRate = band.Rate, Amount = Money(band.Rate), Taxable = false, Description = Describe(day, report, band.Label ?? "Out-of-province per diem", subjectName, $"1 day @ {band.Rate:0.00}")
			};
		}

		private static IEnumerable<ContractorChargeLine> MileageAndFuelLines(DeploymentTimeReport report, DateTime day, DeploymentTimeSubjectTypes subjectType, string subjectId, string subjectName, RateScheduleEntry entry, List<DeploymentTimeEntry> spans, RateSchedulePolicy policy)
		{
			var km = spans.Where(s => s.MileageKm.HasValue).Sum(s => s.MileageKm.Value);
			var band = entry.Band(RateBandTypes.MileagePerKm);
			if (km > 0 && band != null)
			{
				var billable = Math.Max(0m, km - (band.FreeUnitsPerDay ?? 0m));
				if (billable > 0)
					yield return new ContractorChargeLine
					{
						Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
						SubjectType = (int)subjectType, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.Mileage,
						Quantity = billable, UnitRate = band.Rate, Amount = Money(billable * band.Rate),
						Description = Describe(day, report, entry.Name, subjectName, $"{billable:0.##} km @ {band.Rate:0.00}" + ((band.FreeUnitsPerDay ?? 0) > 0 ? $" ({km:0.##} km − {band.FreeUnitsPerDay:0.##} free)" : string.Empty))
					};
			}

			var litres = spans.Where(s => s.FuelDeductionLitres.HasValue).Sum(s => s.FuelDeductionLitres.Value);
			if (litres > 0 && policy.FuelDeductionRatePerLitre.HasValue && policy.FuelDeductionRatePerLitre.Value > 0)
				yield return new ContractorChargeLine
				{
					Date = day, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
					SubjectType = (int)subjectType, SubjectId = subjectId, SubjectName = subjectName, RateScheduleEntryId = entry.RateScheduleEntryId, EntryName = entry.Name, Kind = ContractorChargeKinds.FuelDeduction,
					Quantity = litres, UnitRate = -policy.FuelDeductionRatePerLitre.Value, Amount = -Money(litres * policy.FuelDeductionRatePerLitre.Value),
					Description = Describe(day, report, "Agency-supplied fuel deduction", subjectName, $"{litres:0.##} L @ −{policy.FuelDeductionRatePerLitre.Value:0.00}")
				};
		}

		#endregion

		#region Expenses

		private static IEnumerable<ContractorChargeLine> ExpenseLines(ContractorChargeInput input, List<DeploymentTimeReport> reports, RateSchedulePolicy policy, List<ContractorChargeWarning> warnings)
		{
			if (reports.Count == 0) yield break;
			var reportIds = new HashSet<string>(reports.Select(r => r.DeploymentTimeReportId), StringComparer.OrdinalIgnoreCase);
			var byDate = reports.GroupBy(r => r.ReportDate.Date).ToDictionary(g => g.Key, g => g.First());
			var perDiemBands = (input.Schedule.Entries ?? new List<RateScheduleEntry>()).Where(e => e != null).SelectMany(e => e.Bands ?? new List<RateScheduleEntryBand>()).Where(b => b.BandType == (int)RateBandTypes.PerDiemMeal && !string.IsNullOrWhiteSpace(b.MealCode)).ToList();

			foreach (var expense in (input.Expenses ?? new List<DeploymentExpense>()).Where(e => e != null && !e.IsDeleted && e.Billable).OrderBy(e => e.ExpenseDate).ThenBy(e => e.AddedOn))
			{
				DeploymentTimeReport report = null;
				if (!string.IsNullOrWhiteSpace(expense.DeploymentTimeReportId))
				{
					if (!reportIds.Contains(expense.DeploymentTimeReportId)) continue;
					report = reports.First(r => string.Equals(r.DeploymentTimeReportId, expense.DeploymentTimeReportId, StringComparison.OrdinalIgnoreCase));
				}
				else if (!byDate.TryGetValue(expense.ExpenseDate.Date, out report)) continue;

				var dayEntries = report.Entries ?? new List<DeploymentTimeEntry>();
				if (expense.ExpenseType == (int)DeploymentExpenseTypes.PerDiemMeal)
				{
					var band = perDiemBands.FirstOrDefault(b => string.Equals(b.MealCode, expense.MealCode, StringComparison.OrdinalIgnoreCase));
					if (band != null && band.Rate != expense.Amount)
						warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.PerDiemMismatch, Message = $"Per diem '{expense.MealCode}' claimed {expense.Amount:0.00}; the schedule rate is {band.Rate:0.00}.", DeploymentTimeReportId = report.DeploymentTimeReportId, DeploymentExpenseId = expense.DeploymentExpenseId });
					if (dayEntries.Any(e => e.AgencySuppliedMeals))
						warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.PerDiemAgencyMeals, Message = $"Per diem '{expense.MealCode}' claimed on a day the agency supplied meals.", DeploymentTimeReportId = report.DeploymentTimeReportId, DeploymentExpenseId = expense.DeploymentExpenseId });
					var window = policy.MealEligibility?.FirstOrDefault(w => string.Equals(w.MealCode, expense.MealCode, StringComparison.OrdinalIgnoreCase));
					if (window != null && !MeetsWindow(window, dayEntries))
						warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.PerDiemIneligible, Message = $"Per diem '{expense.MealCode}' claimed outside its eligibility window.", DeploymentTimeReportId = report.DeploymentTimeReportId, DeploymentExpenseId = expense.DeploymentExpenseId });
				}
				else if (expense.ExpenseType is (int)DeploymentExpenseTypes.Accommodation or (int)DeploymentExpenseTypes.PrivateAccommodation && dayEntries.Any(e => e.AgencySuppliedAccommodation))
				{
					warnings.Add(new ContractorChargeWarning { Code = ContractorChargeWarningCodes.AccommodationAgencySupplied, Message = "Accommodation claimed on a day the agency supplied accommodation.", DeploymentTimeReportId = report.DeploymentTimeReportId, DeploymentExpenseId = expense.DeploymentExpenseId });
				}

				yield return new ContractorChargeLine
				{
					Date = expense.ExpenseDate.Date, DeploymentTimeReportId = report.DeploymentTimeReportId, ReportNumber = report.ReportNumber, IncidentNumber = report.IncidentNumber,
					DeploymentExpenseId = expense.DeploymentExpenseId, Kind = ContractorChargeKinds.Expense, Quantity = 1, UnitRate = expense.Amount, Amount = Money(expense.Amount), Taxable = false,
					Description = Describe(expense.ExpenseDate.Date, report, ExpenseLabel(expense), null, $"{expense.Amount:0.00}" + (string.IsNullOrWhiteSpace(expense.Currency) || string.Equals(expense.Currency, input.Schedule.Currency, StringComparison.OrdinalIgnoreCase) ? string.Empty : $" {expense.Currency}"))
				};
			}
		}

		private static bool MeetsWindow(MealEligibilityWindow window, List<DeploymentTimeEntry> entries)
		{
			if (entries.Count == 0) return false;
			var starts = window.StartsBeforeMinutes.HasValue ? entries.Any(e => e.StartTime.TimeOfDay.TotalMinutes <= window.StartsBeforeMinutes.Value) : true;
			var ends = window.EndsAfterMinutes.HasValue ? entries.Any(e => e.EndTime.TimeOfDay.TotalMinutes >= window.EndsAfterMinutes.Value || e.EndTime.Date > e.StartTime.Date) : true;
			return starts && ends;
		}

		private static string ExpenseLabel(DeploymentExpense expense)
		{
			var type = (DeploymentExpenseTypes)expense.ExpenseType switch
			{
				DeploymentExpenseTypes.PerDiemMeal => "Per diem" + (string.IsNullOrWhiteSpace(expense.MealCode) ? string.Empty : $" ({expense.MealCode})"),
				DeploymentExpenseTypes.Accommodation => "Accommodation",
				DeploymentExpenseTypes.PrivateAccommodation => "Private accommodation",
				DeploymentExpenseTypes.Ferry => "Ferry",
				DeploymentExpenseTypes.Fuel => "Fuel",
				DeploymentExpenseTypes.SupplyRestock => "Supply restock",
				_ => "Expense"
			};
			var description = expense.Description;
			if (string.IsNullOrWhiteSpace(description)) return type;
			return $"{type} — {description.Trim()}";
		}

		#endregion

		#region Helpers

		public static decimal Round(decimal hours, int roundingMinutes)
		{
			if (hours <= 0) return 0;
			if (roundingMinutes <= 0) return Math.Round(hours, 2, MidpointRounding.AwayFromZero);
			var step = roundingMinutes / 60m;
			return Math.Round(hours / step, 0, MidpointRounding.AwayFromZero) * step;
		}

		private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

		private static string BandLabel(RateBandTypes band) => band switch
		{
			RateBandTypes.Deployment => "Deployment",
			RateBandTypes.Overtime1 => "OT1",
			RateBandTypes.Overtime2 => "OT2",
			RateBandTypes.Standby => "Standby",
			_ => band.ToString()
		};

		private static string BandBreakdown(List<ContractorChargeBand> bands)
		{
			if (bands.Count == 0) return string.Empty;
			if (bands.Count == 1) return $" @ {bands[0].Rate:0.00}" + (bands[0].Label == "Deployment" ? string.Empty : $" {bands[0].Label}");
			return " (" + string.Join(", ", bands.Select(b => $"{b.Hours:0.##}h {b.Label} @ {b.Rate:0.00}")) + ")";
		}

		private static string Describe(DateTime day, DeploymentTimeReport report, string entryName, string subjectName, string detail)
		{
			var parts = new List<string> { day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), $"DTR #{report.ReportNumber}" };
			if (!string.IsNullOrWhiteSpace(report.IncidentNumber)) parts.Add($"Incident {report.IncidentNumber}");
			parts.Add(entryName);
			if (!string.IsNullOrWhiteSpace(subjectName)) parts.Add(subjectName);
			parts.Add(detail);
			return string.Join(" — ", parts);
		}

		private static ContractorChargeWarning Warn(string code, string message) => new ContractorChargeWarning { Code = code, Message = message };

		#endregion
	}
}
