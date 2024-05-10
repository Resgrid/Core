using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("QueueItems")]
	public class QueueItem : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int QueueItemId { get; set; }

		[ProtoMember(2)]
		public int QueueType { get; set; }

		[ProtoMember(3)]
		public string SourceId { get; set; }	// UserId or DepartmentId

		[ProtoMember(4)]
		public DateTime QueuedOn { get; set; }

		[ProtoMember(5)]
		public DateTime? PickedUp { get; set; }

		[ProtoMember(6)]
		public DateTime? CompletedOn { get; set; }

		[ProtoMember(8)]
		public DateTime? ToBeCompletedOn { get; set; }

		[ProtoMember(7)]
		public string Receipt { get; set; }

		[ProtoMember(9)]
		public string Reason { get; set; }

		[ProtoMember(10)]
		public string QueuedByUserId { get; set; }

		[ProtoMember(11)]
		public string Data { get; set; }

		[ProtoMember(12)]
		public int ReminderCount { get; set; }

		[NotMapped]
		public int DequeueCount { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return QueueItemId; }
			set { QueueItemId = (int)value; }
		}

		[NotMapped]
		public string TableName => "QueueItems";

		[NotMapped]
		public string IdName => "QueueItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DequeueCount" };
	}
}
