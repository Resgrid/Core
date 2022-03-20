using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("TrainingQuestionAnswers")]
	public class TrainingQuestionAnswer : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingQuestionAnswerId { get; set; }

		[Required]
		[ForeignKey("Question"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int TrainingQuestionId { get; set; }

		public virtual TrainingQuestion Question { get; set; }

		public string Answer { get; set; }

		public bool Correct { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingQuestionAnswerId; }
			set { TrainingQuestionAnswerId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingQuestionAnswers";

		[NotMapped]
		public string IdName => "TrainingQuestionAnswerId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Question" };
	}
}
