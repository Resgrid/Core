using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// Gives Messages and MessageRecipients a DepartmentId of their own (ADP plan section 5.1:
	/// "add DepartmentId to child tables that lack it, deriving ownership from a verified parent
	/// before migration").
	///
	/// Neither table could be bound to the protected-field catalog without this. The envelope AAD
	/// is rgdp|{departmentId}|{fieldId}|{rowKey}|{version}, so a row that cannot be attributed to
	/// exactly ONE department can neither be encrypted (which key?) nor decrypted afterwards (the
	/// AAD could not be rebuilt). Deriving the department through a join at read time is not a
	/// substitute: the only route is the sender's or recipient's membership, and membership MOVES —
	/// a user changing departments would silently change the AAD and orphan every envelope already
	/// written. The value has to be resolved once and frozen on the row. Both tables get the column
	/// because a child that derives its department through a join has no value of its own to bind.
	///
	/// Backfill, in order, and never overwriting a value that is already there:
	///   1. The sender's active department, where the sender has exactly one. "Active" is the same
	///      rule the application uses everywhere else: DepartmentMembers, not deleted, IsActive or
	///      IsDefault (SelectDepartmentByUserIdQuery).
	///   2. Where that is silent (a sender with several active memberships, a deleted account, a
	///      system-generated message), the recipients' majority. A tie resolves nothing and is left
	///      alone. When the sender does have memberships, the consensus is restricted to those, so
	///      recipients can only DISAMBIGUATE the sender, never relocate the message.
	///   3. A narrow sanity check on step 1: if not one recipient belongs to the department the
	///      sender's flags pointed at, and the recipients agree on a different department the
	///      sender ALSO belongs to, the recipients win. That is the shape of a sender whose active
	///      flag has since moved to their newer department.
	///   4. Recipients inherit their parent message's answer.
	///
	/// Rows that survive all four passes stay NULL. That is deliberate and safe: unresolved
	/// ownership means the row is excluded from encryption rather than encrypted under a guess.
	///
	/// Nullable for the same reason, and TransactionBehavior.None so the batched updates commit as
	/// they go — Messages and MessageRecipients are among the largest tables in the schema, and one
	/// transaction spanning the whole backfill would hold locks for its entire duration. Every step
	/// is guarded on IS NULL, so an interrupted run is simply re-run.
	/// </summary>
	[Migration(137, TransactionBehavior.None)]
	public class M0137_AddMessageDepartmentOwnership : Migration
	{
		private const int BatchSize = 5000;

		public override void Up()
		{
			if (!Schema.Table("Messages").Column("DepartmentId").Exists())
				Alter.Table("Messages").AddColumn("DepartmentId").AsInt32().Nullable();

			if (!Schema.Table("MessageRecipients").Column("DepartmentId").Exists())
				Alter.Table("MessageRecipients").AddColumn("DepartmentId").AsInt32().Nullable();

			Execute.Sql($@"
-- One row per user with their active membership(s). DepartmentCount = 1 is the unambiguous case.
IF OBJECT_ID('tempdb..#AdpActiveMembership') IS NOT NULL DROP TABLE #AdpActiveMembership;
SELECT DISTINCT dm.[UserId], dm.[DepartmentId]
INTO #AdpActiveMembership
FROM [DepartmentMembers] dm
WHERE dm.[IsDeleted] = 0 AND (dm.[IsActive] = 1 OR dm.[IsDefault] = 1);

CREATE CLUSTERED INDEX [IX_AdpActiveMembership] ON #AdpActiveMembership ([UserId], [DepartmentId]);

IF OBJECT_ID('tempdb..#AdpActiveDepartment') IS NOT NULL DROP TABLE #AdpActiveDepartment;
SELECT am.[UserId], MIN(am.[DepartmentId]) AS [DepartmentId], COUNT(*) AS [DepartmentCount]
INTO #AdpActiveDepartment
FROM #AdpActiveMembership am
GROUP BY am.[UserId];

CREATE UNIQUE CLUSTERED INDEX [IX_AdpActiveDepartment] ON #AdpActiveDepartment ([UserId]);

-- Pass 1: the sender's active department, only where it is unambiguous.
WHILE 1 = 1
BEGIN
	UPDATE TOP ({BatchSize}) m
	SET m.[DepartmentId] = a.[DepartmentId]
	FROM [Messages] m
	INNER JOIN #AdpActiveDepartment a ON a.[UserId] = m.[SendingUserId]
	WHERE m.[DepartmentId] IS NULL AND a.[DepartmentCount] = 1;

	IF @@ROWCOUNT = 0 BREAK;
END

-- One vote per recipient membership, then the strict winner per message (TiedCount = 1).
IF OBJECT_ID('tempdb..#AdpRecipientVotes') IS NOT NULL DROP TABLE #AdpRecipientVotes;
SELECT mr.[MessageId], am.[DepartmentId], COUNT_BIG(*) AS [Votes]
INTO #AdpRecipientVotes
FROM [MessageRecipients] mr
INNER JOIN #AdpActiveMembership am ON am.[UserId] = mr.[UserId]
GROUP BY mr.[MessageId], am.[DepartmentId];

CREATE CLUSTERED INDEX [IX_AdpRecipientVotes] ON #AdpRecipientVotes ([MessageId], [DepartmentId]);

IF OBJECT_ID('tempdb..#AdpRecipientConsensus') IS NOT NULL DROP TABLE #AdpRecipientConsensus;
SELECT v.[MessageId], MIN(v.[DepartmentId]) AS [DepartmentId], COUNT(*) AS [TiedCount]
INTO #AdpRecipientConsensus
FROM #AdpRecipientVotes v
WHERE v.[Votes] = (SELECT MAX(v2.[Votes]) FROM #AdpRecipientVotes v2 WHERE v2.[MessageId] = v.[MessageId])
GROUP BY v.[MessageId];

CREATE UNIQUE CLUSTERED INDEX [IX_AdpRecipientConsensus] ON #AdpRecipientConsensus ([MessageId]);

-- Pass 2: recipient majority for what pass 1 could not answer. Restricted to the sender's own
-- memberships when they have any, so this disambiguates the sender and never relocates a message.
WHILE 1 = 1
BEGIN
	UPDATE TOP ({BatchSize}) m
	SET m.[DepartmentId] = c.[DepartmentId]
	FROM [Messages] m
	INNER JOIN #AdpRecipientConsensus c ON c.[MessageId] = m.[MessageId] AND c.[TiedCount] = 1
	WHERE m.[DepartmentId] IS NULL
	  AND (NOT EXISTS (SELECT 1 FROM #AdpActiveMembership s WHERE s.[UserId] = m.[SendingUserId])
	       OR EXISTS (SELECT 1 FROM #AdpActiveMembership s
	                  WHERE s.[UserId] = m.[SendingUserId] AND s.[DepartmentId] = c.[DepartmentId]));

	IF @@ROWCOUNT = 0 BREAK;
END

-- Pass 3: sanity check on pass 1. Only fires when NO recipient belongs to the department the
-- sender's flags produced and the recipients agree on another department the sender is in too.
-- The loop terminates because each updated row stops matching m.[DepartmentId] <> c.[DepartmentId].
WHILE 1 = 1
BEGIN
	UPDATE TOP ({BatchSize}) m
	SET m.[DepartmentId] = c.[DepartmentId]
	FROM [Messages] m
	INNER JOIN #AdpRecipientConsensus c ON c.[MessageId] = m.[MessageId] AND c.[TiedCount] = 1
	INNER JOIN #AdpActiveMembership s ON s.[UserId] = m.[SendingUserId] AND s.[DepartmentId] = c.[DepartmentId]
	WHERE m.[DepartmentId] IS NOT NULL
	  AND m.[DepartmentId] <> c.[DepartmentId]
	  AND NOT EXISTS (SELECT 1 FROM #AdpRecipientVotes rv
	                  WHERE rv.[MessageId] = m.[MessageId] AND rv.[DepartmentId] = m.[DepartmentId]);

	IF @@ROWCOUNT = 0 BREAK;
END

-- Pass 4: recipients inherit the parent message's answer.
WHILE 1 = 1
BEGIN
	UPDATE TOP ({BatchSize}) mr
	SET mr.[DepartmentId] = m.[DepartmentId]
	FROM [MessageRecipients] mr
	INNER JOIN [Messages] m ON m.[MessageId] = mr.[MessageId]
	WHERE mr.[DepartmentId] IS NULL AND m.[DepartmentId] IS NOT NULL;

	IF @@ROWCOUNT = 0 BREAK;
END

DROP TABLE #AdpRecipientConsensus;
DROP TABLE #AdpRecipientVotes;
DROP TABLE #AdpActiveDepartment;
DROP TABLE #AdpActiveMembership;");

			// The migration engine batches by department and orders by primary key.
			if (!Schema.Table("Messages").Index("IX_Messages_DepartmentId").Exists())
				Create.Index("IX_Messages_DepartmentId")
					.OnTable("Messages")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("MessageId").Ascending();

			if (!Schema.Table("MessageRecipients").Index("IX_MessageRecipients_DepartmentId").Exists())
				Create.Index("IX_MessageRecipients_DepartmentId")
					.OnTable("MessageRecipients")
					.OnColumn("DepartmentId").Ascending()
					.OnColumn("MessageRecipientId").Ascending();
		}

		public override void Down()
		{
			// Safe to reverse: the column is additive and, until these tables enter the catalog,
			// nothing has been encrypted under the ownership it records. Refuse anyway if a
			// protected row exists — at that point the column is load-bearing for decryption.
			Execute.Sql(@"
IF EXISTS (SELECT 1 FROM [MessageRecipients] WHERE [IsProtected] = 1)
	THROW 51000, 'M0137 rollback refused: protected MessageRecipients rows exist and their envelopes are bound to DepartmentId.', 1;");

			if (Schema.Table("MessageRecipients").Index("IX_MessageRecipients_DepartmentId").Exists())
				Delete.Index("IX_MessageRecipients_DepartmentId").OnTable("MessageRecipients");

			if (Schema.Table("Messages").Index("IX_Messages_DepartmentId").Exists())
				Delete.Index("IX_Messages_DepartmentId").OnTable("Messages");

			if (Schema.Table("MessageRecipients").Column("DepartmentId").Exists())
				Delete.Column("DepartmentId").FromTable("MessageRecipients");

			if (Schema.Table("Messages").Column("DepartmentId").Exists())
				Delete.Column("DepartmentId").FromTable("Messages");
		}
	}
}
