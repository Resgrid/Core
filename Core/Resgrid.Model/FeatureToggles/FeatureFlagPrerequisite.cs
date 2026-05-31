using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// A dependency edge: the dependent flag (<see cref="FeatureFlagId"/>) only resolves "on" for a
	/// department when the required flag (<see cref="RequiredFeatureFlagId"/>) also does. The graph is
	/// validated acyclic at write time.
	/// </summary>
	[Table("FeatureFlagPrerequisites")]
	[ProtoContract]
	public class FeatureFlagPrerequisite : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int FeatureFlagPrerequisiteId { get; set; }

		/// <summary>The flag that depends on another.</summary>
		[Required]
		[ProtoMember(2)]
		public int FeatureFlagId { get; set; }

		/// <summary>The flag that must be satisfied first.</summary>
		[Required]
		[ProtoMember(3)]
		public int RequiredFeatureFlagId { get; set; }

		/// <summary>Optional required variant value; null means "the required flag must be enabled".</summary>
		[ProtoMember(4)]
		public string RequiredValue { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return FeatureFlagPrerequisiteId; }
			set { FeatureFlagPrerequisiteId = (int)value; }
		}

		[NotMapped]
		public string TableName => "FeatureFlagPrerequisites";

		[NotMapped]
		public string IdName => "FeatureFlagPrerequisiteId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
