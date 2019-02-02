using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;
using ProtoBuf;
using Resgrid.Framework;

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
		
		[NotMapped]
		public object Id
		{
			get { return CallNoteId; }
			set { CallNoteId = (int)value; }
		}
	}

	public class CallNote_Mapping : EntityTypeConfiguration<CallNote>
	{
		public CallNote_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}