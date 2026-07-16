namespace Resgrid.Chatbot.Models
{
	// The SMS values (SmsTwilio, SmsSignalWire) are mirrored as constants on
	// Resgrid.Model.ChatbotIdentity for consumers that cannot reference this project.
	public enum ChatbotPlatform
	{
		Unknown = 0,
		SmsTwilio = 1,
		SmsSignalWire = 2,
		Discord = 3,
		Slack = 4,
		Telegram = 5,
		WhatsApp = 6,
		MicrosoftTeams = 7,
		Signal = 8,
		WebChat = 9
	}
}
