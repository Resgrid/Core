using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	/// <summary>Pre-plan editor page (Contacts plan Phase A, A6).</summary>
	public class ContactPreplanView
	{
		public Contact Contact { get; set; }
		public Department Department { get; set; }
		public ContactPreplan Preplan { get; set; }
		public List<ContactPreplanHazard> Hazards { get; set; } = new List<ContactPreplanHazard>();

		public SelectList ConstructionTypes { get; set; }
		public SelectList RoofTypes { get; set; }
		public SelectList OccupancyTypes { get; set; }
		public SelectList HazardTypes { get; set; }
		public SelectList HazardSeverities { get; set; }

		/// <summary>Bound from the form; null clears the schedule.</summary>
		public DateTime? NextReviewDue { get; set; }

		/// <summary>Stamps LastReviewedOn / ReviewedByUserId on save.</summary>
		public bool MarkReviewed { get; set; }

		public string Message { get; set; }

		/// <summary>ADP: the contact's own identity fields render as REDACTED (the pre-plan itself is not cataloged).</summary>
		public bool IsProtectedContact { get; set; }

		/// <summary>RMS-5 write cutover: pre-plans are projected from Records occupancies and this editor is read-only.</summary>
		public bool IsRecordsOwned { get; set; }

		/// <summary>The occupancy the projection comes from when <see cref="IsRecordsOwned"/> (null when the contact has none yet).</summary>
		public string OccupancyId { get; set; }
	}

	/// <summary>Site attachments page (Contacts plan Phase A, A6).</summary>
	public class ContactAttachmentsView
	{
		public Contact Contact { get; set; }
		public Department Department { get; set; }
		public List<ContactAttachment> Attachments { get; set; } = new List<ContactAttachment>();
		public SelectList AttachmentTypes { get; set; }
		public string Message { get; set; }
		public bool IsProtectedContact { get; set; }
	}

	/// <summary>JSON body for the hazard add/edit endpoint.</summary>
	public class SaveContactHazardInput
	{
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

	public class ContactHazardJson
	{
		public string ContactPreplanHazardId { get; set; }
		public string ContactId { get; set; }
		public int HazardType { get; set; }
		public string HazardTypeName { get; set; }
		public int Severity { get; set; }
		public string SeverityName { get; set; }
		public string SeverityColor { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string LocationDescription { get; set; }
		public string GpsCoordinates { get; set; }
		public bool ShouldAlert { get; set; }
		public string AddedOn { get; set; }
		public string AddedBy { get; set; }
	}

	public class ContactAttachmentJson
	{
		public int ContactAttachmentId { get; set; }
		public string ContactId { get; set; }
		public int Type { get; set; }
		public string TypeName { get; set; }
		public string Name { get; set; }
		public string FileName { get; set; }
		public string Mime { get; set; }
		public int Size { get; set; }
		public string AddedOn { get; set; }
		public string AddedBy { get; set; }
		public string Url { get; set; }
	}
}
