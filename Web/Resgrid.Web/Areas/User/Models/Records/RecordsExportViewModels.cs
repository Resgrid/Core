using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	public class RecordsExportTemplatesIndexView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public List<RmsExportTemplate> Templates { get; set; } = new List<RmsExportTemplate>();
		public bool ProtectionEnforced { get; set; }
	}

	public class RecordsExportTemplateEditView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public string RmsExportTemplateId { get; set; }
		public string TemplateKey { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public int Format { get; set; } = (int)RmsExportFormat.Csv;
		public int Scope { get; set; } = (int)RmsExportScope.TriggeringRecord;
		public List<string> DefinitionKeys { get; set; } = new List<string>();
		public List<string> Columns { get; set; } = new List<string>();
		public bool IncludeNarrative { get; set; }
		public bool IncludeRestricted { get; set; }
		public bool AcknowledgeEgress { get; set; }
		public DateTime? EgressAcknowledgedOn { get; set; }
		public string EgressAcknowledgedByUserId { get; set; }
		public string FileNameTemplate { get; set; }
		public bool IncludeHeader { get; set; } = true;
		public string Delimiter { get; set; } = ",";
		public int ScheduleKind { get; set; } = (int)RmsExportScheduleKind.None;
		public int ScheduleHourLocal { get; set; } = 6;
		public int ScheduleDayOfWeek { get; set; } = 1;
		public int ScheduleDayOfMonth { get; set; } = 1;
		public int WindowDays { get; set; }
		public bool IsEnabled { get; set; } = true;
		public DateTime? NextRunOn { get; set; }
		public DateTime? LastRunOn { get; set; }
		public long RowVersion { get; set; }
		public bool CanIncludeRestricted { get; set; }
		public bool ProtectionEnforced { get; set; }
		public List<string> Warnings { get; set; } = new List<string>();
		public IReadOnlyList<RecordsExportField> Catalog { get; set; } = RecordsExportFieldCatalog.Fields;
		public List<KeyValuePair<string, string>> Definitions { get; set; } = new List<KeyValuePair<string, string>>();
		public bool IsNew => string.IsNullOrWhiteSpace(RmsExportTemplateId);

		public RmsExportTemplate ToTemplate()
		{
			return new RmsExportTemplate
			{
				RmsExportTemplateId = RmsExportTemplateId,
				TemplateKey = TemplateKey,
				Name = Name,
				Description = Description,
				Format = Format,
				Scope = Scope,
				DefinitionKeysCsv = string.Join(",", DefinitionKeys ?? new List<string>()),
				ColumnsJson = Newtonsoft.Json.JsonConvert.SerializeObject(Columns ?? new List<string>()),
				IncludeNarrative = IncludeNarrative,
				IncludeRestricted = IncludeRestricted,
				EgressAcknowledgedOn = EgressAcknowledgedOn,
				EgressAcknowledgedByUserId = EgressAcknowledgedByUserId,
				FileNameTemplate = FileNameTemplate,
				IncludeHeader = IncludeHeader,
				Delimiter = Delimiter == "tab" ? "\t" : Delimiter,
				ScheduleKind = ScheduleKind,
				ScheduleHourLocal = ScheduleHourLocal,
				ScheduleDayOfWeek = ScheduleDayOfWeek,
				ScheduleDayOfMonth = ScheduleDayOfMonth,
				WindowDays = WindowDays,
				IsEnabled = IsEnabled,
				RowVersion = RowVersion
			};
		}

		public static RecordsExportTemplateEditView From(RmsExportTemplate template)
		{
			var view = new RecordsExportTemplateEditView
			{
				RmsExportTemplateId = template.RmsExportTemplateId,
				TemplateKey = template.TemplateKey,
				Name = template.Name,
				Description = template.Description,
				Format = template.Format,
				Scope = template.Scope,
				DefinitionKeys = string.IsNullOrWhiteSpace(template.DefinitionKeysCsv) ? new List<string>() : new List<string>(template.DefinitionKeysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries)),
				IncludeNarrative = template.IncludeNarrative,
				IncludeRestricted = template.IncludeRestricted,
				EgressAcknowledgedOn = template.EgressAcknowledgedOn,
				EgressAcknowledgedByUserId = template.EgressAcknowledgedByUserId,
				FileNameTemplate = template.FileNameTemplate,
				IncludeHeader = template.IncludeHeader,
				Delimiter = template.Delimiter == "\t" ? "tab" : (template.Delimiter ?? ","),
				ScheduleKind = template.ScheduleKind,
				ScheduleHourLocal = template.ScheduleHourLocal,
				ScheduleDayOfWeek = template.ScheduleDayOfWeek,
				ScheduleDayOfMonth = template.ScheduleDayOfMonth,
				WindowDays = template.WindowDays,
				IsEnabled = template.IsEnabled,
				NextRunOn = template.NextRunOn,
				LastRunOn = template.LastRunOn,
				RowVersion = template.RowVersion
			};
			try { view.Columns = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(template.ColumnsJson ?? "[]") ?? new List<string>(); }
			catch (Newtonsoft.Json.JsonException) { view.Columns = new List<string>(); }
			return view;
		}
	}

	public class RecordsExportRunsView : RecordsBaseView
	{
		public Department Department { get; set; }
		public RmsExportTemplate Template { get; set; }
		public List<RmsExportRun> Runs { get; set; } = new List<RmsExportRun>();
	}
}
