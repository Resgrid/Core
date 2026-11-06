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

		[ProtoMember(23)]
		public bool ChecklistsDisabled { get; set; }

		/// <summary>Department switch for the Business Operations module (Workforce &amp; Business Operations plan). Next free tag after this is 25.</summary>
		[ProtoMember(24)]
		public bool BusinessOperationsDisabled { get; set; }
		[ProtoMember(25)]
		public bool AiDisabled { get; set; }

		/// <summary>Incident Back Office LI-A + LI-B department switch (registry §4C). Default enabled: the add-on purchase is the opt-in (plan rev 4.1 §17).</summary>
		[ProtoMember(26)]
		public bool IncidentBackOfficeDisabled { get; set; }
		/// <summary>Incident Back Office LI-C switch; also requires the back office to be enabled.</summary>
		[ProtoMember(27)]
		public bool IncidentBusinessDisabled { get; set; }
		/// <summary>Incident Back Office LI-D coordination boards switch.</summary>
		[ProtoMember(28)]
		public bool IncidentCoordinationDisabled { get; set; }

		// Visibility switches for modules added after the original list. Each hides the module's navigation entry,
		// dashboard widgets and search shortcuts; none deletes data or changes permissions, and each defaults to shown.

		/// <summary>
		/// The Records module, independently of <see cref="LogsDisabled"/>; the Records.System flag still decides
		/// whether Records exists at all.
		/// </summary>
		[ProtoMember(29)]
		public bool RecordsDisabled { get; set; }
		[ProtoMember(30)]
		public bool ContactsDisabled { get; set; }
		[ProtoMember(31)]
		public bool CertificationsDisabled { get; set; }
		/// <summary>Deployments; the Operations.Deployments flag still decides whether the module exists at all.</summary>
		[ProtoMember(32)]
		public bool DeploymentsDisabled { get; set; }
		/// <summary>Department chat; the Chat.System flag still decides whether chat exists at all. Next free tag after this is 34.</summary>
		[ProtoMember(33)]
		public bool ChatDisabled { get; set; }
	}
}
