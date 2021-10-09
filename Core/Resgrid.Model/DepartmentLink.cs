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

		[Required]
		public int LinkedDepartmentId { get; set; } // Linked Department color for the Department

		public virtual Department LinkedDepartment { get; set; }

		public bool LinkEnabled { get; set; }

		public string LinkedDepartmentColor { get; set; }

		public bool LinkedDepartmentShareCalls { get; set; }

		public bool LinkedDepartmentShareUnits { get; set; }

		public bool LinkedDepartmentSharePersonnel { get; set; }

		public bool LinkedDepartmentShareOrders { get; set; }

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
