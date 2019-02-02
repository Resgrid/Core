using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DepartmentFiles")]
	public class DepartmentFile : IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public Guid DepartmentFileId { get; set; }

		[Required]
		[ProtoMember(2)]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(3)]
		public int Type { get; set; }

		[Required]
		[ProtoMember(4)]
		public string FileName { get; set; }

		[Required]
		[ProtoMember(5)]
		public byte[] Data { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DepartmentFileId; }
			set { DepartmentFileId = (Guid)value; }
		}
	}
}