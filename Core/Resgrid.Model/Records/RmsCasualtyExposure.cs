using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Who the casualty or rescue row is about, matching the contract's CasualtyRescuePayload.type.</summary>
	public static class RmsCasualtyPersonTypes
	{
		/// <summary>A firefighter — responder injury and exposure reporting.</summary>
		public const string Firefighter = "FF";

		/// <summary>A civilian.</summary>
		public const string Civilian = "NONFF";
	}

	/// <summary>Which half of the contract's casualty_rescues entry this row carries.</summary>
	public enum RmsCasualtyRescueKind
	{
		/// <summary>An injury or a documented non-injury.</summary>
		Casualty = 1,

		/// <summary>A rescue, with or without removal.</summary>
		Rescue = 2
	}

	/// <summary>
	/// One civilian or responder casualty or rescue (RMS plan section 4.2, RMS-3; registry M0168; NERIS
	/// <c>incident.casualty_rescues[]</c>).
	/// <para>
	/// This is a restricted class end to end: demographics, injury detail and the person's identity require
	/// <c>RecordRestricted_View</c> in detail, revision, diff, print and export, the row carries the inert
	/// Protected Data envelope (plan 5.9.1), and its retention default is permanent (plan section 4.9). The
	/// department's own personnel link is kept separately from the reported demographics so a responder injury
	/// can be tied to a member without putting a member id into the destination payload.
	/// </para>
	/// </summary>
	public class RmsCasualtyRescue : IEntity
	{
		public string RmsCasualtyRescueId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>The owning incident report.</summary>
		public string RecordId { get; set; }

		public string RevisionId { get; set; }

		/// <summary><see cref="RmsCasualtyRescueKind"/>.</summary>
		public int Kind { get; set; }

		/// <summary><see cref="RmsCasualtyPersonTypes"/>: FF or NONFF.</summary>
		public string PersonType { get; set; }

		/// <summary>Restricted: the department member when the person is one of ours; never sent to the destination.</summary>
		public string PersonnelUserId { get; set; }

		/// <summary>Restricted: rank of the firefighter, as reported.</summary>
		public string Rank { get; set; }

		public decimal? YearsOfService { get; set; }

		/// <summary>NERIS job_classification code.</summary>
		public string JobClassification { get; set; }

		/// <summary>Restricted: NERIS birth_month_year, "YYYY-MM"; never a full date of birth.</summary>
		public string BirthMonthYear { get; set; }

		/// <summary>Restricted: NERIS gender code.</summary>
		public string Gender { get; set; }

		/// <summary>Restricted: NERIS race code.</summary>
		public string Race { get; set; }

		// Casualty half ------------------------------------------------------------------------------------

		/// <summary>False records a documented non-injury (the contract's noinjury branch).</summary>
		public bool? WasInjured { get; set; }

		/// <summary>NERIS casualty_cause code.</summary>
		public string CasualtyCause { get; set; }

		/// <summary>NERIS casualty_action code — what the person was doing.</summary>
		public string CasualtyAction { get; set; }

		/// <summary>NERIS casualty_timeline code — when in the incident it happened.</summary>
		public string CasualtyTimeline { get; set; }

		/// <summary>NERIS duty code for a responder.</summary>
		public string DutyType { get; set; }

		/// <summary>Comma-separated NERIS casualty_ppe codes in use at the time.</summary>
		public string PpeCsv { get; set; }

		/// <summary>Restricted: injury narrative and body-area detail, contract-shaped.</summary>
		public string InjuryDetailJson { get; set; }

		/// <summary>True when the casualty was fatal; drives permanent retention and the restricted class.</summary>
		public bool WasFatal { get; set; }

		// Rescue half --------------------------------------------------------------------------------------

		/// <summary>NERIS rescue type: RESCUED_BY_FIREFIGHTER, RESCUED_BY_FF_RIT, EVAC_ASSISTED_BY_FIREFIGHTER.</summary>
		public string RescueType { get; set; }

		/// <summary>Comma-separated NERIS rescue_action codes.</summary>
		public string RescueActionsCsv { get; set; }

		/// <summary>Comma-separated NERIS rescue_impediment codes.</summary>
		public string RescueImpedimentsCsv { get; set; }

		/// <summary>NERIS rescue_mode code.</summary>
		public string RescueMode { get; set; }

		/// <summary>NERIS rescue_path code.</summary>
		public string RescuePath { get; set; }

		/// <summary>NERIS rescue_elevation code.</summary>
		public string RescueElevation { get; set; }

		/// <summary>NERIS rescue_presence_known code — whether the person was known to be present.</summary>
		public string PresenceKnown { get; set; }

		/// <summary>Contract-shaped remainder of the entry.</summary>
		public string DetailJson { get; set; }

		public DateTime? OccurredOn { get; set; }

		/// <summary>Inert Protected Data envelope (plan section 5.9.1); null until the department enrolls.</summary>
		public string ProtectedEnvelope { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public int Ordinal { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public object IdValue { get => RmsCasualtyRescueId; set => RmsCasualtyRescueId = (string)value; }
		public string TableName => "RmsCasualtyRescues";
		public string IdName => "RmsCasualtyRescueId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// One exposure — property other than the incident property that the incident damaged (registry M0168; NERIS
	/// <c>incident.exposures[]</c>). Damage rating, displacement and the exposure's own location are reported and
	/// summed, so they are columns; the contract's internal/external discrimination and any remainder ride
	/// <see cref="DetailJson"/>.
	/// </summary>
	public class RmsExposure : IEntity
	{
		public string RmsExposureId { get; set; }

		public int DepartmentId { get; set; }

		public string ProtectionId { get; set; }

		/// <summary>The owning incident report.</summary>
		public string RecordId { get; set; }

		public string RevisionId { get; set; }

		/// <summary>INTERNAL when the exposure is inside the incident property, EXTERNAL when it is separate.</summary>
		public string LocationKind { get; set; }

		/// <summary>NERIS exposure_item code — structure, object, outdoor environment.</summary>
		public string ItemType { get; set; }

		/// <summary>NERIS exposure_damage rating; required by the contract.</summary>
		public string DamageType { get; set; }

		/// <summary>NERIS location_use code of the exposed property.</summary>
		public string LocationUse { get; set; }

		public bool? PeoplePresent { get; set; }

		public int? DisplacementCount { get; set; }

		/// <summary>Comma-separated NERIS displace_cause codes.</summary>
		public string DisplacementCausesCsv { get; set; }

		public string AddressText { get; set; }

		public string Street { get; set; }

		public string Municipality { get; set; }

		public string State { get; set; }

		public string PostalCode { get; set; }

		public decimal? Latitude { get; set; }

		public decimal? Longitude { get; set; }

		public decimal? EstimatedValue { get; set; }

		public decimal? EstimatedLoss { get; set; }

		public string CurrencyCode { get; set; }

		/// <summary>Contract-shaped remainder of the exposure entry.</summary>
		public string DetailJson { get; set; }

		/// <summary>Inert Protected Data envelope (plan section 5.9.1); null until the department enrolls.</summary>
		public string ProtectedEnvelope { get; set; }

		public bool IsProtected { get; set; }

		public int ProtectedCatalogVersion { get; set; }

		public int Ordinal { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime ModifiedOn { get; set; }

		public long RowVersion { get; set; }

		public object IdValue { get => RmsExposureId; set => RmsExposureId = (string)value; }
		public string TableName => "RmsExposures";
		public string IdName => "RmsExposureId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
