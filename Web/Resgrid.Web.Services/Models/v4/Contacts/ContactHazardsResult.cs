using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Models.v4.Contacts
{
	/// <summary>
	/// Premise hazards on a contact's pre-plan (Contacts plan Phase A).
	/// </summary>
	public class ContactHazardsResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<ContactHazardData> Data { get; set; } = new List<ContactHazardData>();
	}

	/// <summary>
	/// A single premise hazard.
	/// </summary>
	public class ContactHazardData
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

		public string ContactPreplanHazardId { get; set; }
		public string ContactPreplanId { get; set; }
		public string ContactId { get; set; }

		/// <summary>ContactPreplanHazardTypes value (General = 0, Hazmat = 1, Structural = 2, Electrical = 3, Biological = 4, Animal = 5, Occupant = 6, Other = 7)</summary>
		public int HazardType { get; set; }
		public string HazardTypeName { get; set; }

		/// <summary>ContactPreplanHazardSeverities value (Info = 0, Caution = 1, Danger = 2)</summary>
		public int Severity { get; set; }
		public string SeverityName { get; set; }

		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }

		/// <summary>"lat,lng"</summary>
		public string GpsCoordinates { get; set; }

		/// <summary>Surfaces in the dispatch alert banner when true.</summary>
		public bool ShouldAlert { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime AddedOnUtc { get; set; }
		public string AddedOn { get; set; }
		public string AddedByUserId { get; set; }

		[JsonConverter(typeof(UtcDateTimeConverter))]
		public DateTime? EditedOnUtc { get; set; }
		public string EditedOn { get; set; }
		public string EditedByUserId { get; set; }
	}

	/// <summary>
	/// Input to create (no id) or update (with id) a hazard. A contact without a pre-plan gets an empty one created.
	/// </summary>
	public class SaveContactHazardInput
	{
		/// <summary>Omit to create a new hazard.</summary>
		public string ContactPreplanHazardId { get; set; }

		[Required]
		public string ContactId { get; set; }

		public int HazardType { get; set; }
		public int Severity { get; set; }

		[Required]
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
	}

	public class SaveContactHazardResult : StandardApiResponseV4Base
	{
		/// <summary>Id of the hazard row</summary>
		public string Id { get; set; }
	}

	public class DeleteContactHazardResult : StandardApiResponseV4Base
	{
	}
}
