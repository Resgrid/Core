using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("DispatchProtocolQuestions")]
	public class DispatchProtocolQuestion : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int DispatchProtocolQuestionId { get; set; }

		[Required]
		[ForeignKey("Protocol"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DispatchProtocolId { get; set; }

		public virtual DispatchProtocol Protocol { get; set; }

		public string Question { get; set; }

		public virtual ICollection<DispatchProtocolQuestionAnswer> Answers { get; set; }

		[NotMapped]
		public object Id
		{
			get { return DispatchProtocolQuestionId; }
			set { DispatchProtocolQuestionId = (int)value; }
		}
	}
}
