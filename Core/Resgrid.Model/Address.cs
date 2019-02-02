using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Addresses")]
	public class Address : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int AddressId { get; set; }

		[Required]
		[MaxLength(200)]
		[Display(Name = "Street Address")]
		[ProtoMember(2)]
		public string Address1 { get; set; }

		[Required]
		[MaxLength(100)]
		[ProtoMember(3)]
		public string City { get; set; }

		[Required]
		[MaxLength(50)]
		[ProtoMember(4)]
		public string State { get; set; }

		[MaxLength(50)]
		[ProtoMember(5)]
		public string PostalCode { get; set; }

		[Required]
		[MaxLength(100)]
		[ProtoMember(6)]
		public string Country { get; set; }

		public string FormatAddress()
		{
			return string.Format("{0} {1} {2} {3} {4}", Address1, City, State, PostalCode, Country);
		}

		[NotMapped]
		public object Id
		{
			get { return AddressId; }
			set { AddressId = (int)value; }
		}
	}
}