using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Scopes a communication test to a subset of the department. Rows are additive: the tested
	/// audience is the union of every target, intersected with current department membership.
	/// A test with no rows tests the whole department.
	/// </summary>
	[Table("CommunicationTestTargets")]
	public class CommunicationTestTarget : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid CommunicationTestTargetId { get; set; }

		[Required]
		public Guid CommunicationTestId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>
		/// A <see cref="CommunicationTestTargetType"/> value.
		/// </summary>
		public int TargetType { get; set; }

		/// <summary>
		/// The group id, role id or user id this row targets. Stored as a string because group and
		/// role ids are ints while user ids are 128 character identity keys.
		/// </summary>
		[Required]
		[MaxLength(128)]
		public string TargetId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CommunicationTestTargetId == Guid.Empty ? null : (object)CommunicationTestTargetId.ToString(); }
			set { CommunicationTestTargetId = value == null ? Guid.Empty : Guid.Parse(value.ToString()); }
		}

		[NotMapped]
		public string TableName => "CommunicationTestTargets";

		[NotMapped]
		public string IdName => "CommunicationTestTargetId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
