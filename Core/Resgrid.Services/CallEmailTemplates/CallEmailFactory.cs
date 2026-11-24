using System;
using System.Collections.Generic;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Identity;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.Framework;

namespace Resgrid.Services.CallEmailTemplates
{
	public class CallEmailFactory : ICallEmailFactory
	{
		private Dictionary<int, ICallEmailTemplate> _templates;

		public CallEmailFactory()
		{
			_templates = new Dictionary<int, ICallEmailTemplate>();
			_templates.Add((int)CallEmailTypes.CalFireECC, new CalFireEccTemplate());
			_templates.Add((int)CallEmailTypes.CarencroFire, new CarencroFireTemplate());
			_templates.Add((int)CallEmailTypes.Resgrid, new ResgridEmailTemplate());
			_templates.Add((int)CallEmailTypes.GrandBlanc, new GrandBlancTemplate());
			_templates.Add((int)CallEmailTypes.Generic, new GenericTemplate());
			_templates.Add((int)CallEmailTypes.LowestoftCoastGuard, new LowestoftCoastGuardTemplate());
			_templates.Add((int)CallEmailTypes.UnionFire, new UnionFireTemplate());
			_templates.Add((int)CallEmailTypes.ParklandCounty, new ParklandCountyTemplate());
			_templates.Add((int)CallEmailTypes.GenericPage, new GenericPageTemplate());
			_templates.Add((int)CallEmailTypes.Brockport, new BrockportTemplate());
			_templates.Add((int)CallEmailTypes.HancockCounty, new HancockTemplate());
			_templates.Add((int)CallEmailTypes.CalFireSCU, new CalFireSCUTemplate());
			_templates.Add((int)CallEmailTypes.Connect, new ConnectTemplate());
			_templates.Add((int)CallEmailTypes.SpottedDog, new SpottedDogTemplate());
			_templates.Add((int)CallEmailTypes.PortJervis, new PortJervisTemplate());
			_templates.Add((int)CallEmailTypes.Yellowhead, new YellowHeadTemplate());
			_templates.Add((int)CallEmailTypes.ParklandCounty2, new ParklandCounty2Template());
			_templates.Add((int)CallEmailTypes.FourPartPipe, new FourPartPipeTemplate());
			_templates.Add((int)CallEmailTypes.RandR, new RandRTemplate());
			_templates.Add((int)CallEmailTypes.Active911, new Active911lTemplate());
			_templates.Add((int)CallEmailTypes.OttawaCounty, new OttawaCountyTemplate());
			_templates.Add((int)CallEmailTypes.OttawaKingstonToronto, new OttawaKingstonTorontoTemplate());
			// AI dispatch, Enrich mode: the call is exactly what GenericTemplate builds and is dispatched as today; the
			// aidispatchtriage worker enriches it afterwards. A lapsed entitlement therefore still produces a normal call.
			_templates.Add((int)CallEmailTypes.AI, new GenericTemplate());
			// "Key: Value" CAD emails with department-mapped labels (cad-email-call-updates-plan.md Phase 2).
			_templates.Add((int)CallEmailTypes.LabeledFields, new LabeledFieldsTemplate());
		}

		public async Task<CallEmailParseResult> ParseCallEmailAsync(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users,
			Department department, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities, List<CallType> callTypes,
			IGeoLocationProvider geolocationProvider, EmailLabeledFieldsConfig labeledFields)
		{
			if (!_templates.TryGetValue((int)type, out var template))
				return null;

			try
			{
				CallEmailParseResult result;
				if (template is ICallEmailParser parser)
				{
					result = await parser.ParseAsync(email, managingUser, users, department, units, priority, activePriorities, callTypes, geolocationProvider, labeledFields);
				}
				else
				{
					// Templates no longer match calls; an empty list makes sure none ever hands back an existing call.
					var call = await template.GenerateCall(email, managingUser, users, department, new List<Call>(), units, priority, activePriorities, callTypes, geolocationProvider);
					result = Describe(call, priority);
				}

				if (result?.Call == null)
					return result;

				EnsureRequiredValues(result.Call, email);

				if (template is ICallEmailMatchKeyTemplate keyed)
				{
					var parsedCall = result.Call;
					result.MatchKey = keyed.GetMatchKey(parsedCall);
					result.MatchKeyIsComposite = true;
					result.LegacyMatch = existing => keyed.IsSameIncident(parsedCall, existing);
				}

				return result;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, string.Format("Email Text: {0}", email?.Body));
				return null;
			}
		}

