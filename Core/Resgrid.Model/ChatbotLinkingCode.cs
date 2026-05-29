using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Persistence entity for the ChatbotLinkingCodes table. A short-lived, single-use code a user
	/// generates in the web portal and enters in their chat app to link a platform identity.
	/// </summary>
	[Table("ChatbotLinkingCodes")]
	public class ChatbotLinkingCode : IEntity
	{
		public string Id { get; set; }

		[Required]
		public string UserId { get; set; }

		[Required]
		public string Code { get; set; }

		public int? Platform { get; set; }

		public string PlatformUserId { get; set; }

		public bool IsUsed { get; set; }

		public DateTime CreatedAt { get; set; }

		public DateTime ExpiresAt { get; set; }

		public DateTime? UsedAt { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => Id;
			set => Id = (string)value;
		}

		[NotMapped] public string TableName => "ChatbotLinkingCodes";
		[NotMapped] public string IdName => "Id";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
