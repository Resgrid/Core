using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("TrainingAttachments")]
	public class TrainingAttachment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingAttachmentId { get; set; }

		[Required]
		[ForeignKey("Training"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int TrainingId { get; set; }

		public virtual Training Training { get; set; }

		public string FileName { get; set; }

		public string FileType { get; set; }

		public byte[] Data { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return TrainingAttachmentId; }
			set { TrainingAttachmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingAttachments";

		[NotMapped]
		public string IdName => "TrainingAttachmentId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Training" };
	}
}
