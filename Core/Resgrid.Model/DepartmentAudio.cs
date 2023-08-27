using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Resgrid.Model
{
	public class DepartmentAudio : IEntity
	{
		public string DepartmentAudioId { get; set; }

		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int DepartmentAudioType { get; set; }

		public string Name { get; set; }

		public string Data { get; set; }

		public string Type { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentAudioId; }
			set { DepartmentAudioId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentAudios";

		[NotMapped]
		public string IdName => "DepartmentAudioId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
