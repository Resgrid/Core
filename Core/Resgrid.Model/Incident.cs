using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Incidents")]
	public class Incident : IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int IncidentId { get; set; }

		[Required]
		[ForeignKey("Call"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int CallId { get; set; }

		public virtual Call Call { get; set; }

		[Required]
		[ForeignKey("Definition"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(3)]
		public int CommandDefinitionId { get; set; }

		public virtual CommandDefinition Definition { get; set; }

		public string Name { get; set; }

		public DateTime Start { get; set; }

		public DateTime? End { get; set; }

		public int State { get; set; }

		public virtual ICollection<IncidentLog> Logs { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentId; }
			set { IncidentId = (int)value; }
		}


		[NotMapped]
		public string TableName => "Incidents";

		[NotMapped]
		public string IdName => "IncidentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Call", "Definition", "Logs" };
	}
}
