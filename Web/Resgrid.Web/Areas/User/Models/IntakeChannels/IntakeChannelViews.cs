using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.IntakeChannels
{
	/// <summary>Department → Intake Channels list.</summary>
	public class IntakeChannelsIndexView
	{
		public bool FeatureEnabled { get; set; }

		public List<IntakeChannelRow> Channels { get; set; } = new List<IntakeChannelRow>();

		public string Message { get; set; }

		public string ErrorMessage { get; set; }
	}

	public class IntakeChannelRow
	{
		public string IntakeChannelId { get; set; }

		public string Name { get; set; }

		public IntakeChannelTypes ChannelType { get; set; }

		public string Address { get; set; }

		public IntakeProcessingModes ProcessingMode { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Messages this calendar month (UTC); voice: calls taken.</summary>
		public int ThisMonth { get; set; }
	}

	/// <summary>Create / edit form. Webhook channels never take an address (the token is generated).</summary>
	public class IntakeChannelEditView
	{
		public string IntakeChannelId { get; set; }

		public string Name { get; set; }

		public int ChannelType { get; set; }

		public string Address { get; set; }

		public int ProcessingMode { get; set; } = (int)IntakeProcessingModes.Template;

		public int? TemplateFormat { get; set; }

		public string AllowedSenders { get; set; }

		public int? DefaultPriority { get; set; }

		public int? DefaultCallTypeId { get; set; }

		public bool SendAcknowledgement { get; set; }

		public bool IsEnabled { get; set; } = true;

		public bool IsNew => string.IsNullOrWhiteSpace(IntakeChannelId);

		/// <summary>The inbound dispatch email domain shown in the email help ("name@{domain}").</summary>
		public string EmailDomain { get; set; }

		public List<KeyValuePair<int, string>> Priorities { get; set; } = new List<KeyValuePair<int, string>>();

		public List<KeyValuePair<int, string>> CallTypes { get; set; } = new List<KeyValuePair<int, string>>();

		public List<KeyValuePair<int, string>> TemplateFormats { get; set; } = new List<KeyValuePair<int, string>>();

		public string ErrorMessage { get; set; }
	}

	/// <summary>A webhook channel's URL, signing help and (only right after it is created or rotated) its secret.</summary>
	public class IntakeWebhookView
	{
		public string IntakeChannelId { get; set; }

		public string Name { get; set; }

		public string Url { get; set; }

		/// <summary>The new secret in clear: set only on the response that created it, never stored or shown again.</summary>
		public string Secret { get; set; }

		public bool HasSecret { get; set; }

		public int ToleranceSeconds { get; set; }
	}
}
