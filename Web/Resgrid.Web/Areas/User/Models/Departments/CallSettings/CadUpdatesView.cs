using System;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Departments.CallSettings
{
	/// <summary>Department → Call Settings → Updates from your CAD (cad-email-call-updates-plan.md).</summary>
	public class CadUpdatesView
	{
		public bool Enabled { get; set; }
		public int IdSource { get; set; }
		public string IdLabel { get; set; }
		public string IdPattern { get; set; }
		public int IdSearchIn { get; set; }
		public int LocationPolicy { get; set; }
		public int PriorityPolicy { get; set; }
		public int TypePolicy { get; set; }
		public int NamePolicy { get; set; }
		public int NaturePolicy { get; set; }
		public int NotesPolicy { get; set; } = (int)CadTextPolicies.Append;
		public int RepageWhen { get; set; } = (int)CadRepageRules.LocationOrPriorityChanged;
		public bool PageAddedResources { get; set; } = true;
		public bool DispatcherEditsWin { get; set; } = true;
		public int ClosedMatchMinutes { get; set; } = EmailCallUpdateConfig.DefaultClosedMatchMinutes;
		public string ClosePhrases { get; set; }
		public string CancelPhrases { get; set; }
		public bool NotifyOnClose { get; set; } = true;

		public string LabelCallId { get; set; }
		public string LabelName { get; set; }
		public string LabelNature { get; set; }
		public string LabelAddress { get; set; }
		public string LabelCity { get; set; }
		public string LabelCrossStreets { get; set; }
		public string LabelCoordinates { get; set; }
		public string LabelLatitude { get; set; }
		public string LabelLongitude { get; set; }
		public string LabelType { get; set; }
		public string LabelPriority { get; set; }
		public string LabelUnits { get; set; }
		public string LabelNotes { get; set; }

		/// <summary>The department's email format (<see cref="CallEmailTypes"/>).</summary>
		public int FormatType { get; set; }

		public string FormatName { get; set; }

		/// <summary>The page shows the format's built-in preset (nothing saved yet).</summary>
		public bool IsBuiltInPreset { get; set; }

		/// <summary>The format has a built-in preset the department can go back to.</summary>
		public bool FormatHasPreset { get; set; }

		/// <summary>The format reads a call ID of its own.</summary>
		public bool FormatReadsCallId { get; set; }

		public string Message { get; set; }
		public string ErrorMessage { get; set; }

		public static CadUpdatesView From(EmailCallUpdateConfig config, EmailLabeledFieldsConfig labels, int formatType)
		{
			config ??= new EmailCallUpdateConfig();
			labels ??= new EmailLabeledFieldsConfig();

			return new CadUpdatesView
			{
				Enabled = config.Enabled,
				IdSource = (int)config.GetIdSource(),
				IdLabel = config.IdLabel,
				IdPattern = config.IdPattern,
				IdSearchIn = (int)config.GetIdSearchIn(),
				LocationPolicy = (int)config.GetLocationPolicy(),
				PriorityPolicy = (int)config.GetPriorityPolicy(),
				TypePolicy = (int)config.GetTypePolicy(),
				NamePolicy = (int)config.GetNamePolicy(),
				NaturePolicy = (int)config.GetNaturePolicy(),
				NotesPolicy = (int)config.GetNotesPolicy(),
				RepageWhen = (int)config.GetRepageWhen(),
				PageAddedResources = config.PageAddedResources,
				DispatcherEditsWin = config.DispatcherEditsWin,
				ClosedMatchMinutes = config.GetClosedMatchMinutes(),
				ClosePhrases = config.ClosePhrases,
				CancelPhrases = config.CancelPhrases,
				NotifyOnClose = config.NotifyOnClose,
				LabelCallId = labels.CallId,
				LabelName = labels.Name,
				LabelNature = labels.Nature,
				LabelAddress = labels.Address,
				LabelCity = labels.City,
				LabelCrossStreets = labels.CrossStreets,
				LabelCoordinates = labels.Coordinates,
				LabelLatitude = labels.Latitude,
				LabelLongitude = labels.Longitude,
				LabelType = labels.Type,
				LabelPriority = labels.Priority,
				LabelUnits = labels.Units,
				LabelNotes = labels.Notes,
				FormatType = formatType,
				FormatName = Enum.IsDefined(typeof(CallEmailTypes), formatType) ? ((CallEmailTypes)formatType).ToString() : formatType.ToString(),
				IsBuiltInPreset = config.IsBuiltInPreset,
				FormatHasPreset = EmailCallUpdatePresets.HasPreset(formatType),
				FormatReadsCallId = EmailCallUpdatePresets.FormatReadsCallId(formatType, labels)
			};
		}

		public EmailCallUpdateConfig ToConfig()
		{
			return new EmailCallUpdateConfig
			{
				Enabled = Enabled,
				IdSource = IdSource,
				IdLabel = IdLabel,
				IdPattern = IdPattern,
				IdSearchIn = IdSearchIn,
				LocationPolicy = LocationPolicy,
				PriorityPolicy = PriorityPolicy,
				TypePolicy = TypePolicy,
				NamePolicy = NamePolicy,
				NaturePolicy = NaturePolicy,
				NotesPolicy = NotesPolicy,
				RepageWhen = RepageWhen,
				PageAddedResources = PageAddedResources,
				DispatcherEditsWin = DispatcherEditsWin,
				ClosedMatchMinutes = ClosedMatchMinutes,
				ClosePhrases = ClosePhrases,
				CancelPhrases = CancelPhrases,
				NotifyOnClose = NotifyOnClose
			}.Normalized();
		}

		public EmailLabeledFieldsConfig ToLabels()
		{
			return new EmailLabeledFieldsConfig
			{
				CallId = LabelCallId,
				Name = LabelName,
				Nature = LabelNature,
				Address = LabelAddress,
				City = LabelCity,
				CrossStreets = LabelCrossStreets,
				Coordinates = LabelCoordinates,
				Latitude = LabelLatitude,
				Longitude = LabelLongitude,
				Type = LabelType,
				Priority = LabelPriority,
				Units = LabelUnits,
				Notes = LabelNotes
			}.Normalized();
		}
	}

	/// <summary>The test box: a pasted email plus the page's unsaved settings.</summary>
	public class CadUpdatesPreviewInput : CadUpdatesView
	{
		public string TestSubject { get; set; }
		public string TestBody { get; set; }
	}
}