		/// <summary>
		/// The parse result of a template that only builds a call: its key is the incident number or external identifier it read,
		/// and a field counts as read when the template filled it (the priority only when it differs from the department default the
		/// template was given, since every template starts from that default).
		/// </summary>
		public static CallEmailParseResult Describe(Call call, int defaultPriority)
		{
			var result = new CallEmailParseResult { Call = call };
			if (call == null)
				return result;

			var fields = CallEmailParsedFields.None;
			if (!String.IsNullOrWhiteSpace(call.Address))
				fields |= CallEmailParsedFields.Address;
			if (!String.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Trim().Length > 1)
				fields |= CallEmailParsedFields.Coordinates;
			if (!String.IsNullOrWhiteSpace(call.W3W))
				fields |= CallEmailParsedFields.What3Words;
			if (call.Priority != defaultPriority)
				fields |= CallEmailParsedFields.Priority;
			if (!String.IsNullOrWhiteSpace(call.Type) || call.CallTypeId.HasValue)
				fields |= CallEmailParsedFields.Type;
			if (!String.IsNullOrWhiteSpace(call.Name))
				fields |= CallEmailParsedFields.Name;
			if (!String.IsNullOrWhiteSpace(call.NatureOfCall))
				fields |= CallEmailParsedFields.Nature;
			if (!String.IsNullOrWhiteSpace(call.Notes))
				fields |= CallEmailParsedFields.Notes;
			// Units, groups and roles only: every format puts the whole roster on Dispatches, which is not something the email said.
			if ((call.UnitDispatches != null && call.UnitDispatches.Count > 0) || (call.GroupDispatches != null && call.GroupDispatches.Count > 0) ||
				(call.RoleDispatches != null && call.RoleDispatches.Count > 0))
				fields |= CallEmailParsedFields.Resources;

			result.ParsedFields = fields;
			result.MatchKey = CadEmailText.NormalizeId(call.ExternalIdentifier) ?? CadEmailText.NormalizeId(call.IncidentNumber);
			return result;
		}

		public async Task<Call> GenerateCallFromEmailText(CallEmailTypes type, CallEmail email, string managingUser, List<IdentityUser> users,
			Department department, List<Call> activeCalls, List<Unit> units, int priority, List<DepartmentCallPriority> activePriorities,
			List<CallType> callTypes, IGeoLocationProvider geolocationProvider)
		{
			Call call = null;

			try
			{
				call = await _templates[(int)type].GenerateCall(email, managingUser, users, department, activeCalls, units, priority, activePriorities, callTypes, geolocationProvider);

				EnsureRequiredValues(call, email);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, string.Format("Email Text: {0}", email.Body));
			}

			return call;
		}

		/// <summary>
		/// Name and NatureOfCall are non-nullable on the Calls table. A template that can't find a value
		/// for either, a CAD sending a blank segment or a body that didn't match the format, would hand
		/// back a null and lose the dispatch on the insert. Fall back to the email itself instead.
		/// </summary>
		private static void EnsureRequiredValues(Call call, CallEmail email)
		{
			if (call == null)
				return;

			if (String.IsNullOrWhiteSpace(call.NatureOfCall))
				call.NatureOfCall = FirstWithValue(email?.Subject, email?.Body, email?.TextBody);

			if (String.IsNullOrWhiteSpace(call.Name))
				call.Name = FirstWithValue(email?.Subject, call.NatureOfCall);
		}

		private static string FirstWithValue(params string[] values)
		{
			foreach (var value in values)
			{
				if (!String.IsNullOrWhiteSpace(value))
					return value.Trim().Truncate(4000);
			}

			return String.Empty;
		}
	}
}
