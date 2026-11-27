using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DepartmentLinks")]
	public class DepartmentLink: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentLinkId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public string DepartmentColor { get; set; } // Department color for the Linked Department

		public bool DepartmentShareCalls { get; set; }

		public bool DepartmentShareUnits { get; set; }

		public bool DepartmentSharePersonnel { get; set; }

		public bool DepartmentShareOrders { get; set; }

		/// <summary>Protocols P5 (M0414): the creating department shares its published protocols and protocol sets.</summary>
		public bool DepartmentShareProtocols { get; set; }

		/// <summary>Training T5 (M0414): the creating department shares its course sets (and may sponsor LMS seats) over the link.</summary>
		public bool DepartmentShareTraining { get; set; }

		[Required]
		public int LinkedDepartmentId { get; set; } // Linked Department color for the Department

		public virtual Department LinkedDepartment { get; set; }

		public bool LinkEnabled { get; set; }

		public string LinkedDepartmentColor { get; set; }

		public bool LinkedDepartmentShareCalls { get; set; }

		public bool LinkedDepartmentShareUnits { get; set; }

		public bool LinkedDepartmentSharePersonnel { get; set; }

		public bool LinkedDepartmentShareOrders { get; set; }

		/// <summary>Protocols P5 (M0414): the linked department shares its published protocols and protocol sets.</summary>
		public bool LinkedDepartmentShareProtocols { get; set; }

		/// <summary>Training T5 (M0414): the linked department shares its course sets (and may sponsor LMS seats) over the link.</summary>
		public bool LinkedDepartmentShareTraining { get; set; }

		public DateTime LinkCreated { get; set; }

		public DateTime? LinkAccepted { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentLinkId; }
			set { DepartmentLinkId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentLinks";

		[NotMapped]
		public string IdName => "DepartmentLinkId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "LinkedDepartment" };
	}
}
