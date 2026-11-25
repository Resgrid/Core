using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

		/// <summary>
		/// Any member of the department may view this training without being assigned to it (reference and optional
		/// material). Only assignees are tracked, notified and take the quiz. Off by default: a training is otherwise seen
		/// by its assignees and by whoever manages it.
		/// </summary>
		public bool OpenToAllMembers { get; set; }

		public virtual ICollection<TrainingQuestion> Questions { get; set; }

		public virtual ICollection<TrainingAttachment> Attachments { get; set; }

		public virtual ICollection<TrainingUser> Users { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingId; }
			set { TrainingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Trainings";

		[NotMapped]
		public string IdName => "TrainingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Questions", "Attachments", "Users" };
	}
}
