using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Suggests a meaning (base type / class) from the name a department gave a status, staffing level, call type, unit type
	/// or priority ("IN QUARTERS" -> In Quarters, "Pumper" -> Engine). Only ever a suggestion the administrator confirms on
	/// screen; nothing is applied without them saving it. Null when the name gives no clear hint.
	/// </summary>
	public static class BaseTypeSuggestions
	{
		// Exact matches on the normalized name (letters and digits, lower case) win; then the first "contains" rule.
		private static readonly Dictionary<string, ActionBaseTypes> StatusExact = new Dictionary<string, ActionBaseTypes>
		{
			["available"] = ActionBaseTypes.Available, ["avail"] = ActionBaseTypes.Available, ["avb"] = ActionBaseTypes.Available,
			["inservice"] = ActionBaseTypes.Available, ["ready"] = ActionBaseTypes.Available, ["standingby"] = ActionBaseTypes.Available,
			["onair"] = ActionBaseTypes.Available, ["radioavailable"] = ActionBaseTypes.Available, ["availableonradio"] = ActionBaseTypes.Available,
			["inquarters"] = ActionBaseTypes.InQuarters, ["atstation"] = ActionBaseTypes.InQuarters, ["instation"] = ActionBaseTypes.InQuarters,
			["athall"] = ActionBaseTypes.InQuarters, ["availablestation"] = ActionBaseTypes.InQuarters, ["athome"] = ActionBaseTypes.InQuarters,
			["dispatched"] = ActionBaseTypes.Dispatched, ["assigned"] = ActionBaseTypes.Dispatched, ["alerted"] = ActionBaseTypes.Dispatched, ["paged"] = ActionBaseTypes.Dispatched,
			["responding"] = ActionBaseTypes.Responding, ["respondingtoscene"] = ActionBaseTypes.Responding, ["respondingtostation"] = ActionBaseTypes.Responding,
			["enroute"] = ActionBaseTypes.Enroute, ["onmyway"] = ActionBaseTypes.Enroute, ["omw"] = ActionBaseTypes.Enroute, ["mobile"] = ActionBaseTypes.Enroute,
			["onscene"] = ActionBaseTypes.OnScene, ["onlocation"] = ActionBaseTypes.OnScene, ["arrived"] = ActionBaseTypes.OnScene, ["atscene"] = ActionBaseTypes.OnScene, ["onsite"] = ActionBaseTypes.OnScene,
			["staging"] = ActionBaseTypes.Staging, ["staged"] = ActionBaseTypes.Staging,
			["transporting"] = ActionBaseTypes.Transporting, ["tohospital"] = ActionBaseTypes.Transporting, ["transport"] = ActionBaseTypes.Transporting,
			["athospital"] = ActionBaseTypes.AtHospital, ["arrivedhospital"] = ActionBaseTypes.AtHospital,
			["atpatient"] = ActionBaseTypes.AtPatient, ["patientcontact"] = ActionBaseTypes.AtPatient, ["withpatient"] = ActionBaseTypes.AtPatient,
			["returning"] = ActionBaseTypes.Returning, ["rtq"] = ActionBaseTypes.Returning, ["rtb"] = ActionBaseTypes.Returning, ["returningtoquarters"] = ActionBaseTypes.Returning,
			["cleared"] = ActionBaseTypes.Cleared, ["clear"] = ActionBaseTypes.Cleared, ["released"] = ActionBaseTypes.Cleared,
			["outofservice"] = ActionBaseTypes.Unavailable, ["oos"] = ActionBaseTypes.Unavailable, ["unavailable"] = ActionBaseTypes.Unavailable, ["notavailable"] = ActionBaseTypes.Unavailable,
			["maintenance"] = ActionBaseTypes.Maintenance, ["inshop"] = ActionBaseTypes.Maintenance, ["shop"] = ActionBaseTypes.Maintenance, ["repair"] = ActionBaseTypes.Maintenance, ["fuel"] = ActionBaseTypes.Maintenance,
			["onbreak"] = ActionBaseTypes.OnBreak, ["break"] = ActionBaseTypes.OnBreak, ["meal"] = ActionBaseTypes.OnBreak, ["lunch"] = ActionBaseTypes.OnBreak,
			["notresponding"] = ActionBaseTypes.NotResponding, ["notgoing"] = ActionBaseTypes.NotResponding, ["declined"] = ActionBaseTypes.NotResponding, ["cantmakeit"] = ActionBaseTypes.NotResponding,
			["standby"] = ActionBaseTypes.Standby, ["onpatrol"] = ActionBaseTypes.OnPatrol, ["patrol"] = ActionBaseTypes.OnPatrol,
			["completed"] = ActionBaseTypes.Completed, ["complete"] = ActionBaseTypes.Completed, ["done"] = ActionBaseTypes.Completed,
			["searching"] = ActionBaseTypes.Searching, ["investigating"] = ActionBaseTypes.Investigating, ["madecontact"] = ActionBaseTypes.MadeContact,
			["loading"] = ActionBaseTypes.Loading, ["delivering"] = ActionBaseTypes.Delivering
		};

		private static readonly (string Fragment, ActionBaseTypes BaseType)[] StatusContains =
		{
			("outofservice", ActionBaseTypes.Unavailable), ("unavailable", ActionBaseTypes.Unavailable), ("notavail", ActionBaseTypes.Unavailable),
			("tohospital", ActionBaseTypes.Transporting), ("hospital", ActionBaseTypes.AtHospital), ("transport", ActionBaseTypes.Transporting),
			("return", ActionBaseTypes.Returning), ("respond", ActionBaseTypes.Responding), ("dispatch", ActionBaseTypes.Dispatched),
			("enroute", ActionBaseTypes.Enroute), ("quarters", ActionBaseTypes.InQuarters), ("station", ActionBaseTypes.InQuarters),
			("onscene", ActionBaseTypes.OnScene), ("location", ActionBaseTypes.OnScene),
			("staging", ActionBaseTypes.Staging), ("maint", ActionBaseTypes.Maintenance), ("break", ActionBaseTypes.OnBreak),
			("avail", ActionBaseTypes.Available)
		};

		private static readonly Dictionary<string, StaffingBaseTypes> StaffingExact = new Dictionary<string, StaffingBaseTypes>
		{
			["available"] = StaffingBaseTypes.Available, ["onduty"] = StaffingBaseTypes.OnShift, ["duty"] = StaffingBaseTypes.OnShift, ["avail"] = StaffingBaseTypes.Available,
			["onshift"] = StaffingBaseTypes.OnShift, ["shift"] = StaffingBaseTypes.OnShift, ["working"] = StaffingBaseTypes.OnShift, ["dutycrew"] = StaffingBaseTypes.OnShift,
			["oncall"] = StaffingBaseTypes.OnCall, ["pager"] = StaffingBaseTypes.OnCall, ["pageronly"] = StaffingBaseTypes.OnCall, ["standby"] = StaffingBaseTypes.OnCall,
			["delayed"] = StaffingBaseTypes.Delayed, ["late"] = StaffingBaseTypes.Delayed,
			["committed"] = StaffingBaseTypes.Committed, ["ontask"] = StaffingBaseTypes.Committed, ["busy"] = StaffingBaseTypes.Committed,
			["offduty"] = StaffingBaseTypes.OffDuty, ["off"] = StaffingBaseTypes.OffDuty, ["offshift"] = StaffingBaseTypes.OffDuty,
			["unavailable"] = StaffingBaseTypes.Unavailable, ["notavailable"] = StaffingBaseTypes.Unavailable, ["away"] = StaffingBaseTypes.Unavailable,
			["leave"] = StaffingBaseTypes.Unavailable, ["vacation"] = StaffingBaseTypes.Unavailable, ["holiday"] = StaffingBaseTypes.Unavailable, ["sick"] = StaffingBaseTypes.Unavailable
		};

		private static readonly (string Fragment, StaffingBaseTypes BaseType)[] StaffingContains =
		{
			("offduty", StaffingBaseTypes.OffDuty), ("unavail", StaffingBaseTypes.Unavailable), ("notavail", StaffingBaseTypes.Unavailable),
			("oncall", StaffingBaseTypes.OnCall), ("shift", StaffingBaseTypes.OnShift), ("delay", StaffingBaseTypes.Delayed),
			("onduty", StaffingBaseTypes.OnShift), ("avail", StaffingBaseTypes.Available)
		};

		private static readonly (string Fragment, CallTypeClasses Class)[] CallTypeContains =
		{
			("mutualaid", CallTypeClasses.MutualAid), ("hazmat", CallTypeClasses.HazardousMaterials), ("gasleak", CallTypeClasses.HazardousMaterials), ("odor", CallTypeClasses.HazardousMaterials),
			("alarm", CallTypeClasses.Alarm), ("mva", CallTypeClasses.TrafficCollision), ("mvc", CallTypeClasses.TrafficCollision), ("collision", CallTypeClasses.TrafficCollision),
			("accident", CallTypeClasses.TrafficCollision), ("crash", CallTypeClasses.TrafficCollision), ("vehicleaccident", CallTypeClasses.TrafficCollision),
			("rescue", CallTypeClasses.Rescue), ("search", CallTypeClasses.Rescue), ("water", CallTypeClasses.Rescue), ("extrication", CallTypeClasses.Rescue), ("entrapment", CallTypeClasses.Rescue),
			("fire", CallTypeClasses.Fire), ("smoke", CallTypeClasses.Fire), ("brush", CallTypeClasses.Fire), ("burn", CallTypeClasses.Fire),
			("medical", CallTypeClasses.Medical), ("ems", CallTypeClasses.Medical), ("ambulance", CallTypeClasses.Medical), ("cardiac", CallTypeClasses.Medical),
			("injury", CallTypeClasses.Medical), ("sick", CallTypeClasses.Medical), ("fall", CallTypeClasses.Medical), ("overdose", CallTypeClasses.Medical),
			("storm", CallTypeClasses.DisasterWeather), ("flood", CallTypeClasses.DisasterWeather), ("weather", CallTypeClasses.DisasterWeather), ("disaster", CallTypeClasses.DisasterWeather),
			("earthquake", CallTypeClasses.DisasterWeather), ("police", CallTypeClasses.LawEnforcement), ("security", CallTypeClasses.LawEnforcement), ("crime", CallTypeClasses.LawEnforcement),
			("training", CallTypeClasses.NonEmergencyActivity), ("drill", CallTypeClasses.NonEmergencyActivity), ("standby", CallTypeClasses.NonEmergencyActivity), ("event", CallTypeClasses.NonEmergencyActivity),
			("meeting", CallTypeClasses.NonEmergencyActivity), ("service", CallTypeClasses.ServiceCall), ("assist", CallTypeClasses.ServiceCall), ("lockout", CallTypeClasses.ServiceCall),
			("other", CallTypeClasses.Other)
		};

		private static readonly (string Fragment, UnitTypeClasses Class)[] UnitTypeContains =
		{
			("hazmat", UnitTypeClasses.HazardousMaterials), ("crashtender", UnitTypeClasses.Engine),
			("medical", UnitTypeClasses.MedicalResponse), ("qrv", UnitTypeClasses.MedicalResponse), ("firstresponse", UnitTypeClasses.MedicalResponse),
			("ambulance", UnitTypeClasses.Ambulance), ("medic", UnitTypeClasses.Ambulance), ("als", UnitTypeClasses.Ambulance), ("bls", UnitTypeClasses.Ambulance),
			("ladder", UnitTypeClasses.Aerial), ("aerial", UnitTypeClasses.Aerial), ("quint", UnitTypeClasses.Aerial), ("tower", UnitTypeClasses.Aerial), ("platform", UnitTypeClasses.Aerial), ("truck", UnitTypeClasses.Aerial),
			("tanker", UnitTypeClasses.Tanker), ("tender", UnitTypeClasses.Tanker), ("brush", UnitTypeClasses.Wildland), ("wildland", UnitTypeClasses.Wildland), ("forestry", UnitTypeClasses.Wildland),
			("rescue", UnitTypeClasses.Rescue), ("squad", UnitTypeClasses.Rescue), ("engine", UnitTypeClasses.Engine), ("pumper", UnitTypeClasses.Engine), ("squirt", UnitTypeClasses.Engine),
			("chief", UnitTypeClasses.Command), ("command", UnitTypeClasses.Command), ("battalion", UnitTypeClasses.Command), ("supervisor", UnitTypeClasses.Command),
			("boat", UnitTypeClasses.Watercraft), ("marine", UnitTypeClasses.Watercraft), ("fireboat", UnitTypeClasses.Watercraft),
			("helicopter", UnitTypeClasses.Aircraft), ("aircraft", UnitTypeClasses.Aircraft), ("drone", UnitTypeClasses.Aircraft),
			("police", UnitTypeClasses.Patrol), ("patrol", UnitTypeClasses.Patrol), ("utility", UnitTypeClasses.Support), ("support", UnitTypeClasses.Support), ("service", UnitTypeClasses.Support)
		};

		private static readonly (string Fragment, CallPriority Priority)[] PriorityContains =
		{
			("nonemergency", CallPriority.Low), ("nonurgent", CallPriority.Low),
			("emergency", CallPriority.Emergency), ("critical", CallPriority.Emergency), ("code3", CallPriority.Emergency), ("lightsandsiren", CallPriority.Emergency), ("echo", CallPriority.Emergency),
			("urgent", CallPriority.High), ("high", CallPriority.High), ("code2", CallPriority.Medium),
			("medium", CallPriority.Medium), ("normal", CallPriority.Medium), ("moderate", CallPriority.Medium), ("routine", CallPriority.Low), ("low", CallPriority.Low)
		};

		/// <summary>A suggested base type for a status (ActionBaseTypes) or staffing level (StaffingBaseTypes) by its name.</summary>
		public static int? SuggestStatus(string name, CustomStateTypes type)
		{
			var normalized = Normalize(name);
			if (normalized.Length == 0)
				return null;

			if (type == CustomStateTypes.Staffing)
			{
				if (StaffingExact.TryGetValue(normalized, out var staffing))
					return (int)staffing;

				return StaffingContains.Where(x => normalized.Contains(x.Fragment)).Select(x => (int?)x.BaseType).FirstOrDefault();
			}

			if (StatusExact.TryGetValue(normalized, out var status))
				return Adjust(status, type);

			var contained = StatusContains.Where(x => normalized.Contains(x.Fragment)).Select(x => (ActionBaseTypes?)x.BaseType).FirstOrDefault();
			return contained.HasValue ? Adjust(contained.Value, type) : null;
		}

		// A person can't be "not responding" as a unit status, and has no maintenance.
		private static int? Adjust(ActionBaseTypes baseType, CustomStateTypes type)
		{
			if (type == CustomStateTypes.Unit && baseType == ActionBaseTypes.NotResponding)
				return (int)ActionBaseTypes.Unavailable;

			return (int)baseType;
		}

		public static int? SuggestCallTypeClass(string name) => Suggest(name, CallTypeContains.Select(x => (x.Fragment, (int)x.Class)));

		public static int? SuggestUnitTypeClass(string name) => Suggest(name, UnitTypeContains.Select(x => (x.Fragment, (int)x.Class)));

		public static int? SuggestPriority(string name) => Suggest(name, PriorityContains.Select(x => (x.Fragment, (int)x.Priority)));

		private static int? Suggest(string name, IEnumerable<(string Fragment, int Value)> rules)
		{
			var normalized = Normalize(name);
			if (normalized.Length == 0)
				return null;

			return rules.Where(x => normalized.Contains(x.Fragment)).Select(x => (int?)x.Value).FirstOrDefault();
		}

		public static string Normalize(string value) =>
			String.IsNullOrWhiteSpace(value) ? String.Empty : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
	}
}
