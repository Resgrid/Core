using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A repeating premise hazard on a contact pre-plan (Contacts plan Phase A, decision 1). Typed severity
	/// and optional GPS; ShouldAlert rows surface in the dispatch alert banner alongside alert notes.
	/// </summary>
	public class ContactPreplanHazard : IEntity
	{
		[Required]
		public string ContactPreplanHazardId { get; set; }

		[Required]
		public string ContactPreplanId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Denormalized so hazards can be listed by contact without a join.</summary>
		[Required]
		public string ContactId { get; set; }

		public int HazardType { get; set; }
		public int Severity { get; set; }

		[Required]
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }

		/// <summary>"lat,lng" like the rest of the platform.</summary>
		public string GpsCoordinates { get; set; }

		public bool ShouldAlert { get; set; }

		public bool IsDeleted { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? EditedOn { get; set; }
		public string EditedByUserId { get; set; }

		/// <summary>ADP row marker (catalog v12): true once the cataloged text columns are enveloped.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		public string TableName => "ContactPreplanHazards";

		[NotMapped]
		public string IdName => "ContactPreplanHazardId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ContactPreplanHazardId; }
			set { ContactPreplanHazardId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
