using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("TrainingQuestions")]
	public class TrainingQuestion : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingQuestionId { get; set; }

		[Required]
		[ForeignKey("Training"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int TrainingId { get; set; }

		public virtual Training Training { get; set; }

		public string Question { get; set; }

		public virtual ICollection<TrainingQuestionAnswer> Answers { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return TrainingQuestionId; }
			set { TrainingQuestionId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingQuestions";

		[NotMapped]
		public string IdName => "TrainingQuestionId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Training", "Answers" };
	}
}
