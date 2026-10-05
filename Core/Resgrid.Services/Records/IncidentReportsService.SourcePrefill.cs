using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Report-source prefill: everything a call's sources hold (<see cref="CallSourceData"/>) lands on the report with its
	/// provenance — unit times and crews from the units' own statuses (or the dispatcher's, or Incident Command's, said so
	/// on the fact), units Incident Command assigned without a dispatch, NERIS tactic timestamps from command objectives,
	/// and mutual aid from the command's resource tracking. A refresh re-reads the sources into a draft without ever
	/// overwriting what the author typed or corrected.
	/// </summary>
	public partial class IncidentReportsService
	{
		private const string TacticTimestampsSystem = "IncidentCommand";

		/// <summary>Counts what a merge changed.</summary>
		private sealed class SourceMergeCounter
		{
			public int Filled;
			public int Updated;
			public int Added;
		}

		#region Start

		/// <summary>The department's incident number: the dispatcher-entered one when the call has it, else the Resgrid call number.</summary>
		private static string PreferredIncidentNumber(Call call)
		{
			var entered = CallSourceDataBuilder.Plain(call?.IncidentNumber);
			if (!string.IsNullOrWhiteSpace(entered))
				return entered;
			return string.IsNullOrWhiteSpace(call?.Number) ? null : call.Number;
		}

		/// <summary>
		/// One unit response per unit the sources place on the call: dispatched, assigned by Incident Command, or with a
		/// status on the call. Without the sources (a read failure) the dispatched units still arrive with their dispatch time.
		/// </summary>
		private async Task<List<RmsUnitResponse>> BuildUnitsFromSourcesAsync(RmsIncidentReport report, Call call, CallSourceData sources, List<RmsSourceFact> facts, DateTime now)
		{
			var result = new List<RmsUnitResponse>();
			var candidates = sources?.Units.Where(IsReportable).ToList()
				?? (call.UnitDispatches ?? new List<CallDispatchUnit>()).Where(d => d != null).GroupBy(d => d.UnitId)
					.Select(g => new CallSourceUnit { UnitId = g.Key, WasDispatched = true, DispatchedOn = g.Min(d => d.DispatchedOn) }).ToList();

			var ordinal = 0;
			foreach (var source in candidates)
			{
				var unit = await _unitsService.GetUnitByIdAsync(source.UnitId);
				if (unit == null || unit.DepartmentId != report.DepartmentId)
					continue;

				var response = NewUnitResponse(report, unit, ordinal++, now);
				MergeUnitSource(report, response, source, facts, now, new SourceMergeCounter());
				result.Add(response);
			}

			return result;
		}

		private static bool IsReportable(CallSourceUnit unit)
		{
			return unit.WasDispatched || unit.AssignedByCommand || unit.EnrouteOn.HasValue || unit.OnSceneOn.HasValue || unit.StagingOn.HasValue || unit.CancelledOn.HasValue;
		}

		/// <summary>True when the report already holds facts for the unit's times or crew, i.e. a row for it was prefilled once.</summary>
		private static bool WasUnitImported(List<RmsSourceFact> facts, int unitId)
		{
			var timePrefix = NerisFactKeys.UnitTime(unitId, string.Empty);
			var staffingKey = IncidentSourceFactKeys.UnitStaffing(unitId);
			return facts.Any(f => f.FactKey.StartsWith(timePrefix, StringComparison.Ordinal) || string.Equals(f.FactKey, staffingKey, StringComparison.Ordinal));
		}

		private static RmsUnitResponse NewUnitResponse(RmsIncidentReport report, Unit unit, int ordinal, DateTime now)
		{
			return new RmsUnitResponse
			{
				RmsUnitResponseId = Guid.NewGuid().ToString(),
				DepartmentId = report.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId,
				UnitId = unit.UnitId,
				UnitNameSnapshot = unit.Name,
				UnitTypeSnapshot = unit.Type,
				StationGroupIdSnapshot = unit.StationGroupId,
				ResponseMode = "EMERGENT",
				TimesSourceKind = (int)RmsSourceKind.None,
				Ordinal = ordinal,
				CreatedOn = now,
				ModifiedOn = now,
				RowVersion = 1
			};
		}

		/// <summary>
		/// The command's tactic timestamps as a NERIS <c>tactic_timestamps</c> section, each with a Derived fact naming the
		/// objective (or the command) it came from. Null when the command named none.
		/// </summary>
		private RmsIncidentModule BuildTacticTimestampsModule(RmsIncidentReport report, CallSourceData sources, List<RmsSourceFact> facts, DateTime now)
		{
			var times = sources?.Command?.TacticTimestamps;
			if (times == null || times.Count == 0)
				return null;

			var body = new JObject();
			foreach (var field in NerisTacticTimestamps.Fields.Where(times.ContainsKey))
			{
				body[field] = Iso(times[field]);
				facts.Add(TacticFact(report, sources, field, times[field], now));
			}

			return NewTacticTimestampsModule(report, body, now);
		}

		private RmsIncidentModule NewTacticTimestampsModule(RmsIncidentReport report, JObject body, DateTime now)
		{
			var descriptor = RmsIncidentModuleCatalog.Get(RmsIncidentModuleKind.TacticTimestamps);
			return new RmsIncidentModule
			{
				RmsIncidentModuleId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(),
				RecordId = report.RmsIncidentReportId, RecordKind = (int)RmsRecordKind.IncidentReport, ModuleKind = (int)RmsIncidentModuleKind.TacticTimestamps,
				SchemaName = descriptor.SchemaName, ProfileVersion = report.ProfileVersion ?? _neris.ContractVersion,
				DetailJson = body.ToString(Newtonsoft.Json.Formatting.None), Ordinal = 0, CreatedOn = now, ModifiedOn = now, RowVersion = 1
			};
		}

		private static RmsSourceFact TacticFact(RmsIncidentReport report, CallSourceData sources, string field, DateTime value, DateTime now)
		{
			var objective = sources.Entries.FirstOrDefault(e => e.Kind == CallSourceEntryKind.Objective && e.TacticTimestamp == field && e.TimestampUtc == value);
			var entityType = objective != null ? "TacticalObjective" : "IncidentCommand";
			var entityId = objective?.Id ?? sources.Command.IncidentCommandId;
			return Fact(report, NerisTacticTimestamps.FactKey(field), RmsSourceKind.Derived, TacticTimestampsSystem, entityType, entityId, Iso(value), value, now);
		}

		/// <summary>
		/// Mutual aid Incident Command tracked: one RECEIVED aid row per agency, the linked department's NERIS id filled in when
		/// it has a NERIS profile. The aid type is the officer's call. Agencies already on the report are skipped.
		/// </summary>
		private async Task<List<RmsAid>> BuildMutualAidAsync(RmsIncidentReport report, CallSourceData sources, List<RmsAid> existing, List<RmsSourceFact> facts, DateTime now)
		{
			var result = new List<RmsAid>();
			var ordinal = existing.Count == 0 ? 0 : existing.Max(a => a.Ordinal) + 1;
			foreach (var aid in sources?.Command?.MutualAid ?? new List<CallSourceMutualAid>())
			{
				string nerisId = null;
				if (aid.LinkedDepartmentId.HasValue)
				{
					try { nerisId = Trim((await _neris.GetProfileAsync(aid.LinkedDepartmentId.Value))?.NerisEntityId)?.ToUpperInvariant(); }
					catch (Exception ex) { Logging.LogException(ex, $"The NERIS id of linked department {aid.LinkedDepartmentId} could not be read for mutual aid prefill."); }
				}

				if (existing.Concat(result).Any(a => string.Equals(Trim(a.CounterpartName), aid.AgencyName, StringComparison.OrdinalIgnoreCase)
					|| (nerisId != null && string.Equals(a.CounterpartNerisId, nerisId, StringComparison.OrdinalIgnoreCase))))
					continue;

				// An agency imported before that is no longer on the report was removed by the author; a refresh leaves it out.
				var key = aid.LinkedDepartmentId.HasValue ? "dept-" + aid.LinkedDepartmentId.Value.ToString(CultureInfo.InvariantCulture) : AgencyKey(aid.AgencyName);
				if (facts.Any(f => f.FactKey == IncidentSourceFactKeys.MutualAid(key)))
					continue;

				result.Add(new RmsAid
				{
					RmsAidId = Guid.NewGuid().ToString(), DepartmentId = report.DepartmentId, ProtectionId = Guid.NewGuid().ToString(), RecordId = report.RmsIncidentReportId,
					Direction = "RECEIVED", AidType = string.Empty, CounterpartNerisId = nerisId, CounterpartName = aid.AgencyName,
					Ordinal = ordinal++, CreatedOn = now, ModifiedOn = now, RowVersion = 1
				});

				var value = aid.ResourceNames.Count == 0 ? aid.AgencyName : aid.AgencyName + " · " + string.Join(", ", aid.ResourceNames);
				facts.Add(Fact(report, IncidentSourceFactKeys.MutualAid(key), RmsSourceKind.Derived, "IncidentCommand", "MutualAid", sources.Command.IncidentCommandId, value, aid.FirstOn, now));
			}

			return result;
		}

		private static string AgencyKey(string agency)
		{
			var chars = (agency ?? string.Empty).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
			var key = new string(chars).Trim('-');
			return key.Length > 64 ? key.Substring(0, 64) : key;
		}

		#endregion

		#region Merge rules

		/// <summary>
		/// Brings one unit response in line with its source. A blank time is filled; a prefilled time the author never
		/// corrected follows the source when the source has a newer value; anything else is the author's and is kept. Each
		/// time carries a fact saying where it came from: the unit's own status (App), a dispatcher's (Dispatch), or one
		/// Resgrid linked, inferred, applied automatically or Incident Command set (Derived).
		/// </summary>
		private static void MergeUnitSource(RmsIncidentReport report, RmsUnitResponse response, CallSourceUnit source, List<RmsSourceFact> facts, DateTime now, SourceMergeCounter counter)
		{
			var unitId = response.UnitId ?? source.UnitId;

			void Time(string field, CallSourceMilestone milestone, DateTime? sourceTime, Func<DateTime?> get, Action<DateTime?> set)
			{
				if (!sourceTime.HasValue)
					return;

				source.TimeEntries.TryGetValue(milestone, out var entry);
				var (kind, system, entityType, entityId) = milestone == CallSourceMilestone.Dispatched
					? (RmsSourceKind.Dispatch, "Calls", "CallDispatchUnit", entry?.Id ?? unitId.ToString(CultureInfo.InvariantCulture))
					: UnitTimeProvenance(entry, source, unitId);
				if (MergeValue(report, facts, NerisFactKeys.UnitTime(unitId, field), Iso(get()), Iso(sourceTime), kind, system, entityType, entityId, sourceTime, now, counter))
					set(sourceTime);
			}

			Time("dispatch", CallSourceMilestone.Dispatched, source.DispatchedOn, () => response.DispatchedOn, v => response.DispatchedOn = v);
			Time("enroute_to_scene", CallSourceMilestone.Enroute, source.EnrouteOn, () => response.EnrouteOn, v => response.EnrouteOn = v);
			Time("on_scene", CallSourceMilestone.OnScene, source.OnSceneOn, () => response.OnSceneOn, v => response.OnSceneOn = v);
			Time("staging", CallSourceMilestone.Staging, source.StagingOn, () => response.StagingOn, v => response.StagingOn = v);
			Time("canceled_enroute", CallSourceMilestone.Cancelled, source.CancelledOn, () => response.CanceledEnrouteOn, v => response.CanceledEnrouteOn = v);
			Time("unit_clear", CallSourceMilestone.Cleared, source.ClearedOn, () => response.ClearedOn, v => response.ClearedOn = v);

			if (source.Staffing.HasValue)
			{
				var current = response.Staffing?.ToString(CultureInfo.InvariantCulture);
				var value = source.Staffing.Value.ToString(CultureInfo.InvariantCulture);
				if (MergeValue(report, facts, IncidentSourceFactKeys.UnitStaffing(unitId), current, value, RmsSourceKind.App, "UnitStateRoles", "Unit", unitId.ToString(CultureInfo.InvariantCulture), null, now, counter))
					response.Staffing = source.Staffing;
			}

			// Same rule the draft save uses: the row is Derived while any uncorrected time rests on a status Resgrid linked,
			// inferred or applied, App otherwise.
			var uncorrected = facts.Where(f => f.FactKey.StartsWith($"unit.{unitId}.", StringComparison.Ordinal) && f.CorrectedOn == null).ToList();
			response.TimesSourceKind = uncorrected.Count == 0 ? (int)RmsSourceKind.None
				: uncorrected.Any(f => f.SourceKind == (int)RmsSourceKind.Derived) ? (int)RmsSourceKind.Derived : (int)RmsSourceKind.App;
		}

		private static string UnitSignature(RmsUnitResponse row)
		{
			return string.Join("|", Iso(row.DispatchedOn), Iso(row.EnrouteOn), Iso(row.OnSceneOn), Iso(row.StagingOn), Iso(row.CanceledEnrouteOn), Iso(row.ClearedOn),
				row.Staffing?.ToString(CultureInfo.InvariantCulture), row.TimesSourceKind.ToString(CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// Where a unit time came from. A status Resgrid tied to the call (auto-linked or inferred) is Derived whoever set
		/// it, because the link itself is the uncertainty; otherwise the setter decides: the unit's crew or app is App, a
		/// dispatcher at the console is Dispatch, Incident Command or an automatic dispatch status is Derived.
		/// </summary>
		public static (RmsSourceKind Kind, string System, string EntityType, string EntityId) UnitTimeProvenance(CallSourceEntry entry, CallSourceUnit unit, int unitId)
		{
			var entityId = unitId.ToString(CultureInfo.InvariantCulture);
			if (entry == null)
				return (RmsSourceKind.App, "UnitStates", "Unit", entityId);

			if (CallStatusAttribution.IsInferred(entry.Linkage))
				return (RmsSourceKind.Derived, "UnitStates (inferred)", "Unit", entityId);
			if (CallStatusAttribution.IsAutoLinked(entry.Linkage))
				return (RmsSourceKind.Derived, "UnitStates (auto-linked)", "Unit", entityId);

			var crew = unit?.CrewUserIds ?? new List<string>();
			var byCrew = !string.IsNullOrWhiteSpace(entry.SetByUserId) && crew.Contains(entry.SetByUserId, StringComparer.OrdinalIgnoreCase);
			switch ((StatusSetOrigins)entry.Origin)
			{
				case StatusSetOrigins.DispatchAutomation:
					return (RmsSourceKind.Derived, "UnitStates (automatic)", "Unit", entityId);
				case StatusSetOrigins.CommandApp:
					return byCrew ? (RmsSourceKind.App, "UnitStates", "Unit", entityId) : (RmsSourceKind.Derived, "UnitStates (incident command)", "Unit", entityId);
				case StatusSetOrigins.DispatchApp:
				case StatusSetOrigins.Web:
				case StatusSetOrigins.BigBoard:
					return byCrew ? (RmsSourceKind.App, "UnitStates", "Unit", entityId) : (RmsSourceKind.Dispatch, "UnitStates (dispatcher)", "Unit", entityId);
				default:
					return (RmsSourceKind.App, "UnitStates", "Unit", entityId);
			}
		}

		/// <summary>
		/// The one merge rule for every prefilled value. Returns true when the field should take <paramref name="sourceValue"/>:
		/// it is blank and the author never corrected it, or it still holds the value its fact imported (the author never
		/// touched it) and the source moved on. The fact is created or re-imported to match; a value the author changed,
		/// including one they cleared, is recorded as a correction, not overwritten.
		/// </summary>
		private static bool MergeValue(RmsIncidentReport report, List<RmsSourceFact> facts, string key, string currentValue, string sourceValue, RmsSourceKind kind,
			string system, string entityType, string entityId, DateTime? sourceTime, DateTime now, SourceMergeCounter counter)
		{
			if (string.IsNullOrWhiteSpace(sourceValue))
				return false;

			var fact = facts.FirstOrDefault(f => string.Equals(f.FactKey, key, StringComparison.Ordinal));
			// A corrected value is the author's, a cleared one included: a blank field here is a deletion, not a gap to fill.
			if (fact != null && fact.CorrectedOn.HasValue)
				return false;

			if (string.IsNullOrWhiteSpace(currentValue))
			{
				Import(report, facts, fact, key, kind, system, entityType, entityId, sourceValue, sourceTime, now);
				counter.Filled++;
				return true;
			}

			if (fact == null)
				return false;

			if (!string.Equals(currentValue, fact.CurrentValue, StringComparison.Ordinal))
			{
				// The author changed the field without the save recording it (a section edited outside the tracked fields).
				fact.CurrentValue = currentValue;
				fact.CorrectedOn = now;
				fact.ModifiedOn = now;
				fact.RowVersion += 1;
				return false;
			}

			if (string.Equals(sourceValue, fact.SourceValue, StringComparison.Ordinal) && fact.SourceKind == (int)kind && string.Equals(fact.SourceSystem, system, StringComparison.Ordinal))
				return false;

			var changed = !string.Equals(sourceValue, currentValue, StringComparison.Ordinal);
			Import(report, facts, fact, key, kind, system, entityType, entityId, sourceValue, sourceTime, now);
			if (changed)
				counter.Updated++;
			return changed;
		}

		private static void Import(RmsIncidentReport report, List<RmsSourceFact> facts, RmsSourceFact fact, string key, RmsSourceKind kind, string system, string entityType, string entityId,
			string value, DateTime? sourceTime, DateTime now)
		{
			if (fact == null)
			{
				facts.Add(Fact(report, key, kind, system, entityType, entityId, value, sourceTime, now));
				return;
			}

			fact.SourceKind = (int)kind;
			fact.SourceSystem = system;
			fact.SourceEntityType = entityType;
			fact.SourceEntityId = entityId;
			fact.SourceValue = value;
			fact.CurrentValue = value;
			fact.SourceTime = sourceTime;
			fact.CorrectedOn = null;
			fact.CorrectedByUserId = null;
			fact.ImportedOn = now;
			fact.ModifiedOn = now;
			fact.RowVersion += 1;
		}

		/// <summary>
		/// A draft save edits the tactic timestamps section as a whole; any field the author changed from what the command
		/// supplied is recorded as a correction on its fact, so a later refresh leaves it alone.
		/// </summary>
		private static void CorrectTacticTimestamps(List<RmsIncidentModule> modules, List<RmsSourceFact> facts, string userId, DateTime now)
		{
			var body = ParseModule(modules?.FirstOrDefault(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps));
			foreach (var field in NerisTacticTimestamps.Fields)
			{
				var value = NormalizeIso(body?[field]?.ToString());
				Correct(facts, NerisTacticTimestamps.FactKey(field), value, userId, now);
			}
		}

		private static JObject ParseModule(RmsIncidentModule module)
		{
			if (string.IsNullOrWhiteSpace(module?.DetailJson))
				return null;
			// Times stay strings: a parsed date would render back in the server culture, not ISO.
			try { return JObject.Load(new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(module.DetailJson)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }); }
			catch (Newtonsoft.Json.JsonException) { return null; }
		}

		/// <summary>An ISO timestamp from a section body in the fact format (UTC, seconds), or the text as given when it is not a time.</summary>
		private static string NormalizeIso(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
				? Iso(DateTime.SpecifyKind(parsed, DateTimeKind.Utc))
				: value.Trim();
		}

		#endregion

		#region Refresh

		public async Task<IncidentReportRefreshResult> RefreshFromSourcesAsync(int departmentId, string userId, string reportId, long expectedRowVersion, RmsOriginClient origin = RmsOriginClient.Web, CancellationToken cancellationToken = default)
		{
			var report = await LoadAsync(departmentId, reportId);
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord)
				|| !await _authorization.CanUserViewRecordAsync(userId, reportId, departmentId)) throw new UnauthorizedAccessException("Incident report access is not authorized.");
			RequireEditable(report);

			var call = await _calls.GetCallByIdAsync(report.CallId);
			if (call == null || call.DepartmentId != departmentId)
				throw new ArgumentException($"Call {report.CallId} does not belong to this department.");
			if (!await _authorization.CanReadSourceCallAsync(userId, departmentId, call))
				throw new UnauthorizedAccessException("Source Call access is not authorized.");
			call = await _calls.PopulateCallData(call, true, false, true, false, true, false, false, false, true) ?? call;

			var sources = await _feeds.GetCallSourceDataAsync(departmentId, call);
			var result = new IncidentReportRefreshResult { Warnings = sources == null ? new List<string> { "sources" } : sources.Warnings.ToList() };
			if (sources == null)
			{
				result.Aggregate = await GetAsync(departmentId, reportId, false);
				return result;
			}

			var counter = new SourceMergeCounter();
			var now = DateTime.UtcNow;
			await InTransactionAsync(async () =>
			{
				await GuardVersionAsync(report, expectedRowVersion, cancellationToken);
				var facts = (await _facts.GetForRecordAsync(departmentId, reportId, null))?.ToList() ?? new List<RmsSourceFact>();
				var modules = (await _modules.GetForRecordAsync(departmentId, reportId, null))?.ToList() ?? new List<RmsIncidentModule>();
				// Merging compares stored values with the sources, so they must be readable: a protected report needs the
				// caller's grant here exactly as a draft save does.
				(await _protection.RevealAsync(departmentId, new IncidentReportAggregate { Report = report, Facts = facts, Modules = modules }, cancellationToken)).RequireRevealed("refresh from sources");
				var factIds = new HashSet<string>(facts.Select(f => f.RmsSourceFactId), StringComparer.Ordinal);
				var factVersions = facts.ToDictionary(f => f.RmsSourceFactId, f => f.RowVersion, StringComparer.Ordinal);

				// Header: the clear time (dispatch close, else command close).
				var clearedBefore = report.IncidentClearedOn;
				if (call.ClosedOn.HasValue)
				{
					if (MergeValue(report, facts, NerisFactKeys.IncidentClear, Iso(report.IncidentClearedOn), Iso(call.ClosedOn), RmsSourceKind.Dispatch, "Calls", "Call",
						call.CallId.ToString(CultureInfo.InvariantCulture), call.ClosedOn, now, counter))
						report.IncidentClearedOn = call.ClosedOn;
				}
				else if (sources.Command?.ClosedOn != null)
				{
					if (MergeValue(report, facts, NerisFactKeys.IncidentClear, Iso(report.IncidentClearedOn), Iso(sources.Command.ClosedOn), RmsSourceKind.Derived, "IncidentCommand", "IncidentCommand",
						sources.Command.IncidentCommandId, sources.Command.ClosedOn, now, counter))
						report.IncidentClearedOn = sources.Command.ClosedOn;
				}

				// Units: every reportable source unit, merged into its row or added.
				var units = (await _units.GetForRecordAsync(departmentId, reportId, null))?.OrderBy(u => u.Ordinal).ToList() ?? new List<RmsUnitResponse>();
				var unitSignatures = units.ToDictionary(u => u.RmsUnitResponseId, UnitSignature, StringComparer.Ordinal);
				var ordinal = units.Count == 0 ? 0 : units.Max(u => u.Ordinal) + 1;
				var addedUnits = new List<RmsUnitResponse>();
				foreach (var source in sources.Units.Where(IsReportable))
				{
					var row = units.FirstOrDefault(u => u.UnitId == source.UnitId);
					if (row == null)
					{
						// A unit whose times or crew were imported before and has no row now was removed by the author.
						if (WasUnitImported(facts, source.UnitId))
							continue;

						var unit = await _unitsService.GetUnitByIdAsync(source.UnitId);
						if (unit == null || unit.DepartmentId != departmentId)
							continue;
						row = NewUnitResponse(report, unit, ordinal++, now);
						addedUnits.Add(row);
						counter.Added++;
					}

					MergeUnitSource(report, row, source, facts, now, counter);
				}

				foreach (var row in units.Where(u => !string.Equals(UnitSignature(u), unitSignatures[u.RmsUnitResponseId], StringComparison.Ordinal)))
				{
					row.ModifiedOn = now;
					row.RowVersion += 1;
					await _units.UpdateAsync(row, cancellationToken, true);
				}
				foreach (var row in addedUnits)
					await _units.InsertAsync(row, cancellationToken, true);

				// Tactic timestamps: fields the command names that the section lacks, or still holds as imported. A missing
				// section goes through the same rule, so fields the author cleared (which removes the section) stay cleared.
				var tactics = modules.FirstOrDefault(m => m.ModuleKind == (int)RmsIncidentModuleKind.TacticTimestamps);
				var commandTimes = sources.Command?.TacticTimestamps ?? new Dictionary<string, DateTime>();
				if (commandTimes.Count > 0)
				{
					var body = ParseModule(tactics) ?? new JObject();
					// A new section counts once as added, not once per field filled.
					var fieldCounter = tactics == null ? new SourceMergeCounter() : counter;
					var changed = false;
					foreach (var field in NerisTacticTimestamps.Fields.Where(commandTimes.ContainsKey))
					{
						var fact = TacticFact(report, sources, field, commandTimes[field], now);
						if (MergeValue(report, facts, fact.FactKey, NormalizeIso(body[field]?.ToString()), fact.SourceValue, RmsSourceKind.Derived, TacticTimestampsSystem,
							fact.SourceEntityType, fact.SourceEntityId, fact.SourceTime, now, fieldCounter))
						{
							body[field] = fact.SourceValue;
							changed = true;
						}
					}

					if (changed && tactics == null)
					{
						var created = NewTacticTimestampsModule(report, body, now);
						created.Ordinal = modules.Count == 0 ? 0 : modules.Max(m => m.Ordinal) + 1;
						await _protection.ProtectModuleAsync(departmentId, created, null, userId, cancellationToken);
						await _modules.InsertAsync(created, cancellationToken, true);
						counter.Added++;
					}
					else if (changed)
					{
						var previous = Copy(tactics, _ => { }, tactics.RevisionId, tactics.ModifiedOn);
						tactics.DetailJson = body.ToString(Newtonsoft.Json.Formatting.None);
						tactics.ModifiedOn = now;
						tactics.RowVersion += 1;
						await _protection.ProtectModuleAsync(departmentId, tactics, previous, userId, cancellationToken);
						await _modules.UpdateAsync(tactics, cancellationToken, true);
					}
				}

				// Mutual aid agencies not yet on the report.
				var aids = (await _aids.GetForRecordAsync(departmentId, reportId, null))?.ToList() ?? new List<RmsAid>();
				foreach (var aid in await BuildMutualAidAsync(report, sources, aids, facts, now))
				{
					await _aids.InsertAsync(aid, cancellationToken, true);
					counter.Added++;
				}

				foreach (var fact in facts)
				{
					if (!factIds.Contains(fact.RmsSourceFactId))
					{
						await _protection.ProtectSourceFactAsync(departmentId, fact, null, userId, cancellationToken);
						await _facts.InsertAsync(fact, cancellationToken, true);
					}
					else if (fact.RowVersion != factVersions[fact.RmsSourceFactId])
					{
						await _protection.ProtectSourceFactAsync(departmentId, fact, null, userId, cancellationToken);
						await _facts.UpdateAsync(fact, cancellationToken, true);
					}
				}

				report.ModifiedOn = now;
				report.ModifiedByUserId = userId;
				await _reports.UpdateAsync(report, cancellationToken, true);

				var aggregate = await HydrateAsync(report, null, false);
				await RecomputeGroupScopeAsync(aggregate, null, cancellationToken);
				await UpsertProjectionAsync(aggregate, cancellationToken);
				await AuditAsync(departmentId, userId, reportId, null, RmsAccessAuditAction.Change, "Refresh from call sources", origin, cancellationToken,
					new { filled = counter.Filled, updated = counter.Updated, added = counter.Added, clearedChanged = clearedBefore != report.IncidentClearedOn });
			});

			result.FilledCount = counter.Filled;
			result.UpdatedCount = counter.Updated;
			result.AddedCount = counter.Added;
			result.Aggregate = await GetAsync(departmentId, reportId, false);
			return result;
		}

		#endregion
	}
}
