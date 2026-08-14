using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Audit record of one run card activation against a call: which card, alarm level
	/// and mode ran, whether it auto-dispatched, and the full serialized
	/// DispatchRecommendationResult (picks, reasons, shortfalls, move-ups) that powers
	/// the "why were these resources selected?" panel.
	/// </summary>
	[Table("RunCardActivations")]
	public class RunCardActivation : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RunCardActivationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int CallId { get; set; }

		[Required]
		public int RunCardId { get; set; }

		[Required]
		public int AlarmLevel { get; set; }

		/// <summary>DispatchRecommendationModes value used for this activation.</summary>
		public int ModeUsed { get; set; }

		public bool WasAutoDispatched { get; set; }

		/// <summary>JSON-serialized DispatchRecommendationResult.</summary>
		public string ResultJson { get; set; }

		[Required]
		public DateTime CreatedOn { get; set; }

		/// <summary>Null for automated sources (email import, scheduled dispatch, etc.).</summary>
		public string CreatedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RunCardActivationId; }
			set { RunCardActivationId = (int)value; }
		}

		[NotMapped]
		public string TableName => "RunCardActivations";

		[NotMapped]
		public string IdName => "RunCardActivationId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
