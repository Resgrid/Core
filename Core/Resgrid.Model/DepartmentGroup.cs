using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("DepartmentGroups")]
	public class DepartmentGroup : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentGroupId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		//[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		[ProtoMember(3)]
		public int? Type { get; set; }

		[ProtoMember(4)]
		public int? AddressId { get; set; }

		[ProtoMember(13)]
		public Address Address { get; set; }

		[ProtoMember(5)]
		public int? ParentDepartmentGroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup Parent { get; set; }

		public virtual ICollection<DepartmentGroupMember> Members { get; set; }

		public virtual ICollection<DepartmentGroup> Children { get; set; }

		[Required]
		[MaxLength(50)]
		[ProtoMember(6)]
		public string Name { get; set; }

		[ProtoMember(7)]
		public string Geofence { get; set; }

		[ProtoMember(8)]
		public string GeofenceColor { get; set; }

		[ProtoMember(9)]
		public string DispatchEmail { get; set; }

		[ProtoMember(10)]
		public string MessageEmail { get; set; }

		[ProtoMember(11)]
		public string Latitude { get; set; }

		[ProtoMember(12)]
		public string Longitude { get; set; }

		public string What3Words { get; set; }

		public bool DispatchToPrinter { get; set; }

		public string PrinterData { get; set; }

		public bool DispatchToFax { get; set; }

		public string FaxNumber { get; set; }

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return DepartmentGroupId; }
			set { DepartmentGroupId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentGroups";

		[NotMapped]
		public string IdName => "DepartmentGroupId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Parent", "Members", "Children", "Address" };

		public bool IsUserGroupAdmin(string userId)
		{
			if (Members == null)
				return false;

			var admins = from m in Members
			             where m.IsAdmin == true
			             select m;

			foreach (var admin in admins)
			{
				if (admin.UserId == userId)
					return true;
			}

			return false;
		}

		public bool IsUserInGroup(string userId)
		{
			if (Members == null)
				return false;

			foreach (var member in Members)
			{
				if (member.UserId == userId)
					return true;
			}

			return false;
		}
	}
}
