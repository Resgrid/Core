using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("TrainingUsers")]
	public class TrainingUser : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingUserId { get; set; }

		[Required]
		[ForeignKey("Training"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int TrainingId { get; set; }

		public virtual Training Training { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		public bool Viewed { get; set; }

		public DateTime? ViewedOn { get; set; }

		public bool Complete { get; set; }

		public DateTime? CompletedOn { get; set; }

		public double Score { get; set; }

		[NotMapped]
		public object Id
		{
			get { return TrainingUserId; }
			set { TrainingUserId = (int)value; }
		}
	}
}