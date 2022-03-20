using System;
using System.Collections.Generic;
using Resgrid.Model.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ResourceOrderFills")]
	public class ResourceOrderFill : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ResourceOrderFillId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		[ForeignKey("OrderItem"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ResourceOrderItemId { get; set; }

		public virtual ResourceOrderItem OrderItem { get; set; }

		[ForeignKey("FillingUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string FillingUserId { get; set; }

		public virtual IdentityUser FillingUser { get; set; }

		public string Note { get; set; }

		public string ContactName { get; set; }

		public string ContactNumber { get; set; }

		public DateTime FilledOn { get; set; }

		public bool Accepted { get; set; }

		public DateTime? AcceptedOn { get; set; }

		[ForeignKey("LeadUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string LeadUserId { get; set; }

		public virtual IdentityUser LeadUser { get; set; }

		[ForeignKey("AcceptedUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string AcceptedUserId { get; set; }

		public virtual IdentityUser AcceptedUser { get; set; }

		public virtual ICollection<ResourceOrderFillUnit> Units { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceOrderFillId; }
			set { ResourceOrderFillId = (int)value; }
		}


		[NotMapped]
		public string TableName => "ResourceOrderFills";

		[NotMapped]
		public string IdName => "ResourceOrderFillId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "OrderItem", "FillingUser", "LeadUser", "AcceptedUser", "Units" };
	}
}
