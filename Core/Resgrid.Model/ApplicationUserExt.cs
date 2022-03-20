using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("AspNetUsersExt")]
	public class ApplicationUserExt: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[MaxLength(256)]
		public string SecurityQuestion { get; set; }

		[MaxLength(128)]
		public string SecurityAnswer { get; set; }

		[MaxLength(128)]
		public string SecurityAnswerSalt { get; set; }

		[DefaultValue(typeof(DateTime), "1900-01-01 00:00:00.000")]
		public DateTime CreateDate { get; set; }

		public DateTime? LastActivityDate { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UserId; }
			set { UserId = (string)value; }
		}

		[NotMapped]
		public string TableName => "AspNetUsersExt";

		[NotMapped]
		public string IdName => "UserId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
