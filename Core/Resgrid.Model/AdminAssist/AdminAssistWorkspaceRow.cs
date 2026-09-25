using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model.AdminAssist
{
	[Table("AdminAssistWorkspaces")]
	public sealed class AdminAssistWorkspaceRow : IEntity
	{
		[Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }
		public long Revision { get; set; }
		public int Mode { get; set; }
		/// <summary>Versioned scope/reason codes, review evidence references and revisit date; never notes, configuration or secrets.</summary>
		public string AreasJson { get; set; }
		public string CatalogVersion { get; set; }
		public DateTime? ReviewedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		[NotMapped, JsonIgnore] public object IdValue { get => DepartmentId; set => DepartmentId = Convert.ToInt32(value); }
		[NotMapped] public string TableName => "AdminAssistWorkspaces";
		[NotMapped] public string IdName => "DepartmentId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
