using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocolQuestionAnswers")]
	public class DispatchProtocolQuestionAnswer : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolQuestionAnswerId { get; set; }

		[Required]
		[ForeignKey("Question"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DispatchProtocolQuestionId { get; set; }

		public virtual DispatchProtocolQuestion Question { get; set; }

		public string Answer { get; set; }

		public int Weight { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatchProtocolQuestionAnswerId; }
			set { DispatchProtocolQuestionAnswerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DispatchProtocolQuestionAnswers";

		[NotMapped]
		public string IdName => "DispatchProtocolQuestionAnswerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Question" };
	}
}
