using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using Resgrid.Framework;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("CallNotes")]
	public class CallNote : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CallNoteId { get; set; }

		[ProtoMember(2)]
		public int CallId { get; set; }

		[ForeignKey("CallId")]
		public virtual Call Call { get; set; }

		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		[ForeignKey("UserId")]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(5)]
		public string Note { get; set; }

		[ProtoMember(6)]
		public int Source { get; set; }

		[ProtoMember(7)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(8)]
		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[ProtoMember(9)]
		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		public bool IsDeleted { get; set; }

		public bool IsFlagged { get; set; }

		public string FlaggedReason { get; set; }

		public string FlaggedByUserId { get; set; }

		public string DeletedByUserId { get; set; }

		public DateTime? FlaggedOn { get; set; }

		public DateTime? DeletedOn { get; set; }

		/// <summary>ADP: true when this row's cataloged values carry rgdp envelopes (M0128).</summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP companion column: envelope for Latitude while protected; typed column is nulled.</summary>
		public string ProtectedLatitudeEnvelope { get; set; }

		/// <summary>ADP companion column: envelope for Longitude while protected; typed column is nulled.</summary>
		public string ProtectedLongitudeEnvelope { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CallNoteId; }
			set { CallNoteId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CallNotes";

		[NotMapped]
		public string IdName => "CallNoteId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call", "User" };
	}
}
