using Newtonsoft.Json;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DepartmentCallPriorities")]
	public class DepartmentCallPriority : IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DepartmentCallPriorityId { get; set; }

		[Required]
		[ProtoMember(2)]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[ProtoMember(3)]
		public string Name { get; set; }

		[ProtoMember(4)]
		public string Color { get; set; }

		[ProtoMember(5)]
		public int Sort { get; set; }

		[ProtoMember(6)]
		public bool IsDeleted { get; set; }

		[ProtoMember(7)]
		public bool IsDefault { get; set; }

		public byte[] PushNotificationSound { get; set; }

		public byte[] IOSPushNotificationSound { get; set; }

		public byte[] ShortNotificationSound { get; set; }

		[ProtoMember(8)]
		public bool DispatchPersonnel { get; set; }

		[ProtoMember(9)]
		public bool DispatchUnits { get; set; }

		[ProtoMember(10)]
		public bool ForceNotifyAllPersonnel { get; set; }

		[ProtoMember(11)]
		public int Tone { get; set; }

		[NotMapped]
		public bool IsSystemPriority { get; set; }
		
		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentCallPriorityId; }
			set { DepartmentCallPriorityId = (int)value; }
		}

		
		[NotMapped]
		public string TableName => "DepartmentCallPriorities";

		[NotMapped]
		public string IdName => "DepartmentCallPriorityId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "IsSystemPriority" };
	}
}
