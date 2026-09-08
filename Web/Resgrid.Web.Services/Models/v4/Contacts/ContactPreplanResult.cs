using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.Contacts
{
	/// <summary>
	/// A contact's pre-incident plan (Contacts plan Phase A).
	/// </summary>
	public class ContactPreplanResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data (null when the contact has no pre-plan)
		/// </summary>
		public ContactPreplanData Data { get; set; }
	}

	/// <summary>
	/// Structured NFPA 1620 pre-incident plan for a contact. Enum-valued fields carry the integer value;
	/// see ContactPreplanConstructionTypes / ContactPreplanRoofTypes / ContactPreplanOccupancyTypes.
	/// </summary>
	public class ContactPreplanData
	{
		/// <summary>
		/// ADP: true when this row belongs to a protection-enforced department (shield indicator).
		/// Protected values here are broker-decrypted plaintext or the exact "REDACTED" placeholder
		/// — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when values are redacted (step_up_required,
		/// grant_expired, grant_revoked, protected_access_denied, broker_unavailable); null when
		/// nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>ADP: stable catalog field ids (catalog v12) whose values are REDACTED on this row.</summary>
		public List<string> RedactedFields { get; set; } = new List<string>();

		public string ContactPreplanId { get; set; }
		public string ContactId { get; set; }

		/// <summary>ContactPreplanConstructionTypes value</summary>
		public int ConstructionType { get; set; }
		public string ConstructionTypeName { get; set; }

		/// <summary>ContactPreplanRoofTypes value</summary>
		public int RoofType { get; set; }
		public string RoofTypeName { get; set; }

		/// <summary>ContactPreplanOccupancyTypes value</summary>
		public int OccupancyType { get; set; }
		public string OccupancyTypeName { get; set; }
		public string OccupancyNotes { get; set; }
		public string OccupancyHours { get; set; }
		public int? OccupantLoad { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }

		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }

		public string KnoxBoxLocation { get; set; }
		/// <summary>ADP catalog v12 field: decrypted with a valid grant, otherwise the REDACTED placeholder.</summary>
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }

		public string NearestHydrantLocation { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }

		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }
		public string SecondaryContactName { get; set; }
		public string SecondaryContactPhone { get; set; }

		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? LastReviewedOnUtc { get; set; }
		public string LastReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? NextReviewDueUtc { get; set; }
		public string NextReviewDue { get; set; }

		/// <summary>True when NextReviewDue has passed.</summary>
		public bool IsReviewOverdue { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime AddedOnUtc { get; set; }
		public string AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? EditedOnUtc { get; set; }
		public string EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		/// <summary>Live hazards on this pre-plan.</summary>
		public List<ContactHazardData> Hazards { get; set; } = new List<ContactHazardData>();
	}

	/// <summary>
	/// Input to create or replace a contact's pre-plan. The whole plan is written; omitted text fields clear.
	/// </summary>
	public class SaveContactPreplanInput
	{
		[Required]
		public string ContactId { get; set; }

		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public string OccupancyNotes { get; set; }
		public string OccupancyHours { get; set; }
		public int? OccupantLoad { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }

		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }

		public string KnoxBoxLocation { get; set; }
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }

		public string NearestHydrantLocation { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }

		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }
		public string SecondaryContactName { get; set; }
		public string SecondaryContactPhone { get; set; }

		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }

		/// <summary>When true, LastReviewedOn is stamped now and ReviewedByUserId is the caller.</summary>
		public bool MarkReviewed { get; set; }

		/// <summary>Next scheduled review (UTC). Null leaves no review scheduled.</summary>
		public DateTime? NextReviewDueUtc { get; set; }
	}

	public class SaveContactPreplanResult : StandardApiResponseV4Base
	{
		/// <summary>Id of the pre-plan row</summary>
		public string Id { get; set; }
	}

	public class DeleteContactPreplanResult : StandardApiResponseV4Base
	{
	}
}
