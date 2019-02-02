using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("QueueItems")]
	public class QueueItem: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int QueueItemId { get; set; }

		[ProtoMember(2)]
		public int QueueType { get; set; }

		[ProtoMember(3)]
		public string SourceId { get; set; }

		[ProtoMember(4)]
		public DateTime QueuedOn { get; set; }

		[ProtoMember(5)]
		public DateTime? PickedUp { get; set; }

		[ProtoMember(6)]
		public DateTime? CompletedOn { get; set; }

		[ProtoMember(7)]
		public string Receipt { get; set; }

		[NotMapped]
		public int DequeueCount { get; set; }

		[NotMapped]
		public object Id
		{
			get { return QueueItemId; }
			set { QueueItemId = (int)value; }
		}
	}
}
