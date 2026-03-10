using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UdfFieldValues")]
	public class UdfFieldValue : IEntity
	{
		public string UdfFieldValueId { get; set; }

		public string UdfFieldId { get; set; }

		/// <summary>
		/// Pinned to the definition version at the time the value was saved, preserving historical data.
		/// </summary>
		public string UdfDefinitionId { get; set; }

		/// <summary>
		/// The ID of the entity this value belongs to (CallId, UnitId, ContactId, UserId, etc.).
		/// </summary>
		public string EntityId { get; set; }

		/// <summary>
		/// The entity type. See <see cref="UdfEntityType"/>.
		/// </summary>
		public int EntityType { get; set; }

		public string Value { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedBy { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedBy { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UdfFieldValueId; }
			set { UdfFieldValueId = (string)value; }
		}

		[NotMapped]
		public string TableName => "UdfFieldValues";

		[NotMapped]
		public string IdName => "UdfFieldValueId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}

