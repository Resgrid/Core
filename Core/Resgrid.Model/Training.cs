using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;

namespace Resgrid.Model
{
	[Table("Trainings")]
	public class Training : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int TrainingId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Description { get; set; }

		[Required]
		public string TrainingText { get; set; }

		public double MinimumScore { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ToBeCompletedBy { get; set; }

		public string UsersToAdd { get; set; }

		public string GroupsToAdd { get; set; }

		public string RolesToAdd { get; set; }

		public DateTime? Notified { get; set; }

		public virtual ICollection<TrainingQuestion> Questions { get; set; }

		public virtual ICollection<TrainingAttachment> Attachments { get; set; }

		public virtual ICollection<TrainingUser> Users { get; set; }

		[NotMapped]
		public object Id
		{
			get { return TrainingId; }
			set { TrainingId = (int)value; }
		}
	}

	public class Training_Mapping : EntityTypeConfiguration<Training>
	{
		public Training_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
		}
	}
}