using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UdfDefinitions")]
	public class UdfDefinition : IEntity
	{
		public string UdfDefinitionId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>
		/// The entity type this definition applies to. See <see cref="UdfEntityType"/>.
		/// </summary>
		public int EntityType { get; set; }

		/// <summary>
		/// Auto-incrementing version number per department + entity type. Each save creates a new version.
		/// </summary>
		public int Version { get; set; }

		/// <summary>
		/// Only one definition per department + entity type is active at any time.
		/// </summary>
		public bool IsActive { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedBy { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedBy { get; set; }

		[NotMapped]
		public virtual ICollection<UdfField> Fields { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UdfDefinitionId; }
			set { UdfDefinitionId = (string)value; }
		}

		[NotMapped]
		public string TableName => "UdfDefinitions";

		[NotMapped]
		public string IdName => "UdfDefinitionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new string[] { "IdValue", "IdType", "TableName", "IdName", "Fields" };
	}
}

