using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class DepartmentModuleSettings
	{
		// Calls, Personnel and Units are always enabled

		[ProtoMember(1)]
		public bool MessagingDisabled { get; set; }
		[ProtoMember(2)]
		public string MessagingNameOverride { get; set; }

		[ProtoMember(3)]
		public bool MappingDisabled { get; set; }
		[ProtoMember(4)]
		public string MappingNameOverride { get; set; }

		[ProtoMember(5)]
		public bool ShiftsDisabled { get; set; }
		[ProtoMember(6)]
		public string ShiftsNameOverride { get; set; }

		[ProtoMember(7)]
		public bool LogsDisabled { get; set; }
		[ProtoMember(8)]
		public string LogsNameOverride { get; set; }

		[ProtoMember(9)]
		public bool ReportsDisabled { get; set; }
		[ProtoMember(10)]
		public string ReportsNameOverride { get; set; }

		[ProtoMember(11)]
		public bool DocumentsDisabled { get; set; }
		[ProtoMember(12)]
		public string DocumentsNameOverride { get; set; }

		[ProtoMember(13)]
		public bool CalendarDisabled { get; set; }
		[ProtoMember(14)]
		public string CalendarNameOverride { get; set; }

		[ProtoMember(15)]
		public bool NotesDisabled { get; set; }
		[ProtoMember(16)]
		public string NotesNameOverride { get; set; }

		[ProtoMember(17)]
		public bool TrainingDisabled { get; set; }
		[ProtoMember(18)]
		public string TrainingNameOverride { get; set; }

		[ProtoMember(19)]
		public bool InventoryDisabled { get; set; }
		[ProtoMember(20)]
		public string InventoryNameOverride { get; set; }

		[ProtoMember(21)]
		public bool MaintenanceDisabled { get; set; }
		[ProtoMember(22)]
		public string MaintenanceNameOverride { get; set; }
	}
}
