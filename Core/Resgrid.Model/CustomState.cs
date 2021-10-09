using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("CustomStates")]
	[ProtoContract]
	public class CustomState : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int CustomStateId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[ProtoMember(3)]
		public int Type { get; set; }

		[Required]
		[ProtoMember(4)]
		public string Name { get; set; }

		[ProtoMember(5)]
		public string Description { get; set; }

		[ProtoMember(6)]
		public bool IsDeleted { get; set; }

		[ProtoMember(7)]
		public virtual ICollection<CustomStateDetail> Details { get; set; }

		public List<CustomStateDetail> GetActiveDetails()
		{
			if (Details != null && Details.Any())
				return Details.Where(x => x.IsDeleted == false).OrderBy(y => y.Order).ToList();

			return new List<CustomStateDetail>();
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CustomStateId; }
			set { CustomStateId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CustomStates";

		[NotMapped]
		public string IdName => "CustomStateId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Details" };
	}
}
