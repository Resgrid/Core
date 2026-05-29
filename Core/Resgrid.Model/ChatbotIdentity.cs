using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Persistence entity for the ChatbotUserIdentities table. Maps a chat-platform identity
	/// (platform + platform user id) to a Resgrid user. The chatbot project maps this to/from its
	/// own <c>Resgrid.Chatbot.Models.ChatbotUserIdentity</c> (which carries the platform as an enum).
	/// </summary>
	[Table("ChatbotUserIdentities")]
	public class ChatbotIdentity : IEntity
	{
		public string Id { get; set; }

		[Required]
		public string UserId { get; set; }

		[Required]
		public int Platform { get; set; }

		[Required]
		public string PlatformUserId { get; set; }

		public string PlatformUserName { get; set; }

		public bool IsActive { get; set; }

		public DateTime CreatedAt { get; set; }

		public DateTime? LastUsedAt { get; set; }

		public string LinkingMethod { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => Id;
			set => Id = (string)value;
		}

		[NotMapped] public string TableName => "ChatbotUserIdentities";
		[NotMapped] public string IdName => "Id";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
