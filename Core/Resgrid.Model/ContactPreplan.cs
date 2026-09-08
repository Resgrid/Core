using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Structured pre-incident plan for a Contact (NFPA 1620 core fields; Contacts plan Phase A, decision 1).
	/// One live row per contact. The dispatch-facing structure master until the RMS occupancy master lands,
	/// after which it becomes the lightweight projection (RMS plan section 4.3).
	/// </summary>
	public class ContactPreplan : IEntity
	{
		[Required]
		public string ContactPreplanId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string ContactId { get; set; }

		// Construction / occupancy
		public int ConstructionType { get; set; }
		public int RoofType { get; set; }
		public int OccupancyType { get; set; }
		public string OccupancyNotes { get; set; }
		public string OccupancyHours { get; set; }
		public int? OccupantLoad { get; set; }
		public bool HasOccupantsNeedingAssistance { get; set; }
		public string OccupantsNeedingAssistanceNotes { get; set; }

		// Utilities
		public string GasShutoffLocation { get; set; }
		public string ElectricShutoffLocation { get; set; }
		public string WaterShutoffLocation { get; set; }
		public string UtilityNotes { get; set; }

		// Access
		public string KnoxBoxLocation { get; set; }

		/// <summary>
		/// ADP catalog v12 field (contactpreplans.gatecode): enveloped at rest in a protected department and
		/// revealed only through the protected read with a current grant. Never written to search, workflow
		/// payloads, or audit snapshots in clear.
		/// </summary>
		public string GateCode { get; set; }
		public string AlarmPanelLocation { get; set; }
		public string AlarmCompany { get; set; }
		public string AlarmCompanyPhone { get; set; }
		public string AccessNotes { get; set; }

		// Water supply
		public string NearestHydrantLocation { get; set; }
		public int? RequiredFireFlowGpm { get; set; }
		public string WaterSupplyNotes { get; set; }

		// On-site contacts
		public string EmergencyContactName { get; set; }
		public string EmergencyContactPhone { get; set; }
		public string SecondaryContactName { get; set; }
		public string SecondaryContactPhone { get; set; }

		// Hazards / tactics
		public bool HazmatOnSite { get; set; }
		public string GeneralHazardNotes { get; set; }
		public string TacticalSummary { get; set; }

		// Review cycle
		public DateTime? LastReviewedOn { get; set; }
		public string ReviewedByUserId { get; set; }
		public DateTime? NextReviewDue { get; set; }

		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		/// <summary>ADP row marker (catalog v12): true once the cataloged text columns are enveloped.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		public List<ContactPreplanHazard> Hazards { get; set; }

		[NotMapped]
		public string TableName => "ContactPreplans";

		[NotMapped]
		public string IdName => "ContactPreplanId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactPreplanId; }
			set { ContactPreplanId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Hazards" };

		/// <summary>True when a review date is set and has passed.</summary>
		public bool IsReviewOverdue(DateTime utcNow)
		{
			return NextReviewDue.HasValue && NextReviewDue.Value < utcNow;
		}
	}
}
