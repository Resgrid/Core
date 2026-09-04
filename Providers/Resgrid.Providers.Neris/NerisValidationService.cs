using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Neris
{
	/// <summary>
	/// Local validation of an incident report against the pinned contract (RMS plan section 5.5): requiredness of
	/// the payload's mandatory fields, value-set membership, identifier shapes, and time sequence. Remote validation
	/// goes through the client's validate endpoint and is folded into the same issue shape. Local rules mirror the
	/// contract's required lists so the common 95% of rejections surface before a submission is queued.
	/// </summary>
	public class NerisValidationService : INerisValidationService
	{
		public static readonly Regex DepartmentIdPattern = new Regex(@"^FD\d{8}$", RegexOptions.Compiled);
		public static readonly Regex UnitIdPattern = new Regex(@"^FD\d{8}S[A-Z\d]{3}U[A-Z\d]{3}$", RegexOptions.Compiled);
		public static readonly Regex AidDepartmentIdPattern = new Regex(@"^(FD|FM)\d{8}$", RegexOptions.Compiled);

		private readonly INerisApiClient _client;
		private readonly INerisProfileService _profiles;

		public NerisValidationService(INerisApiClient client, INerisProfileService profiles)
		{
			_client = client;
			_profiles = profiles;
		}

		public List<RmsValidationIssue> ValidateLocal(NerisIncidentSnapshot snapshot, RmsNerisProfile profile)
		{
			var issues = new List<RmsValidationIssue>();
			if (snapshot?.Report == null)
				return issues;

			var report = snapshot.Report;
			var catalog = NerisValueSetCatalog.Instance;
			void Add(string rule, RmsValidationSeverity severity, string path, string message)
			{
				issues.Add(new RmsValidationIssue
				{
					RmsValidationIssueId = Guid.NewGuid().ToString(),
					DepartmentId = report.DepartmentId,
					RecordId = report.RmsIncidentReportId,
					ProfileVersion = catalog.ContractVersion,
					RuleKey = rule,
					Severity = (int)severity,
					FieldPath = path,
					Message = message,
					Source = (int)RmsValidationSource.Local,
					CreatedOn = DateTime.UtcNow
				});
			}

			// base.*
			if (profile == null || string.IsNullOrWhiteSpace(profile.NerisEntityId))
				Add("neris.profile.entity", RmsValidationSeverity.Error, "base.department_neris_id", "The department has no NERIS entity ID configured.");
			else if (!DepartmentIdPattern.IsMatch(profile.NerisEntityId))
				Add("neris.profile.entity.shape", RmsValidationSeverity.Error, "base.department_neris_id", "The NERIS entity ID must look like FD12345678.");

			if (string.IsNullOrWhiteSpace(report.IncidentNumber))
				Add("neris.base.incident_number", RmsValidationSeverity.Error, "base.incident_number", "An incident number is required.");

			if (snapshot.Location == null || (string.IsNullOrWhiteSpace(snapshot.Location.Street) && string.IsNullOrWhiteSpace(snapshot.Location.AddressText) && snapshot.Location.Latitude == null))
				Add("neris.base.location", RmsValidationSeverity.Error, "base.location", "A location (street, address text, or coordinates) is required.");

			if (snapshot.Location != null)
			{
				if (!string.IsNullOrWhiteSpace(snapshot.Location.State) && !catalog.Contains("state", snapshot.Location.State))
					Add("neris.location.state", RmsValidationSeverity.Error, "base.location.state", $"'{snapshot.Location.State}' is not a NERIS state/territory code.");
				if (!string.IsNullOrWhiteSpace(snapshot.Location.Country) && !catalog.Contains("country", snapshot.Location.Country))
					Add("neris.location.country", RmsValidationSeverity.Error, "base.location.country", $"'{snapshot.Location.Country}' is not a NERIS country code.");
				if (!string.IsNullOrWhiteSpace(snapshot.Location.PlaceType) && !catalog.Contains("location_place", snapshot.Location.PlaceType))
					Add("neris.location.place_type", RmsValidationSeverity.Error, "base.location.place_type", $"'{snapshot.Location.PlaceType}' is not a NERIS place type.");
				if (snapshot.Location.Latitude.HasValue != snapshot.Location.Longitude.HasValue)
					Add("neris.location.point", RmsValidationSeverity.Error, "base.point", "Latitude and longitude must both be present.");
				if (snapshot.Location.Latitude.HasValue && (Math.Abs(snapshot.Location.Latitude.Value) > 90 || Math.Abs(snapshot.Location.Longitude.Value) > 180))
					Add("neris.location.point.range", RmsValidationSeverity.Error, "base.point", "Coordinates are out of range.");
			}

			// incident_types
			if (snapshot.Types.Count == 0)
				Add("neris.incident_types.required", RmsValidationSeverity.Error, "incident_types", "At least one incident type is required.");
			if (snapshot.Types.Count > 0 && snapshot.Types.Count(t => t.IsPrimary) != 1)
				Add("neris.incident_types.primary", RmsValidationSeverity.Error, "incident_types", "Exactly one incident type must be marked primary.");
			foreach (var type in snapshot.Types.Where(t => !catalog.Contains("incident_type", t.TypeCode)))
				Add("neris.incident_types.code", RmsValidationSeverity.Error, "incident_types", $"'{type.TypeCode}' is not a NERIS incident type at contract {catalog.ContractVersion}.");

			foreach (var modifier in snapshot.SpecialModifiers.Where(m => !catalog.Contains("special_modifier", m)))
				Add("neris.special_modifiers.code", RmsValidationSeverity.Error, "special_modifiers", $"'{modifier}' is not a NERIS special modifier.");

			// dispatch.*
			if (!report.CallCreatedOn.HasValue)
				Add("neris.dispatch.call_create", RmsValidationSeverity.Error, "dispatch.call_create", "The call create time is required.");
			if (!report.CallAnsweredOn.HasValue)
				Add("neris.dispatch.call_answered", RmsValidationSeverity.Error, "dispatch.call_answered", "The call answered time is required.");
			if (!report.CallArrivalOn.HasValue)
				Add("neris.dispatch.call_arrival", RmsValidationSeverity.Error, "dispatch.call_arrival", "The first arrival time is required.");
			if (report.CallCreatedOn.HasValue && report.CallArrivalOn.HasValue && report.CallArrivalOn < report.CallCreatedOn)
				Add("neris.dispatch.sequence", RmsValidationSeverity.Error, "dispatch.call_arrival", "First arrival cannot be before the call was created.");
			if (report.CallCreatedOn.HasValue && report.IncidentClearedOn.HasValue && report.IncidentClearedOn < report.CallCreatedOn)
				Add("neris.dispatch.clear_sequence", RmsValidationSeverity.Error, "dispatch.incident_clear", "Incident clear cannot be before the call was created.");

			// unit_responses
			if (snapshot.Units.Count == 0)
				Add("neris.unit_responses.required", RmsValidationSeverity.Warning, "dispatch.unit_responses", "No unit responses are recorded.");
			var index = 0;
			foreach (var unit in snapshot.Units.OrderBy(u => u.Ordinal))
			{
				var path = $"dispatch.unit_responses[{index++}]";
				if (!string.IsNullOrWhiteSpace(unit.UnitNerisId) && !UnitIdPattern.IsMatch(unit.UnitNerisId))
					Add("neris.unit.id.shape", RmsValidationSeverity.Error, path + ".unit_neris_id", $"Unit NERIS ID '{unit.UnitNerisId}' must look like FD12345678S001U003.");
				if (string.IsNullOrWhiteSpace(unit.UnitNerisId) && string.IsNullOrWhiteSpace(unit.UnitNameSnapshot))
					Add("neris.unit.identity", RmsValidationSeverity.Error, path, "A unit response needs a NERIS unit ID or a reported unit ID.");
				if (!string.IsNullOrWhiteSpace(unit.ResponseMode) && !catalog.Contains("response_mode", unit.ResponseMode))
					Add("neris.unit.response_mode", RmsValidationSeverity.Error, path + ".response_mode", $"'{unit.ResponseMode}' is not a NERIS response mode.");
				if (!InOrder(unit.DispatchedOn, unit.EnrouteOn, unit.OnSceneOn, unit.ClearedOn))
					Add("neris.unit.sequence", RmsValidationSeverity.Error, path, $"Unit {unit.UnitNameSnapshot}: dispatch, en route, on scene and clear must be in time order.");
			}

			// aids
			foreach (var aid in snapshot.Aids)
			{
				if (aid.IsNonFireDepartment)
				{
					if (!catalog.Contains("aid_nonfd", aid.NonFdType))
						Add("neris.aid.nonfd", RmsValidationSeverity.Error, "nonfd_aids", $"'{aid.NonFdType}' is not a NERIS non-fire-department aid type.");
					continue;
				}
				if (!catalog.Contains("aid_type", aid.AidType))
					Add("neris.aid.type", RmsValidationSeverity.Error, "aids", $"'{aid.AidType}' is not a NERIS aid type.");
				if (!catalog.Contains("aid_direction", aid.Direction))
					Add("neris.aid.direction", RmsValidationSeverity.Error, "aids", $"'{aid.Direction}' is not a NERIS aid direction.");
				if (string.IsNullOrWhiteSpace(aid.CounterpartNerisId) || !AidDepartmentIdPattern.IsMatch(aid.CounterpartNerisId))
					Add("neris.aid.counterpart", RmsValidationSeverity.Error, "aids", "Each aid entry needs the counterpart's NERIS ID (FD or FM followed by eight digits).");
			}

			foreach (var tactic in snapshot.Tactics.Where(t => !catalog.Contains("action_tactic", t.TacticCode)))
				Add("neris.tactic.code", RmsValidationSeverity.Error, "actions_tactics", $"'{tactic.TacticCode}' is not a NERIS action/tactic.");

			return issues;
		}

		public async Task<List<RmsValidationIssue>> ValidateRemoteAsync(RmsNerisProfile profile, string payloadJson, CancellationToken cancellationToken = default)
		{
			var credential = await _profiles.GetCredentialAsync(profile);
			var outcome = await _client.ValidateAsync(profile, credential, payloadJson, cancellationToken);
			return ToIssues(outcome, profile?.DepartmentId ?? 0, null);
		}

		/// <summary>Destination errors become issues of source Destination; a transient failure is one Warning so the author knows validation did not run.</summary>
		public static List<RmsValidationIssue> ToIssues(NerisSubmissionOutcome outcome, int departmentId, string recordId)
		{
			var now = DateTime.UtcNow;
			var issues = new List<RmsValidationIssue>();
			if (outcome == null)
				return issues;

			if (outcome.Kind == NerisOutcomeKind.Rejected)
			{
				foreach (var error in outcome.Errors)
				{
					issues.Add(new RmsValidationIssue
					{
						RmsValidationIssueId = Guid.NewGuid().ToString(), DepartmentId = departmentId, RecordId = recordId, RuleKey = "neris.destination." + (error.Code ?? "error"),
						Severity = (int)RmsValidationSeverity.Error, FieldPath = error.FieldPath, Message = error.Message, Source = (int)RmsValidationSource.Destination, CreatedOn = now
					});
				}
			}
			else if (outcome.Kind == NerisOutcomeKind.Transient || outcome.Kind == NerisOutcomeKind.Fatal)
			{
				issues.Add(new RmsValidationIssue
				{
					RmsValidationIssueId = Guid.NewGuid().ToString(), DepartmentId = departmentId, RecordId = recordId, RuleKey = "neris.destination.unavailable",
					Severity = (int)RmsValidationSeverity.Warning, FieldPath = null, Message = outcome.Message ?? "NERIS validation could not run.", Source = (int)RmsValidationSource.Destination, CreatedOn = now
				});
			}

			return issues;
		}

		private static bool InOrder(params DateTime?[] times)
		{
			DateTime? previous = null;
			foreach (var time in times)
			{
				if (!time.HasValue)
					continue;
				if (previous.HasValue && time.Value < previous.Value)
					return false;
				previous = time;
			}
			return true;
		}
	}
}
