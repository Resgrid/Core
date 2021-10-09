using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[Table("ResourceOrderSettings")]
	public class ResourceOrderSetting : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ResourceOrderSettingId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public int Visibility { get; set; }

		public bool DoNotReceiveOrders { get; set; }

		[ForeignKey("RoleAllowedToFulfilOrders"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? RoleAllowedToFulfilOrdersRoleId { get; set; }

		public PersonnelRole RoleAllowedToFulfilOrders { get; set; }

		public bool LimitStaffingLevelToReceiveNotifications { get; set; }

		public int AllowedStaffingLevelToReceiveNotifications { get; set; }

		public string DefaultResourceOrderManagerUserId { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Latitude { get; set; }

		[DecimalPrecision(10, 7)]
		public decimal? Longitude { get; set; }

		[DefaultValue(500)]
		public int Range { get; set; }

		public string BoundryGeofence { get; set; }

		public string TargetDepartmentType { get; set; }

		public bool AutomaticFillAcceptance { get; set; }

		public string ImportEmailCode { get; set; }

		public bool NotifyUsers { get; set; }

		public string UserIdsToNotifyOnOrders { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ResourceOrderSettingId; }
			set { ResourceOrderSettingId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ResourceOrderSettings";

		[NotMapped]
		public string IdName => "ResourceOrderSettingId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "RoleAllowedToFulfilOrders" };
	}


	public enum ResourceOrderVisibilites
	{
		Range = 0,
		//Geofence	 = 1,
		Linked = 2,
		Unrestricted = 3
	}
}
