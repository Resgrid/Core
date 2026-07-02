namespace Resgrid.Model.CustomStates
{
	/// <summary>
	/// A single predefined button/option within a <see cref="CustomStateTemplate"/>. This is a pure
	/// code-defined shape (never persisted). When a user picks a template it is projected into a real
	/// <see cref="CustomStateDetail"/> that the user can then rename, recolor, reorder or delete before
	/// saving to their department.
	/// </summary>
	public class CustomStateTemplateDetail
	{
		/// <summary>Display order of the button within the set (0 based).</summary>
		public int Order { get; set; }

		/// <summary>The text shown inside the button (e.g. "Responding", "Transporting").</summary>
		public string ButtonText { get; set; }

		/// <summary>Background color of the button as a hex string (e.g. "#5CB85C").</summary>
		public string ButtonColor { get; set; }

		/// <summary>Text color of the button as a hex string (e.g. "#FFFFFF").</summary>
		public string TextColor { get; set; }

		/// <summary>
		/// The canonical system meaning of the status (drives availability/reporting/automations).
		/// See <see cref="ActionBaseTypes"/>.
		/// </summary>
		public ActionBaseTypes BaseType { get; set; } = ActionBaseTypes.None;

		/// <summary>Whether pressing the button prompts for / captures a note.</summary>
		public CustomStateNoteTypes NoteType { get; set; } = CustomStateNoteTypes.None;

		/// <summary>Whether the status can be associated with a Call, Station and/or POI.</summary>
		public CustomStateDetailTypes DetailType { get; set; } = CustomStateDetailTypes.None;

		/// <summary>Whether pressing the button requires/captures the device GPS location.</summary>
		public bool GpsRequired { get; set; }

		/// <summary>Optional time-to-live (minutes) for the status; 0 means none.</summary>
		public int Ttl { get; set; }

		/// <summary>
		/// Projects this template button into a real, unsaved <see cref="CustomStateDetail"/> entity so
		/// it can be edited and then persisted through the normal create pipeline.
		/// </summary>
		public CustomStateDetail ToCustomStateDetail()
		{
			return new CustomStateDetail
			{
				ButtonText = ButtonText,
				ButtonColor = ButtonColor,
				TextColor = TextColor,
				BaseType = (int)BaseType,
				NoteType = (int)NoteType,
				DetailType = (int)DetailType,
				GpsRequired = GpsRequired,
				Order = Order,
				TTL = Ttl,
				IsDeleted = false
			};
		}
	}
}
