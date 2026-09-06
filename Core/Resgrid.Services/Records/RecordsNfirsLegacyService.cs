using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Read-only NFIRS Basic Module rendering and per-incident crosswalk (RMS plan sections 4.3 and 6, RMS-3).
	/// The field set is the same representative set the existing CSV export declares; the values come from
	/// where the department already holds them. Each field names the NERIS fact or section that carries it
	/// going forward and, when the department's NERIS report for the Call exists, whether that report already
	/// has the value — so the page is the crosswalk report, not just a rendering. Nothing is imported,
	/// authored or submitted: NFIRS retired on 2026-01-31.
	/// </summary>
	public class RecordsNfirsLegacyService : IRecordsNfirsLegacyService
	{
		private const string Calls = "Calls";
		private const string UnitStates = "UnitStates";
		private const string IncidentCommand = "IncidentCommand";
		private const string NerisProfile = "NerisProfile";
		private const string Crosswalk = "Crosswalk";

		private readonly ICallsService _calls;
		private readonly IUnitsService _units;
		private readonly IIncidentReportingService _reporting;
		private readonly IIncidentReportsService _incidents;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly INerisProfileService _neris;

		public RecordsNfirsLegacyService(ICallsService calls, IUnitsService units, IIncidentReportingService reporting, IIncidentReportsService incidents,
			IRecordsAuthorizationService authorization, INerisProfileService neris)
		{
			_calls = calls;
			_units = units;
			_reporting = reporting;
			_incidents = incidents;
			_authorization = authorization;
			_neris = neris;
		}

		public async Task<NfirsLegacyRendering> RenderAsync(int departmentId, string viewerUserId, int callId)
		{
			if (callId <= 0)
				return null;

			var call = await _calls.GetCallByIdAsync(callId);
			// CallId is guessable; another department's Call is "not found", never "forbidden".
			if (call == null || call.DepartmentId != departmentId)
				return null;
			if (string.IsNullOrWhiteSpace(viewerUserId) || !await _authorization.CanReadSourceCallAsync(viewerUserId, departmentId, call))
				throw new UnauthorizedAccessException("Source Call access is not authorized.");

			var rendering = new NfirsLegacyRendering
			{
				DepartmentId = departmentId,
				CallId = callId,
				CallNumber = call.Number,
				CallName = call.Name,
				GeneratedOn = DateTime.UtcNow
			};

			var report = await TryGetReportAsync(departmentId, callId, rendering);
			var times = await TryGetTimesAsync(departmentId, callId, rendering);
			var firstOnScene = await TryGetFirstOnSceneAsync(departmentId, callId, rendering);
			var profile = await TryGetProfileAsync(departmentId, rendering);
			var mappedType = await TryResolveTypeAsync(departmentId, call.Type, rendering);

			var f = rendering.Fields;
			Add(f, "A", "FDID", true, profile?.NerisEntityId, NerisProfile, null, "base.reporting_entity", report != null ? !string.IsNullOrWhiteSpace(report.Report.ReportingEntityId) : (bool?)null);
			Add(f, "B", "IncidentDate", true, Utc(call.LoggedOn), Calls, NerisFactKeys.CallCreate, "dispatch", FactPopulated(report, NerisFactKeys.CallCreate));
			Add(f, "B", "Station", false, null, null, null, "unit_responses", report != null ? report.Units.Any(u => u.StationGroupIdSnapshot.HasValue) : (bool?)null, NfirsLegacyFieldStatus.NotCaptured);
			Add(f, "B", "IncidentNumber", true, string.IsNullOrWhiteSpace(call.IncidentNumber) ? call.Number : call.IncidentNumber, Calls, NerisFactKeys.IncidentNumber, "dispatch", FactPopulated(report, NerisFactKeys.IncidentNumber));
			Add(f, "B", "ExposureNumber", false, "000", Calls, null, "exposures", report != null ? report.Exposures.Count > 0 : (bool?)null);
			Add(f, "C", "IncidentTypeCode", true, mappedType == null ? call.Type : call.Type + " → " + mappedType, mappedType == null ? Calls : Crosswalk, NerisFactKeys.IncidentType, "incident_types",
				report != null ? report.Types.Count > 0 : (bool?)null, mappedType == null ? NfirsLegacyFieldStatus.Missing : NfirsLegacyFieldStatus.Populated);
			Add(f, "E1", "AlarmDateTime", true, Utc(call.LoggedOn), Calls, NerisFactKeys.CallCreate, "dispatch", FactPopulated(report, NerisFactKeys.CallCreate));
			Add(f, "E1", "ArrivalDateTime", false, Utc(firstOnScene?.Timestamp), UnitStates, firstOnScene == null ? null : NerisFactKeys.UnitTime(firstOnScene.UnitId, "on_scene"), "unit_responses",
				report != null ? report.Units.Any(u => u.OnSceneOn.HasValue) : (bool?)null);
			Add(f, "E1", "ControlledDateTime", false, Utc(times?.LastBenchmarkCompletedOn), IncidentCommand, NerisFactKeys.CommandLastBenchmark, "dispatch", FactPopulated(report, NerisFactKeys.CommandLastBenchmark));
			Add(f, "E1", "LastUnitClearedDateTime", false, Utc(call.ClosedOn ?? times?.CommandClosedOn), call.ClosedOn.HasValue ? Calls : IncidentCommand, NerisFactKeys.IncidentClear, "dispatch", FactPopulated(report, NerisFactKeys.IncidentClear));
			Add(f, "B", "LocationAddress", true, call.Address, Calls, NerisFactKeys.Location, "base.location", FactPopulated(report, NerisFactKeys.Location));
			Add(f, "B", "IncidentName", false, call.Name, Calls, null, null, null);
			Add(f, "B", "NatureOfCall", false, call.NatureOfCall, Calls, null, "narrative", report != null ? !string.IsNullOrWhiteSpace(report.Narrative?.Narrative) : (bool?)null);
			Add(f, "G", "AidGivenOrReceived", false, times == null ? null : times.MutualAidResourceCount > 0 ? "Received (" + times.MutualAidResourceCount.ToString(CultureInfo.InvariantCulture) + ")" : "None",
				IncidentCommand, NerisFactKeys.CommandMutualAid, "aids", report != null ? report.Aids.Count > 0 : (bool?)null);
			Add(f, "H", "ActionsTaken", false, null, null, null, "action_tactics", report != null ? report.Tactics.Count > 0 : (bool?)null, NfirsLegacyFieldStatus.NotCaptured);
			Add(f, "K", "PropertyUse", false, null, null, null, "base.location.use", report != null ? !string.IsNullOrWhiteSpace(report.Location?.LocationUse) : (bool?)null, NfirsLegacyFieldStatus.NotCaptured);

			Summarize(rendering);
			return rendering;
		}

		#region Sources

		private async Task<IncidentReportAggregate> TryGetReportAsync(int departmentId, int callId, NfirsLegacyRendering rendering)
		{
			try
			{
				var report = await _incidents.GetForCallAsync(departmentId, callId);
				if (report?.Report == null)
					return null;
				rendering.IncidentReportId = report.Report.RmsIncidentReportId;
				rendering.IncidentReportNumber = report.Report.RecordNumber ?? report.Report.DraftReference;
				rendering.IncidentReportState = ((RmsRecordState)report.Report.State).ToString();
				return report;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The NERIS report for call {callId} could not be read for the NFIRS crosswalk.");
				rendering.Notes.Add("The department's NERIS report for this call could not be read; crosswalk coverage is unknown.");
				return null;
			}
		}

		private async Task<IncidentTimesReport> TryGetTimesAsync(int departmentId, int callId, NfirsLegacyRendering rendering)
		{
			try
			{
				return await _reporting.GetIncidentTimesReportAsync(departmentId, callId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Incident Command key times could not be read for call {callId}.");
				rendering.Notes.Add("Incident Command key times could not be read.");
				return null;
			}
		}

		private async Task<UnitState> TryGetFirstOnSceneAsync(int departmentId, int callId, NfirsLegacyRendering rendering)
		{
			try
			{
				var states = await _units.GetUnitStatesForCallAsync(departmentId, callId) ?? new List<UnitState>();
				return states.Where(s => s != null && s.State == (int)UnitStateTypes.OnScene).OrderBy(s => s.Timestamp).FirstOrDefault();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Unit state history could not be read for call {callId}.");
				rendering.Notes.Add("Unit state history could not be read; the arrival time is unknown.");
				return null;
			}
		}

		private async Task<RmsNerisProfile> TryGetProfileAsync(int departmentId, NfirsLegacyRendering rendering)
		{
			try
			{
				return await _neris.GetProfileAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The NERIS profile for department {departmentId} could not be read.");
				rendering.Notes.Add("The department's NERIS profile could not be read.");
				return null;
			}
		}

		private async Task<string> TryResolveTypeAsync(int departmentId, string callType, NfirsLegacyRendering rendering)
		{
			if (string.IsNullOrWhiteSpace(callType))
				return null;
			try
			{
				var mapped = await _neris.ResolveCrosswalkAsync(departmentId, "incident_type", NerisCrosswalkSources.CallType, callType);
				return string.IsNullOrWhiteSpace(mapped) ? null : mapped;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The incident type crosswalk could not be read for department {departmentId}.");
				rendering.Notes.Add("The incident type crosswalk could not be read.");
				return null;
			}
		}

		#endregion

		#region Helpers

		private static bool? FactPopulated(IncidentReportAggregate report, string factKey)
		{
			if (report == null)
				return null;
			var fact = report.Facts?.FirstOrDefault(x => string.Equals(x.FactKey, factKey, StringComparison.Ordinal));
			if (fact == null)
				return false;
			// A correction is authoritative once one was made; until then the imported value stands.
			return !string.IsNullOrWhiteSpace(fact.CurrentValue) || !fact.CorrectedOn.HasValue && !string.IsNullOrWhiteSpace(fact.SourceValue);
		}

		private static void Add(List<NfirsLegacyField> fields, string section, string name, bool required, string value, string sourceSystem, string nerisFactKey, string nerisSection,
			bool? nerisPopulated, NfirsLegacyFieldStatus? status = null)
		{
			var populated = !string.IsNullOrWhiteSpace(value);
			fields.Add(new NfirsLegacyField
			{
				Section = section,
				Code = section + "-" + name,
				Name = name,
				Required = required,
				Value = populated ? value : null,
				Status = status ?? (populated ? NfirsLegacyFieldStatus.Populated : NfirsLegacyFieldStatus.Missing),
				SourceSystem = populated ? sourceSystem : (status == NfirsLegacyFieldStatus.NotCaptured ? null : sourceSystem),
				NerisFactKey = nerisFactKey,
				NerisSection = nerisSection,
				NerisPopulated = nerisPopulated
			});
		}

		private static void Summarize(NfirsLegacyRendering rendering)
		{
			var s = rendering.Summary;
			s.TotalFields = rendering.Fields.Count;
			s.Populated = rendering.Fields.Count(x => x.Status == NfirsLegacyFieldStatus.Populated);
			s.Missing = rendering.Fields.Count(x => x.Status == NfirsLegacyFieldStatus.Missing);
			s.NotCaptured = rendering.Fields.Count(x => x.Status == NfirsLegacyFieldStatus.NotCaptured);
			s.RequiredMissing = rendering.Fields.Count(x => x.Required && x.Status != NfirsLegacyFieldStatus.Populated);
			s.CrosswalkedToNeris = rendering.Fields.Count(x => x.NerisFactKey != null || x.NerisSection != null);
			s.CrosswalkedAndPopulated = rendering.Fields.Count(x => (x.NerisFactKey != null || x.NerisSection != null) && x.NerisPopulated == true);
		}

		private static string Utc(DateTime? value)
		{
			return value.HasValue ? value.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null;
		}

		#endregion
	}
}
