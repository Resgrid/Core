using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocolTriggers")]
	public class DispatchProtocolTrigger : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolTriggerId { get; set; }

		[Required]
		[ForeignKey("Protocol"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DispatchProtocolId { get; set; }

		public virtual DispatchProtocol Protocol { get; set; }

		public int Type { get; set; }

		public DateTime? StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public int? Priority { get; set; }

		/// <summary>
		/// The call type's name, kept for display. Matching uses <see cref="CallTypeId"/>; a trigger with neither (null id and
		/// no name) is the "no type" trigger, which matches a call that has no type.
		/// </summary>
		public string CallType { get; set; }

		/// <summary>
		/// The call type the trigger matches (M0336). Null with a blank <see cref="CallType"/> means "no type"; null with a name
		/// is a trigger written before M0336 whose type no longer exists, matched by name as before.
		/// </summary>
		public int? CallTypeId { get; set; }

		/// <summary>Stored but not evaluated: geofence triggers are planned (P4) as "inside station or group geofence X".</summary>
		public string Geofence { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolTriggerId; }
			set { DispatchProtocolTriggerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocolTriggers";

		[NotMapped]
		public string IdName => "DispatchProtocolTriggerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Protocol" };
	}
}
