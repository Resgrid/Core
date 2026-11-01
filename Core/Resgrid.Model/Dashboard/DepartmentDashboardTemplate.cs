using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Who a department dashboard template is for (plan D7).</summary>
	public enum DashboardTemplateAudiences
	{
		Everyone = 0,
		PersonnelRole = 1,
		Group = 2
	}

	/// <summary>
	/// A department admin's dashboard template (DepartmentDashboardTemplates, plan §5.2). A member with no saved board gets
	/// the first template matching one of their roles or groups (lowest <see cref="Priority"/>), else the department default.
	/// </summary>
	[Table("DepartmentDashboardTemplates")]
	public class DepartmentDashboardTemplate : IEntity
	{
		[Key]
		[Required]
		public string DepartmentDashboardTemplateId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		[MaxLength(500)]
		public string Description { get; set; }

		public int Audience { get; set; }

		/// <summary>Personnel role id or department group id for <see cref="Audience"/> 1 and 2; null for everyone.</summary>
		public int? TargetId { get; set; }

		/// <summary>Lower wins when a member matches several templates.</summary>
		public int Priority { get; set; }

		public string LayoutJson { get; set; }

		public int SchemaVersion { get; set; }

		public bool IsDefault { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentDashboardTemplateId; }
			set { DepartmentDashboardTemplateId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "DepartmentDashboardTemplates";

		[NotMapped]
		public string IdName => "DepartmentDashboardTemplateId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
