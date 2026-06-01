using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Tests for the chatbot localization layer (<see cref="ChatbotResources"/>): culture resolution,
	/// English fallback, argument formatting, and that handlers honor the session's culture end-to-end.
	/// </summary>
	[TestFixture]
	public class ChatbotLocalizationTests
	{
		[Test]
		public void Resources_ReturnsTranslated_ForSupportedCultures()
		{
			ChatbotResources.Get("Msg_NoUnread", "es").Should().Be("No tienes mensajes sin leer.");
			ChatbotResources.Get("Msg_NoUnread", "fr").Should().Be("Vous n'avez aucun message non lu.");
			ChatbotResources.Get("Msg_NoUnread", "de").Should().Be("Sie haben keine ungelesenen Nachrichten.");
			ChatbotResources.Get("Msg_NoUnread", "uk").Should().Be("У вас немає непрочитаних повідомлень.");
		}

		[Test]
		public void Resources_FallsBackToEnglish_ForUnknownOrNullCulture()
		{
			ChatbotResources.Get("Msg_NoUnread", "xx").Should().Be("You have no unread messages.");
			ChatbotResources.Get("Msg_NoUnread", null).Should().Be("You have no unread messages.");
			ChatbotResources.Get("Msg_NoUnread", "").Should().Be("You have no unread messages.");
		}

		[Test]
		public void Resources_FormatsArguments()
		{
			ChatbotResources.Get("Msg_NotFound", "es", 42).Should().Be("Mensaje #42 no encontrado.");
			ChatbotResources.Get("Msg_NotFound", "en", 7).Should().Be("Message #7 not found.");
		}

		[Test]
		public void Resources_UnknownKey_ReturnsKey()
		{
			ChatbotResources.Get("This_Key_Does_Not_Exist", "en").Should().Be("This_Key_Does_Not_Exist");
		}

		[Test]
		public void NormalizeCulture_HandlesVariants()
		{
			ChatbotResources.NormalizeCulture("en-US").Should().Be("en");
			ChatbotResources.NormalizeCulture("ES").Should().Be("es");
			ChatbotResources.NormalizeCulture("pt-BR").Should().Be("en"); // unsupported -> English
			ChatbotResources.NormalizeCulture(null).Should().Be("en");
		}

		[Test]
		public async Task Handler_RendersInSessionCulture()
		{
			var messages = new Mock<IMessageService>();
			messages.Setup(m => m.GetMessageRecipientByMessageAndUserAsync(42, "user-1")).ReturnsAsync((MessageRecipient)null);

			var handler = new MessageDeleteHandler(messages.Object);
			var session = new ChatbotSession { SessionId = "s1", UserId = "user-1", DepartmentId = 1, Platform = ChatbotPlatform.SmsTwilio, Culture = "es" };
			var intent = new ChatbotIntent { Type = ChatbotIntentType.DeleteMessage };
			intent.Parameters["messageId"] = "42";

			var response = await handler.HandleAsync(new ChatbotMessage { Text = "delete #42" }, intent, session);

			// Spanish "Message #42 not found." — proves the session culture flows through to the output.
			response.Text.Should().Be("Mensaje #42 no encontrado.");
		}
	}
}
