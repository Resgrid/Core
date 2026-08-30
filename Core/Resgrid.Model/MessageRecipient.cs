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
	[Table("MessageRecipients")]
	public class MessageRecipient : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int MessageRecipientId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int MessageId { get; set; }

		[ForeignKey("MessageId")]
		public virtual Message Message { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		[ForeignKey("UserId")]
		[ProtoMember(4)]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(5)]
		public bool IsDeleted { get; set; }

		[ProtoMember(6)]
		public DateTime? ReadOn { get; set; }

		[ProtoMember(7)]
		public string Response { get; set; }

		[ProtoMember(8)]
		public string Note { get; set; }

		[ProtoMember(9)]
		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[ProtoMember(10)]
		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		/// <summary>ADP: true when this row's cataloged values carry rgdp envelopes (M0129; inert until catalog v2).</summary>
		[ProtoMember(11)]
		public bool IsProtected { get; set; }

		/// <summary>ADP companion column: envelope for Latitude while protected; typed column is nulled.</summary>
		[ProtoMember(12)]
		public string ProtectedLatitudeEnvelope { get; set; }

		/// <summary>ADP companion column: envelope for Longitude while protected; typed column is nulled.</summary>
		[ProtoMember(13)]
		public string ProtectedLongitudeEnvelope { get; set; }

		/// <summary>
		/// The department that owns this row (M0137). Messages are addressed to users, and a user
		/// can belong to several departments and move between them, so ownership is resolved ONCE
		/// at send time and frozen here rather than derived through a join: the ADP envelope AAD
		/// binds the department, and a derived value that later moves would orphan every envelope
		/// written under it. Null on historic rows the M0137 backfill could not attribute, which
		/// keeps them out of encryption instead of encrypting them under a guess.
		/// </summary>
		[ProtoMember(14)]
		public int? DepartmentId { get; set; }

		/// <summary>
		/// Machine metadata for prompts that can be answered from any text channel — which calendar
		/// item or poll this recipient row belongs to (M0138). It used to share the Note column with
		/// the member's own words, which is what kept Note out of the protected-field catalog: every
		/// reader of this token runs WITHOUT a Protected Data Grant (the chatbot inbound resolver
		/// most of all), and the broker's workload lane cannot decrypt. Deliberately NOT cataloged:
		/// it is a row pointer and says nothing about a person.
		/// </summary>
		[ProtoMember(15)]
		public string PromptMetadata { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return MessageRecipientId; }
			set { MessageRecipientId = (int)value; }
		}

		[NotMapped]
		public string TableName => "MessageRecipients";

		[NotMapped]
		public string IdName => "MessageRecipientId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Message", "User" };
	}
}
