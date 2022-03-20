using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Linq;
using ProtoBuf;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("Departments")]
	public class Department : IEntity
	{
		public Department()
		{
			AdminUsers = new List<string>();
		}

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(150)]
		[ProtoMember(2)]
		public string Name { get; set; }

		[MaxLength(4)]
		[ProtoMember(3)]
		public string Code { get; set; }

		[Required]
		[ProtoMember(4)]
		public string ManagingUserId { get; set; }

		[ForeignKey("ManagingUserId")]
		[ProtoMember(5)]
		public virtual IdentityUser ManagingUser { get; set; }

		[ProtoMember(6)]
		public int? AddressId { get; set; }

		[ForeignKey("AddressId")]
		[ProtoMember(7)]
		public virtual Address Address { get; set; }

		[ProtoMember(8)]
		public bool ShowWelcome { get; set; }

		[MaxLength(150)]
		[ProtoMember(9)]
		public string DepartmentType { get; set; }

		[ProtoMember(10)]
		public string TimeZone { get; set; }

		[ProtoMember(11)]
		public string ApiKey { get; set; }

		[ProtoMember(12)]
		public string PublicApiKey { get; set; }

		[MaxLength(20)]
		[ProtoMember(13)]
		public string SharedSecret { get; set; }

		[ProtoMember(14)]
		public DateTime? CreatedOn { get; set; }

		[ProtoMember(15)]
		public DateTime? UpdatedOn { get; set; }

		[ProtoMember(16)]
		public int? ReferringDepartmentId { get; set; }

		[ProtoMember(17)]
		public string AffiliateCode { get; set; }

		[ProtoMember(19)]
		public bool? Use24HourTime { get; set; }

		[ProtoMember(20)]
		public string LinkCode { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentId; }
			set { DepartmentId = (int)value; }
		}

		[NotMapped]
		public string TableName => "Departments";

		[NotMapped]
		public string IdName => "DepartmentId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ManagingUser", "Address", "Members", "AdminUsers" };

		[ProtoMember(18)]
		[NotMapped]
		public List<string> AdminUsers { get; set; }

		//[ProtoMember(21)]
		[NotMapped]
		public List<DepartmentMember> Members { get; set; }

		public bool IsUserAnAdmin(string userId)
		{
			if (userId == ManagingUserId)
				return true;

			if (Members != null && Members.Any())
				return Members.Any(x => x.IsAdmin.GetValueOrDefault() && x.UserId == userId);

			var user = from u in AdminUsers
								 where u == userId
								 select u;

			if (user.Any())
				return true;

			return false;
		}

		public bool IsUserInDepartment(string userId)
		{
			if (userId == ManagingUserId)
				return true;

			if (Members != null && Members.Any())
				return Members.Any(x => x.UserId == userId);

			return false;
		}
	}
}
