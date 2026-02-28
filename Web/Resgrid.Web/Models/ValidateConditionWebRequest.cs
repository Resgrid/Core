using System.ComponentModel.DataAnnotations;

namespace Resgrid.WebCore.Models
{
	/// <summary>JSON body for the ValidateCondition AJAX action.</summary>
	public sealed class ValidateConditionWebRequest
	{
		[Required]
		public string ConditionExpression { get; set; }

		public int TriggerEventType { get; set; }

		public string SamplePayloadJson { get; set; }
	}
}


