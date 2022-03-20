using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	public class DepartmentVoiceChannel : IEntity
	{
		public string DepartmentVoiceChannelId { get; set; }

		public string DepartmentVoiceId { get; set; }

		public virtual DepartmentVoice DepartmentVoice { get; set; }

		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public string Name { get; set; }

		public string SystemConferenceId { get; set; }

		public string SystemCallflowId { get; set; }

		public int ConferenceNumber { get; set; }

		public bool IsDefault { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentVoiceChannelId; }
			set { DepartmentVoiceChannelId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentVoiceChannels";

		[NotMapped]
		public string IdName => "DepartmentVoiceChannelId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "DepartmentVoice", "Department" };
	}
}
