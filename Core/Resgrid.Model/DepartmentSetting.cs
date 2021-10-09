using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("DepartmentSettings")]
	[ProtoContract]
	public class DepartmentSetting : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentSettingId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public Department Department { get; set; }

		[Required]
		[ProtoMember(3)]
		public int SettingType { get; set; }

		[Required]
		[ProtoMember(4)]
		public string Setting { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentSettingId; }
			set { DepartmentSettingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentSettings";

		[NotMapped]
		public string IdName => "DepartmentSettingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
