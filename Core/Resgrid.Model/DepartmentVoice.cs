using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Resgrid.Model
{
	public class DepartmentVoice : IEntity
	{
		public string DepartmentVoiceId { get; set; }

		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int StartConferenceNumber { get; set; }

		public virtual List<DepartmentVoiceChannel> Channels { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentVoiceId; }
			set { DepartmentVoiceId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentVoices";

		[NotMapped]
		public string IdName => "DepartmentVoiceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Channels" };
	}
}
