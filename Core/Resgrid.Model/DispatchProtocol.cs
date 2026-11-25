using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocols")]
	public class DispatchProtocol : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[MaxLength(100)]
		public string Name { get; set; }

		/// <summary>Longest code a protocol can carry: room for determinant-style codes (09-E-01, 26A01) and SOG numbers (SOG-204).</summary>
		public const int CodeMaxLength = 16;

		/// <summary>Longest description, counted on its text rather than its markup.</summary>
		public const int DescriptionTextMaxLength = 500;

		[Required]
		[MaxLength(CodeMaxLength)]
		public string Code { get; set; }

		public bool IsDisabled { get; set; }

		/// <summary>
		/// A deleted protocol stays as a row because the calls it was used on cite it. It is left out of lists, matching and
		/// new calls; a call that already carries it still shows it.
		/// </summary>
		public bool IsDeleted { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>Rich text. Its text, not its markup, is limited to <see cref="DescriptionTextMaxLength"/> (validated on save).</summary>
		public string Description { get; set; }

		public string ProtocolText { get; set; }

		[Required]
		public DateTime CreatedOn { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public int MinimumWeight { get; set; }

		[Required]
		public string UpdatedByUserId { get; set; }

		public virtual ICollection<DispatchProtocolTrigger> Triggers { get; set; }

		public virtual ICollection<DispatchProtocolAttachment> Attachments { get; set; }

		public virtual ICollection<DispatchProtocolQuestion> Questions { get; set; }

		[NotMapped]
		public ProtocolStates State { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolId; }
			set { DispatchProtocolId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocols";

		[NotMapped]
		public string IdName => "DispatchProtocolId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Triggers", "Attachments", "Questions", "State" };
	}
}
