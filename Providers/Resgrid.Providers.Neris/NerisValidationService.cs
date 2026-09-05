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
				// Both coordinates, or the range check dereferences the missing one and the whole validation run fails
				// with an exception instead of reporting the paired-coordinate error above.
				if (snapshot.Location.Latitude.HasValue && snapshot.Location.Longitude.HasValue
					&& (Math.Abs(snapshot.Location.Latitude.Value) > 90 || Math.Abs(snapshot.Location.Longitude.Value) > 180))
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
				Add("neris.dispatch.call_arrival", RmsValidationSeverity.Error, "dispatch.call_arrival", "The call's arrival time at the dispatch center is required.");
			if (!InOrder(report.CallArrivalOn, report.CallAnsweredOn, report.CallCreatedOn))
				Add("neris.dispatch.sequence", RmsValidationSeverity.Error, "dispatch.call_arrival", "Call arrival at dispatch, call answered, and call creation must be in time order. Unit arrival is recorded separately.");
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

			// Non-unit resources are a department record: contract 1.4.78 has no incident-level resources field, so
			// they are never submitted and there is no NERIS value set to check them against.
			foreach (var resource in snapshot.Resources.Where(r => string.IsNullOrWhiteSpace(r.ResourceCode)))
				Add("neris.resource.code", RmsValidationSeverity.Error, "resources", "A resource entry needs a code.");

			ValidateSections(Add, snapshot, catalog);
			ValidateExposures(Add, snapshot, catalog);
			ValidateCasualties(Add, snapshot, catalog);
			if (profile != null)
			{
				var payload = new NerisMappingService().BuildIncidentPayloadJson(snapshot, profile);
				var contractIssues = NerisContractCatalog.Instance.Validate("IncidentPayload", payload, report.DepartmentId, report.RmsIncidentReportId);
				issues.AddRange(contractIssues);
				if (contractIssues.Count == 0) NerisPayloadRules.Validate(Newtonsoft.Json.Linq.JObject.Parse(payload), false, (path, message) => Add("neris.contract.condition", RmsValidationSeverity.Error, path, message));
			}

			return issues;
		}

		/// <summary>
		/// Progressive section rules (RMS-3): the sections the selected incident types demand must be present and
		/// coherent, and every section that is present must carry codes the pinned contract knows.
		/// </summary>
		private static void ValidateSections(Action<string, RmsValidationSeverity, string, string> add, NerisIncidentSnapshot snapshot, NerisValueSetCatalog catalog)
		{
			var present = snapshot.Modules.Select(m => (RmsIncidentModuleKind)m.ModuleKind).Distinct().ToList();

			var fdAids = snapshot.Aids.Where(a => !a.IsNonFireDepartment).ToList();
			var supportOnly = fdAids.Count > 0 && fdAids.All(a => a.AidType == "SUPPORT_AID" && a.Direction == "GIVEN");
			foreach (var requirement in NerisSectionRules.For(snapshot.Types.Select(t => t.TypeCode), supportOnly))
			{
				if (present.Contains(requirement.Kind))
					continue;

				var descriptor = RmsIncidentModuleCatalog.Get(requirement.Kind);
				add(
					"neris.section." + requirement.Kind.ToString().ToLowerInvariant(),
					requirement.Required ? RmsValidationSeverity.Error : RmsValidationSeverity.Warning,
					descriptor?.PayloadPath ?? requirement.Kind.ToString(),
					$"The {requirement.Kind} section is missing. {requirement.Reason}");
			}

			foreach (var kind in present)
			{
				foreach (var other in present.Where(o => NerisSectionRules.Conflicts(kind, o)))
				{
					add("neris.section.conflict", RmsValidationSeverity.Error, "fire_detail.location_detail",
						$"A report cannot carry both the {kind} and {other} sections; the fire was either in a structure or outside it.");
					break;
				}
			}

			foreach (var module in snapshot.Modules)
			{
				var kind = (RmsIncidentModuleKind)module.ModuleKind;
				var descriptor = RmsIncidentModuleCatalog.Get(kind);
				if (descriptor == null)
				{
					add("neris.section.unknown", RmsValidationSeverity.Error, kind.ToString(), $"'{kind}' is not a section of contract {catalog.ContractVersion}.");
					continue;
				}

				var path = descriptor.PayloadPath;

				if (!descriptor.IsCollection && snapshot.Modules.Count(m => m.ModuleKind == module.ModuleKind) > 1)
					add("neris.section.cardinality", RmsValidationSeverity.Error, path, $"The {kind} section may appear only once.");

				if (!string.IsNullOrWhiteSpace(module.DetailJson) && !ParsesAsObject(module.DetailJson))
					add("neris.section.detail", RmsValidationSeverity.Error, path, $"The {kind} section body is not a JSON object and cannot be submitted.");

				var primarySet = NerisSectionRules.PrimaryCodeSetFor(kind);
				if (primarySet != null && !string.IsNullOrWhiteSpace(module.PrimaryCode) && !catalog.Contains(primarySet, module.PrimaryCode))
					add("neris.section.primary_code", RmsValidationSeverity.Error, path, $"'{module.PrimaryCode}' is not a NERIS {primarySet} value.");

				var secondarySet = NerisSectionRules.SecondaryCodeSetFor(kind);
				if (secondarySet != null && !string.IsNullOrWhiteSpace(module.SecondaryCode) && !catalog.Contains(secondarySet, module.SecondaryCode))
					add("neris.section.secondary_code", RmsValidationSeverity.Error, path, $"'{module.SecondaryCode}' is not a NERIS {secondarySet} value.");

				if (!string.IsNullOrWhiteSpace(module.QuantityUnit) && !catalog.Contains("hazard_unit", module.QuantityUnit))
					add("neris.section.quantity_unit", RmsValidationSeverity.Error, path, $"'{module.QuantityUnit}' is not a NERIS unit of measure.");
			}
		}

		private static void ValidateExposures(Action<string, RmsValidationSeverity, string, string> add, NerisIncidentSnapshot snapshot, NerisValueSetCatalog catalog)
		{
			var index = 0;
			foreach (var exposure in snapshot.Exposures.OrderBy(e => e.Ordinal))
			{
				var path = $"exposures[{index++}]";

				// damage_type is required by the contract; the rest are checked only when supplied.
				if (string.IsNullOrWhiteSpace(exposure.DamageType))
					add("neris.exposure.damage_type", RmsValidationSeverity.Error, path + ".damage_type", "Each exposure needs a damage rating.");
				else if (!catalog.Contains("exposure_damage", exposure.DamageType))
					add("neris.exposure.damage_type.code", RmsValidationSeverity.Error, path + ".damage_type", $"'{exposure.DamageType}' is not a NERIS exposure damage rating.");

				if (!string.IsNullOrWhiteSpace(exposure.ItemType) && !catalog.Contains("exposure_item", exposure.ItemType))
					add("neris.exposure.item_type", RmsValidationSeverity.Error, path + ".location_detail", $"'{exposure.ItemType}' is not a NERIS exposure item type.");

				if (!string.IsNullOrWhiteSpace(exposure.LocationUse) && !catalog.Contains("location_use", exposure.LocationUse))
					add("neris.exposure.location_use", RmsValidationSeverity.Error, path + ".location_use", $"'{exposure.LocationUse}' is not a NERIS location use.");

				foreach (var cause in Split(exposure.DisplacementCausesCsv).Where(c => !catalog.Contains("displace_cause", c)))
					add("neris.exposure.displace_cause", RmsValidationSeverity.Error, path + ".displacement_causes", $"'{cause}' is not a NERIS displacement cause.");

				if (exposure.Latitude.HasValue != exposure.Longitude.HasValue)
					add("neris.exposure.point", RmsValidationSeverity.Error, path + ".point", "Latitude and longitude must both be present.");
			}
		}

		private static void ValidateCasualties(Action<string, RmsValidationSeverity, string, string> add, NerisIncidentSnapshot snapshot, NerisValueSetCatalog catalog)
		{
			var index = 0;
			foreach (var casualty in snapshot.Casualties.OrderBy(c => c.Ordinal))
			{
				var path = $"casualty_rescues[{index++}]";

				if (casualty.PersonType != RmsCasualtyPersonTypes.Firefighter && casualty.PersonType != RmsCasualtyPersonTypes.Civilian)
					add("neris.casualty.person_type", RmsValidationSeverity.Error, path + ".type", "Each casualty or rescue must say whether the person was a firefighter (FF) or not (NONFF).");

				if (!string.IsNullOrWhiteSpace(casualty.Gender) && !catalog.Contains("gender", casualty.Gender))
					add("neris.casualty.gender", RmsValidationSeverity.Error, path + ".gender", $"'{casualty.Gender}' is not a NERIS gender value.");

				if (!string.IsNullOrWhiteSpace(casualty.Race) && !catalog.Contains("race", casualty.Race))
					add("neris.casualty.race", RmsValidationSeverity.Error, path + ".race", $"'{casualty.Race}' is not a NERIS race value.");

				if (!string.IsNullOrWhiteSpace(casualty.BirthMonthYear) && !BirthMonthYearPattern.IsMatch(casualty.BirthMonthYear))
					add("neris.casualty.birth_month_year", RmsValidationSeverity.Error, path + ".birth_month_year", "Birth month and year must be written as YYYY-MM.");

				if ((RmsCasualtyRescueKind)casualty.Kind == RmsCasualtyRescueKind.Casualty)
				{
					if (!string.IsNullOrWhiteSpace(casualty.CasualtyCause) && !catalog.Contains("casualty_cause", casualty.CasualtyCause))
						add("neris.casualty.cause", RmsValidationSeverity.Error, path + ".casualty", $"'{casualty.CasualtyCause}' is not a NERIS casualty cause.");
					if (!string.IsNullOrWhiteSpace(casualty.CasualtyAction) && !catalog.Contains("casualty_action", casualty.CasualtyAction))
						add("neris.casualty.action", RmsValidationSeverity.Error, path + ".casualty", $"'{casualty.CasualtyAction}' is not a NERIS casualty action.");
					if (!string.IsNullOrWhiteSpace(casualty.CasualtyTimeline) && !catalog.Contains("casualty_timeline", casualty.CasualtyTimeline))
						add("neris.casualty.timeline", RmsValidationSeverity.Error, path + ".casualty", $"'{casualty.CasualtyTimeline}' is not a NERIS casualty timeline.");
					if (!string.IsNullOrWhiteSpace(casualty.DutyType) && !catalog.Contains("duty", casualty.DutyType))
						add("neris.casualty.duty", RmsValidationSeverity.Error, path + ".casualty", $"'{casualty.DutyType}' is not a NERIS duty type.");
					foreach (var ppe in Split(casualty.PpeCsv).Where(p => !catalog.Contains("casualty_ppe", p)))
						add("neris.casualty.ppe", RmsValidationSeverity.Error, path + ".casualty", $"'{ppe}' is not a NERIS PPE item.");

					// A responder casualty without duty context cannot be analysed; the contract accepts it, the fire service should not.
					if (casualty.PersonType == RmsCasualtyPersonTypes.Firefighter && string.IsNullOrWhiteSpace(casualty.DutyType))
						add("neris.casualty.ff_duty", RmsValidationSeverity.Warning, path + ".casualty", "A firefighter casualty should record what the member was doing at the time.");
				}
				else
				{
					if (string.IsNullOrWhiteSpace(casualty.RescueType))
						add("neris.rescue.type", RmsValidationSeverity.Error, path + ".rescue", "Each rescue needs a rescue type.");
					foreach (var action in Split(casualty.RescueActionsCsv).Where(a => !catalog.Contains("rescue_action", a)))
						add("neris.rescue.action", RmsValidationSeverity.Error, path + ".rescue", $"'{action}' is not a NERIS rescue action.");
					foreach (var impediment in Split(casualty.RescueImpedimentsCsv).Where(i => !catalog.Contains("rescue_impediment", i)))
						add("neris.rescue.impediment", RmsValidationSeverity.Error, path + ".rescue", $"'{impediment}' is not a NERIS rescue impediment.");
					if (!string.IsNullOrWhiteSpace(casualty.RescueMode) && !catalog.Contains("rescue_mode", casualty.RescueMode))
						add("neris.rescue.mode", RmsValidationSeverity.Error, path + ".rescue", $"'{casualty.RescueMode}' is not a NERIS rescue mode.");
					if (!string.IsNullOrWhiteSpace(casualty.RescuePath) && !catalog.Contains("rescue_path", casualty.RescuePath))
						add("neris.rescue.path", RmsValidationSeverity.Error, path + ".rescue", $"'{casualty.RescuePath}' is not a NERIS rescue path.");
					if (!string.IsNullOrWhiteSpace(casualty.RescueElevation) && !catalog.Contains("rescue_elevation", casualty.RescueElevation))
						add("neris.rescue.elevation", RmsValidationSeverity.Error, path + ".rescue", $"'{casualty.RescueElevation}' is not a NERIS rescue elevation.");
				}
			}
		}

		/// <summary>
		/// Local validation of the separate incident-analysis filing (RMS-3). It is checked on its own because it
		/// is submitted on its own; an analysis problem must never block the incident it belongs to.
		/// </summary>
		public List<RmsValidationIssue> ValidateAnalysisLocal(NerisIncidentAnalysisSnapshot snapshot, RmsNerisProfile profile)
		{
			var issues = new List<RmsValidationIssue>();
			if (snapshot?.Analysis == null)
				return issues;

			var analysis = snapshot.Analysis;
			var catalog = NerisValueSetCatalog.Instance;
			void Add(string rule, RmsValidationSeverity severity, string path, string message)
			{
				issues.Add(new RmsValidationIssue
				{
					RmsValidationIssueId = Guid.NewGuid().ToString(),
					DepartmentId = analysis.DepartmentId,
					RecordId = analysis.RmsIncidentAnalysisId,
					ProfileVersion = catalog.ContractVersion,
					RuleKey = rule,
					Severity = (int)severity,
					FieldPath = path,
					Message = message,
					Source = (int)RmsValidationSource.Local,
					CreatedOn = DateTime.UtcNow
				});
			}

			if (profile == null || string.IsNullOrWhiteSpace(profile.NerisEntityId))
				Add("neris.profile.entity", RmsValidationSeverity.Error, "base.department_neris_id", "The department has no NERIS entity ID configured.");

			if (string.IsNullOrWhiteSpace(snapshot.Report?.NerisIncidentId))
				Add("neris.analysis.incident", RmsValidationSeverity.Error, "base.neris_id_incident", "The incident must be filed with NERIS before its analysis can be.");

			if (!string.IsNullOrWhiteSpace(analysis.GeneralCause) && !catalog.Contains("fire_cause_general", analysis.GeneralCause))
				Add("neris.analysis.general_cause", RmsValidationSeverity.Error, "base.general_cause", $"'{analysis.GeneralCause}' is not a NERIS general fire cause.");

			foreach (var investigation in Split(analysis.InvestigationTypesCsv).Where(i => !catalog.Contains("fire_invest", i)))
				Add("neris.analysis.investigation", RmsValidationSeverity.Error, "base.investigation_types", $"'{investigation}' is not a NERIS investigation type.");

			var index = 0;
			foreach (var property in snapshot.Properties.OrderBy(p => p.Ordinal))
			{
				var path = $"properties[{index++}]";
				if (!string.IsNullOrWhiteSpace(property.LocationUse) && !catalog.Contains("location_use", property.LocationUse))
					Add("neris.analysis.property.location_use", RmsValidationSeverity.Error, path + ".location_use", $"'{property.LocationUse}' is not a NERIS location use.");
				if (!string.IsNullOrWhiteSpace(property.ConstructionType) && !catalog.Contains("construction", property.ConstructionType))
					Add("neris.analysis.property.construction", RmsValidationSeverity.Error, path + ".construction_type", $"'{property.ConstructionType}' is not a NERIS construction type.");
				if (!string.IsNullOrWhiteSpace(property.DamageType) && !catalog.Contains("fire_bldg_damage", property.DamageType))
					Add("neris.analysis.property.damage", RmsValidationSeverity.Error, path + ".damage_type", $"'{property.DamageType}' is not a NERIS building damage rating.");
				if (!string.IsNullOrWhiteSpace(property.FireSpread) && !catalog.Contains("fire_spread", property.FireSpread))
					Add("neris.analysis.property.fire_spread", RmsValidationSeverity.Error, path + ".fire_spread", $"'{property.FireSpread}' is not a NERIS fire spread value.");
				if (property.EstimatedLoss.HasValue && property.EstimatedValue.HasValue && property.EstimatedLoss > property.EstimatedValue)
					Add("neris.analysis.property.loss", RmsValidationSeverity.Warning, path + ".estimated_loss", "The reported loss is greater than the reported pre-incident value.");
			}

			index = 0;
			foreach (var vehicle in snapshot.Vehicles.OrderBy(v => v.Ordinal))
			{
				var path = $"vehicles[{index++}]";
				if (!string.IsNullOrWhiteSpace(vehicle.Make) && !catalog.Contains("auto_make", vehicle.Make))
					Add("neris.analysis.vehicle.make", RmsValidationSeverity.Error, path + ".make", $"'{vehicle.Make}' is not a NERIS vehicle make.");
				if (!string.IsNullOrWhiteSpace(vehicle.BodyStyle) && !catalog.Contains("auto_body_style", vehicle.BodyStyle))
					Add("neris.analysis.vehicle.body_style", RmsValidationSeverity.Error, path + ".body_style", $"'{vehicle.BodyStyle}' is not a NERIS body style.");
				if (!string.IsNullOrWhiteSpace(vehicle.Powertrain) && !catalog.Contains("powertrain", vehicle.Powertrain))
					Add("neris.analysis.vehicle.powertrain", RmsValidationSeverity.Error, path + ".powertrain", $"'{vehicle.Powertrain}' is not a NERIS powertrain.");
				if (!string.IsNullOrWhiteSpace(vehicle.DamageType) && !catalog.Contains("vehicle_damage", vehicle.DamageType))
					Add("neris.analysis.vehicle.damage", RmsValidationSeverity.Error, path + ".damage_type", $"'{vehicle.DamageType}' is not a NERIS vehicle damage rating.");
			}

			foreach (var module in snapshot.Modules)
			{
				var kind = (RmsIncidentModuleKind)module.ModuleKind;
				var descriptor = RmsIncidentModuleCatalog.Get(kind);
				if (descriptor == null || !descriptor.BelongsToAnalysis)
				{
					Add("neris.analysis.section.unknown", RmsValidationSeverity.Error, kind.ToString(), $"'{kind}' is not a section of the incident analysis.");
					continue;
				}

				if (!string.IsNullOrWhiteSpace(module.DetailJson) && !ParsesAsObject(module.DetailJson))
					Add("neris.analysis.section.detail", RmsValidationSeverity.Error, descriptor.PayloadPath, $"The {kind} section body is not a JSON object and cannot be submitted.");

				var primarySet = NerisSectionRules.PrimaryCodeSetFor(kind);
				if (primarySet != null && !string.IsNullOrWhiteSpace(module.PrimaryCode) && !catalog.Contains(primarySet, module.PrimaryCode))
					Add("neris.analysis.section.primary_code", RmsValidationSeverity.Error, descriptor.PayloadPath, $"'{module.PrimaryCode}' is not a NERIS {primarySet} value.");
			}

			if (profile != null)
			{
				var payload = new NerisMappingService().BuildIncidentAnalysisPayloadJson(snapshot, profile);
				var contractIssues = NerisContractCatalog.Instance.Validate("IncidentAnalysisPayload", payload, analysis.DepartmentId, analysis.RmsIncidentAnalysisId, string.IsNullOrWhiteSpace(snapshot.Report?.NerisIncidentId));
				issues.AddRange(contractIssues);
				if (contractIssues.Count == 0) NerisPayloadRules.Validate(Newtonsoft.Json.Linq.JObject.Parse(payload), true, (path, message) => Add("neris.contract.condition", RmsValidationSeverity.Error, path, message));
			}
			return issues;
		}

		private static readonly Regex BirthMonthYearPattern = new Regex(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

		private static IEnumerable<string> Split(string csv)
		{
			if (string.IsNullOrWhiteSpace(csv))
				return Enumerable.Empty<string>();

			return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0);
		}

		private static bool ParsesAsObject(string json)
		{
			try
			{
				Newtonsoft.Json.Linq.JObject.Parse(json);
				return true;
			}
			catch (Newtonsoft.Json.JsonReaderException)
			{
				return false;
			}
		}

		public IReadOnlyList<NerisSectionRequirement> GetSectionRequirements(IEnumerable<string> incidentTypeCodes)
		{
			return NerisSectionRules.For(incidentTypeCodes)
				.Select(r => new NerisSectionRequirement
				{
					Kind = r.Kind, Required = r.Required, Reason = r.Reason,
					PrimaryCodeSet = NerisSectionRules.PrimaryCodeSetFor(r.Kind), SecondaryCodeSet = NerisSectionRules.SecondaryCodeSetFor(r.Kind)
				})
				.ToList();
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
						RmsValidationIssueId = Guid.NewGuid().ToString(), DepartmentId = departmentId, RecordId = recordId,
						RuleKey = outcome.LocalValidationFailure ? error.Code ?? "neris.local.error" : "neris.destination." + (error.Code ?? "error"),
						Severity = (int)RmsValidationSeverity.Error, FieldPath = error.FieldPath, Message = error.Message,
						Source = (int)(outcome.LocalValidationFailure ? RmsValidationSource.Local : RmsValidationSource.Destination), CreatedOn = now
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
