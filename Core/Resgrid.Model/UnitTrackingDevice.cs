using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class UnitTrackingDevice : IEntity
	{
		[Key]
		[Required]
		[MaxLength(128)]
		public string UnitTrackingDeviceId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey(nameof(DepartmentId))]
		public virtual Department Department { get; set; }

		[Required]
		public int UnitId { get; set; }

		[ForeignKey(nameof(UnitId))]
		public virtual Unit Unit { get; set; }

		[MaxLength(200)]
		public string DisplayName { get; set; }

		[MaxLength(64)]
		public string ManufacturerKey { get; set; }

		[MaxLength(64)]
		public string ModelKey { get; set; }

		[Required]
		public int TransportType { get; set; }

		[MaxLength(64)]
		public string ProtocolKey { get; set; }

		[MaxLength(64)]
		public string PayloadAdapterKey { get; set; }

		[MaxLength(128)]
		public string DeviceIdentifier { get; set; }

		[MaxLength(128)]
		public string SecondaryIdentifier { get; set; }

		public bool IsEnabled { get; set; } = true;

		public bool IsDeleted { get; set; }

		public int SourcePriority { get; set; } = 100;

		public string AllowedSourceCidrs { get; set; }

		public DateTime? LastSeenOn { get; set; }

		public DateTime? LastPositionOn { get; set; }

		public DateTime? LastReceivedOn { get; set; }

		public int LastStatus { get; set; } = (int)UnitTrackingDeviceStatus.NeverSeen;

		[MaxLength(64)]
		public string LastErrorCode { get; set; }

		[MaxLength(128)]
		public string FirmwareVersion { get; set; }

		[Required]
		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => UnitTrackingDeviceId;
			set => UnitTrackingDeviceId = (string)value;
		}

		[NotMapped]
		public string TableName => "UnitTrackingDevices";

		[NotMapped]
		public string IdName => "UnitTrackingDeviceId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Unit" };
	}
}
