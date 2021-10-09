using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Ranks")]
	public class Rank : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int RankId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }
		public virtual Department Department { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public int SortWeight { get; set; }
		public int TradeWeight { get; set; }
		public byte[] Image { get; set; }
		public string Color { get; set; }
		public virtual ICollection<DepartmentMember> Members { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RankId; }
			set { RankId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Ranks";

		[NotMapped]
		public string IdName => "RankId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Members" };
	}
}
