using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Resgrid.Model
{
	public class DepartmentVoiceUser : IEntity
	{
		public string DepartmentVoiceUserId { get; set; }

		public string DepartmentVoiceId { get; set; }

		public virtual DepartmentVoice DepartmentVoice { get; set; }

		public string UserId { get; set; }

		public string SystemUserId { get; set; }

		public string SystemDeviceId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentVoiceUserId; }
			set { DepartmentVoiceUserId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentVoiceUsers";

		[NotMapped]
		public string IdName => "DepartmentVoiceUserId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DepartmentVoice" };
	}
}
