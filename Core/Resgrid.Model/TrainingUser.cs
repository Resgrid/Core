using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

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
		[JsonIgnore]
		public object IdValue
		{
			get { return TrainingUserId; }
			set { TrainingUserId = (int)value; }
		}

		[NotMapped]
		public string TableName => "TrainingUsers";

		[NotMapped]
		public string IdName => "TrainingUserId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Training", "User" };
	}
}
