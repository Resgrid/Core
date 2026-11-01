using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One member's saved home dashboard board in one department (UserDashboards, home-dashboard-redesign-plan.md §5.2).
	/// A member who never customizes has no row: their board is resolved from the department templates or the built-in
	/// default on every read, so template edits reach them. The first save creates the row.
	/// </summary>
	[Table("UserDashboards")]
	public class UserDashboard : IEntity
	{
		[Key]
		[Required]
		public string UserDashboardId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string UserId { get; set; }

		[MaxLength(100)]
		public string Name { get; set; }

		/// <summary>Board (tab) order; 0 is the member's home board.</summary>
		public int SortOrder { get; set; }

		/// <summary>The board's <see cref="Dashboard.DashboardLayout"/> as JSON (schema version in <see cref="SchemaVersion"/>).</summary>
		public string LayoutJson { get; set; }

		public int SchemaVersion { get; set; }

		/// <summary>The department template the board was first saved from, if any.</summary>
		public string TemplateId { get; set; }

		/// <summary>The member chose the classic dashboard (until GA, plan D10).</summary>
		public bool UseClassic { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime UpdatedOn { get; set; }

		/// <summary>Incremented on every save; a save that names an older value is a conflict (plan §5.4).</summary>
		public int RowVersion { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UserDashboardId; }
			set { UserDashboardId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "UserDashboards";

		[NotMapped]
		public string IdName => "UserDashboardId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
