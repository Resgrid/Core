using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One push-to-talk transmission on an incident voice channel: who keyed up, on which channel, and for how long.
	/// Written by clients when a PTT transmission ends; read back as the channel's transmission log (§3.4).
	/// </summary>
	public class VoiceTransmissionLog : IEntity
	{
		public string VoiceTransmissionLogId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>The on-demand incident channel this transmission occurred on.</summary>
		public string DepartmentVoiceChannelId { get; set; }

		public string UserId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		[NotMapped]
		public string TableName => "VoiceTransmissionLogs";

		[NotMapped]
		public string IdName => "VoiceTransmissionLogId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return VoiceTransmissionLogId; }
			set { VoiceTransmissionLogId = (string)value; }
		}

		[NotMapped]
		[JsonIgnore]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
