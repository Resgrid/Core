using FluentMigrator;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	/// <summary>
	/// Gives messages and messagerecipients a departmentid of their own (ADP plan section 5.1:
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
	///      rule the application uses everywhere else: departmentmembers, not deleted, isactive or
	///      isdefault (SelectDepartmentByUserIdQuery).
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
	/// TransactionBehavior.None for two reasons: CREATE INDEX CONCURRENTLY cannot run inside a
	/// transaction, and the batched updates commit as they go rather than holding locks across the
	/// whole backfill of two of the largest tables in the schema. Every step is guarded on IS NULL,
	/// so an interrupted run is simply re-run.
	/// </summary>
	[Migration(137, TransactionBehavior.None)]
	public class M0137_AddMessageDepartmentOwnershipPg : Migration
	{
		private const int BatchSize = 5000;

		public override void Up()
		{
			if (!Schema.Table("messages").Column("departmentid").Exists())
				Alter.Table("messages").AddColumn("departmentid").AsInt32().Nullable();

			if (!Schema.Table("messagerecipients").Column("departmentid").Exists())
				Alter.Table("messagerecipients").AddColumn("departmentid").AsInt32().Nullable();

			// Unlogged temp tables scoped to this session; dropped explicitly at the end.
			Execute.Sql(@"
DROP TABLE IF EXISTS pg_temp.adp_active_membership;
CREATE TEMP TABLE adp_active_membership AS
SELECT DISTINCT dm.userid, dm.departmentid
FROM departmentmembers dm
WHERE dm.isdeleted = false AND (dm.isactive = true OR dm.isdefault = true);

CREATE INDEX ix_adp_active_membership ON adp_active_membership (userid, departmentid);

DROP TABLE IF EXISTS pg_temp.adp_active_department;
CREATE TEMP TABLE adp_active_department AS
SELECT am.userid, MIN(am.departmentid) AS departmentid, COUNT(*) AS departmentcount
FROM adp_active_membership am
GROUP BY am.userid;

CREATE UNIQUE INDEX ix_adp_active_department ON adp_active_department (userid);");

			// Pass 1: the sender's active department, only where it is unambiguous.
			Execute.Sql($@"
DO $$
DECLARE affected integer;
BEGIN
	LOOP
		UPDATE messages m
		SET departmentid = a.departmentid
		FROM adp_active_department a
		WHERE a.userid = m.sendinguserid
		  AND m.messageid IN (
		      SELECT m2.messageid
		      FROM messages m2
		      INNER JOIN adp_active_department a2 ON a2.userid = m2.sendinguserid
		      WHERE m2.departmentid IS NULL AND a2.departmentcount = 1
		      LIMIT {BatchSize});

		GET DIAGNOSTICS affected = ROW_COUNT;
		EXIT WHEN affected = 0;
	END LOOP;
END $$;");

			// One vote per recipient membership, then the strict winner per message (tiedcount = 1).
			Execute.Sql(@"
DROP TABLE IF EXISTS pg_temp.adp_recipient_votes;
CREATE TEMP TABLE adp_recipient_votes AS
SELECT mr.messageid, am.departmentid, COUNT(*) AS votes
FROM messagerecipients mr
INNER JOIN adp_active_membership am ON am.userid = mr.userid
GROUP BY mr.messageid, am.departmentid;

CREATE INDEX ix_adp_recipient_votes ON adp_recipient_votes (messageid, departmentid);

DROP TABLE IF EXISTS pg_temp.adp_recipient_consensus;
CREATE TEMP TABLE adp_recipient_consensus AS
SELECT v.messageid, MIN(v.departmentid) AS departmentid, COUNT(*) AS tiedcount
FROM adp_recipient_votes v
WHERE v.votes = (SELECT MAX(v2.votes) FROM adp_recipient_votes v2 WHERE v2.messageid = v.messageid)
GROUP BY v.messageid;

CREATE UNIQUE INDEX ix_adp_recipient_consensus ON adp_recipient_consensus (messageid);");

			// Pass 2: recipient majority for what pass 1 could not answer. Restricted to the sender's
			// own memberships when they have any, so this disambiguates and never relocates.
			Execute.Sql($@"
DO $$
DECLARE affected integer;
BEGIN
	LOOP
		UPDATE messages m
		SET departmentid = c.departmentid
		FROM adp_recipient_consensus c
		WHERE c.messageid = m.messageid
		  AND m.messageid IN (
		      SELECT m2.messageid
		      FROM messages m2
		      INNER JOIN adp_recipient_consensus c2 ON c2.messageid = m2.messageid AND c2.tiedcount = 1
		      WHERE m2.departmentid IS NULL
		        AND (NOT EXISTS (SELECT 1 FROM adp_active_membership s WHERE s.userid = m2.sendinguserid)
		             OR EXISTS (SELECT 1 FROM adp_active_membership s
		                        WHERE s.userid = m2.sendinguserid AND s.departmentid = c2.departmentid))
		      LIMIT {BatchSize});

		GET DIAGNOSTICS affected = ROW_COUNT;
		EXIT WHEN affected = 0;
	END LOOP;
END $$;");

			// Pass 3: sanity check on pass 1. Only fires when NO recipient belongs to the department
			// the sender's flags produced and the recipients agree on another department the sender
			// is in too. Terminates because an updated row no longer differs from the consensus.
			Execute.Sql($@"
DO $$
DECLARE affected integer;
BEGIN
	LOOP
		UPDATE messages m
		SET departmentid = c.departmentid
		FROM adp_recipient_consensus c
		WHERE c.messageid = m.messageid
		  AND m.messageid IN (
		      SELECT m2.messageid
		      FROM messages m2
		      INNER JOIN adp_recipient_consensus c2 ON c2.messageid = m2.messageid AND c2.tiedcount = 1
		      INNER JOIN adp_active_membership s ON s.userid = m2.sendinguserid AND s.departmentid = c2.departmentid
		      WHERE m2.departmentid IS NOT NULL
		        AND m2.departmentid <> c2.departmentid
		        AND NOT EXISTS (SELECT 1 FROM adp_recipient_votes rv
		                        WHERE rv.messageid = m2.messageid AND rv.departmentid = m2.departmentid)
		      LIMIT {BatchSize});

		GET DIAGNOSTICS affected = ROW_COUNT;
		EXIT WHEN affected = 0;
	END LOOP;
END $$;");

			// Pass 4: recipients inherit the parent message's answer.
			Execute.Sql($@"
DO $$
DECLARE affected integer;
BEGIN
	LOOP
		UPDATE messagerecipients mr
		SET departmentid = m.departmentid
		FROM messages m
		WHERE m.messageid = mr.messageid
		  AND mr.messagerecipientid IN (
		      SELECT mr2.messagerecipientid
		      FROM messagerecipients mr2
		      INNER JOIN messages m2 ON m2.messageid = mr2.messageid
		      WHERE mr2.departmentid IS NULL AND m2.departmentid IS NOT NULL
		      LIMIT {BatchSize});

		GET DIAGNOSTICS affected = ROW_COUNT;
		EXIT WHEN affected = 0;
	END LOOP;
END $$;");

			Execute.Sql(@"
DROP TABLE IF EXISTS pg_temp.adp_recipient_consensus;
DROP TABLE IF EXISTS pg_temp.adp_recipient_votes;
DROP TABLE IF EXISTS pg_temp.adp_active_department;
DROP TABLE IF EXISTS pg_temp.adp_active_membership;");

			RemoveInvalidIndexes();

			// The migration engine batches by department and orders by primary key.
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messages_departmentid ON messages (departmentid, messageid);");
			Execute.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_messagerecipients_departmentid ON messagerecipients (departmentid, messagerecipientid);");
		}

		public override void Down()
		{
			// Safe to reverse: the column is additive and, until these tables enter the catalog,
			// nothing has been encrypted under the ownership it records. Refuse anyway if a
			// protected row exists — at that point the column is load-bearing for decryption.
			Execute.Sql(@"
DO $$
BEGIN
	IF EXISTS (SELECT 1 FROM messagerecipients WHERE isprotected = true) THEN
		RAISE EXCEPTION 'M0137 rollback refused: protected messagerecipients rows exist and their envelopes are bound to departmentid.';
	END IF;
END $$;");

			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_messagerecipients_departmentid;");
			Execute.Sql("DROP INDEX CONCURRENTLY IF EXISTS ix_messages_departmentid;");

			if (Schema.Table("messagerecipients").Column("departmentid").Exists())
				Delete.Column("departmentid").FromTable("messagerecipients");

			if (Schema.Table("messages").Column("departmentid").Exists())
				Delete.Column("departmentid").FromTable("messages");
		}

		private void RemoveInvalidIndexes()
		{
			Execute.Sql(@"
				DO $$
				DECLARE invalid_index record;
				BEGIN
					FOR invalid_index IN
						SELECT n.nspname AS schema_name, c.relname AS index_name
						FROM pg_class c
						JOIN pg_index i ON i.indexrelid = c.oid
						JOIN pg_namespace n ON n.oid = c.relnamespace
						WHERE n.nspname = current_schema()
						AND c.relname IN (
							'ix_messages_departmentid',
							'ix_messagerecipients_departmentid')
						AND NOT i.indisvalid
					LOOP
						EXECUTE format('DROP INDEX %I.%I', invalid_index.schema_name, invalid_index.index_name);
					END LOOP;
				END $$;");
		}
	}
}
