using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Chat subtree of an authorized department deletion, in the caller's transaction, after the legal hold checks.
	/// Chat tables carry no foreign keys; message children go before messages and channels last so the order reads the model.</summary>
	public static class ChatDepartmentCleanup
	{
		internal static readonly string[] Tables =
		{
			"ChatMessageAcks", "ChatMessageEdits", "ChatMessageFlags", "ChatMessageMentions", "ChatMessageReactions", "ChatAttachments",
			"ChatModerationActions", "ChatExports", "ChatMessages", "ChatChannelAccessRules", "ChatChannelMembers", "ChatChannels", "ChatDepartmentSettings"
		};

		public static Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Chat");
			return DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, Tables, ct);
		}
	}
}
