using System;
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

		/// <summary>When set, this is an on-demand tactical channel scoped to a specific Call/incident (§3.4).</summary>
		public int? CallId { get; set; }

		/// <summary>True for IC-created on-demand incident channels (vs. standing department channels).</summary>
		public bool IsOnDemand { get; set; }

		/// <summary>When the on-demand incident channel was closed (soft-close at incident close).</summary>
		public DateTime? ClosedOn { get; set; }

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
