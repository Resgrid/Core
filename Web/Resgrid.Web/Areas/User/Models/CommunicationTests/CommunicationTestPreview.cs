using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.CommunicationTests
{
	/// <summary>
	/// The message each channel will actually deliver, shown on the new/edit screens so the person
	/// sending a test can tell their people what to expect before it goes out. Built from
	/// <see cref="Resgrid.Model.CommunicationTestMessageCatalog"/> — the same source the sending code
	/// uses — so the preview cannot drift from the real message.
	/// The literal <see cref="NamePlaceholder"/> appears wherever the test name lands; the screen
	/// swaps it for whatever the administrator is typing.
	/// </summary>
	public class CommunicationTestPreview
	{
		/// <summary>Token substituted for the test name as the administrator types.</summary>
		public const string NamePlaceholder = "{name}";

		public string SmsBody { get; set; }
		public string EmailSubject { get; set; }
		public string EmailBody { get; set; }
		public List<string> VoicePrompts { get; set; } = new List<string>();
		public string PushTitle { get; set; }
		public string PushBody { get; set; }
		public string SampleRunCode { get; set; }
	}
}
