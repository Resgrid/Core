namespace Resgrid.Model
{
	/// <summary>Kind of chat channel; drives audience resolution and provisioning (see ChatPermissionService).</summary>
	public enum ChatChannelType
	{
		DirectMessage = 0,
		AdHocGroup = 1,
		DepartmentDefault = 2,
		GroupDefault = 3,
		CustomLocked = 4,
		Incident = 5,
		IncidentLane = 6,
		IncidentCommand = 7,
		Chatbot = 8,

		/// <summary>
		/// Incident Commander plus every lane's primary and secondary lead — command talking to the
		/// people running the lanes, without the lane crews. Membership is derived live from the lanes,
		/// so demoting a lead removes their access on the next check.
		/// </summary>
		IncidentLeads = 9,

		/// <summary>
		/// The incident's line to the dispatch desk: everyone working the incident on one side, every
		/// dispatch-authorized user on the other. Per-call rather than department-wide so dispatchers can
		/// tell which incident is talking to them, and audience-wide on the dispatch side so whichever
		/// dispatcher is on shift picks it up.
		/// </summary>
		IncidentDispatch = 10
	}

	/// <summary>Who a chat participant is: a person, a unit-shared identity ("Engine 6"), or the chatbot.</summary>
	public enum ChatParticipantType
	{
		User = 0,
		Unit = 1,
		Bot = 2
	}

	/// <summary>Access rule kinds for CustomLocked channels; rules are OR-evaluated.</summary>
	public enum ChatAccessRuleType
	{
		GroupMembership = 0,
		Role = 1,
		User = 2
	}

	public enum ChatMessageType
	{
		Text = 0,
		Image = 1,
		Gif = 2,
		Location = 3,
		System = 4,
		Bot = 5
	}

	/// <summary>Urgent messages provision per-user acknowledgment rows and override channel mutes.</summary>
	public enum ChatMessagePriority
	{
		Normal = 0,
		Urgent = 1
	}

	/// <summary>Per-member, per-channel notification preference. Default resolves to All.</summary>
	public enum ChatNotificationPreference
	{
		Default = 0,
		All = 1,
		MentionsOnly = 2,
		Muted = 3
	}

	public enum ChatMentionType
	{
		User = 0,
		Unit = 1,
		Role = 2,
		Group = 3,
		Everyone = 4
	}

	public enum ChatFlagReason
	{
		Other = 0,
		Inappropriate = 1,
		Harassment = 2,
		Spam = 3,
		SensitiveInformation = 4,
		PolicyViolation = 5
	}

	public enum ChatFlagStatus
	{
		Open = 0,
		Reviewed = 1,
		Dismissed = 2,
		ActionTaken = 3
	}

	public enum ChatModerationActionType
	{
		DeleteMessage = 0,
		MuteUser = 1,
		UnmuteUser = 2,
		BanUser = 3,
		UnbanUser = 4,
		LockChannel = 5,
		UnlockChannel = 6,
		ArchiveChannel = 7,
		UnarchiveChannel = 8,
		PinMessage = 9,
		UnpinMessage = 10,
		ResolveFlag = 11,
		ExportRequested = 12,
		ExportDownloaded = 13
	}

	/// <summary>Why a ChatMessageEdits history row exists.</summary>
	public enum ChatMessageEditType
	{
		Edit = 0,
		ModeratorDelete = 1,
		SenderDelete = 2
	}

	public enum ChatExportFormat
	{
		Json = 0,
		Csv = 1,
		Zip = 2
	}

	public enum ChatExportStatus
	{
		Queued = 0,
		Running = 1,
		Complete = 2,
		Failed = 3
	}
}
