using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Notes")]
	public class Note: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int NoteId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public string UserId { get; set; }

		[Required]
		public string Title { get; set; }

		[Required]
		public string Body { get; set; }

		[Required]
		public bool IsAdminOnly { get; set; }

		public string Color { get; set; }

		public string Category { get; set; }

		public DateTime? ExpiresOn { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return NoteId; }
			set { NoteId = (int)value; }
		}
	}
}