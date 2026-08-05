using FluentMigrator;
using System.Data;

namespace Resgrid.Providers.MigrationsPg.Migrations
{
	[Migration(112)]
	public class M0112_AddModerationPg : Migration
	{
		public override void Up()
		{
			if (Schema.Table("chatmessages").Exists() && !Schema.Table("chatmessages").Column("ismoderated").Exists())
			{
				Alter.Table("chatmessages")
					.AddColumn("ismoderated").AsBoolean().NotNullable().WithDefaultValue(false);
			}

			if (!Schema.Table("moderationrequests").Exists())
			{
				Create.Table("moderationrequests")
					.WithColumn("moderationrequestid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("itemtype").AsInt32().NotNullable()
					.WithColumn("itemid").AsCustom("citext").NotNullable()
					.WithColumn("callid").AsInt32().Nullable()
					.WithColumn("chatchannelid").AsCustom("citext").Nullable()
					.WithColumn("contentauthoruserid").AsCustom("citext").Nullable()
					.WithColumn("contentauthorunitid").AsInt32().Nullable()
					.WithColumn("contentcreatedon").AsDateTime2().Nullable()
					.WithColumn("originalsubject").AsCustom("text").Nullable()
					.WithColumn("originaltext").AsCustom("text").Nullable()
					.WithColumn("originalfilename").AsCustom("text").Nullable()
					.WithColumn("originalcontenttype").AsCustom("citext").Nullable()
					.WithColumn("originalcontent").AsCustom("bytea").Nullable()
					.WithColumn("originalmetadatajson").AsCustom("text").Nullable()
					.WithColumn("status").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("disposition").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("createdon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("modifiedon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("completedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("completedon").AsDateTime2().Nullable()
					.WithColumn("adminnote").AsCustom("text").Nullable();

				Create.Index("ux_moderationrequests_department_item")
					.OnTable("moderationrequests")
					.OnColumn("departmentid").Ascending()
					.OnColumn("itemtype").Ascending()
					.OnColumn("itemid").Ascending()
					.WithOptions().Unique();

				Create.Index("ix_moderationrequests_department_status_modifiedon")
					.OnTable("moderationrequests")
					.OnColumn("departmentid").Ascending()
					.OnColumn("status").Ascending()
					.OnColumn("modifiedon").Descending();

				Create.Index("ix_moderationrequests_department_author")
					.OnTable("moderationrequests")
					.OnColumn("departmentid").Ascending()
					.OnColumn("contentauthoruserid").Ascending();
			}

			if (!Schema.Table("moderationreports").Exists())
			{
				Create.Table("moderationreports")
					.WithColumn("moderationreportid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("moderationrequestid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("reportedbyuserid").AsCustom("citext").NotNullable()
					.WithColumn("reportergroupid").AsInt32().Nullable()
					.WithColumn("reason").AsInt32().NotNullable().WithDefaultValue(0)
					.WithColumn("note").AsCustom("text").Nullable()
					.WithColumn("reportedon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

				Create.Index("ux_moderationreports_request_reporter")
					.OnTable("moderationreports")
					.OnColumn("moderationrequestid").Ascending()
					.OnColumn("reportedbyuserid").Ascending()
					.WithOptions().Unique();

				Create.Index("ix_moderationreports_department_group")
					.OnTable("moderationreports")
					.OnColumn("departmentid").Ascending()
					.OnColumn("reportergroupid").Ascending();

				Create.Index("ix_moderationreports_department_reporter")
					.OnTable("moderationreports")
					.OnColumn("departmentid").Ascending()
					.OnColumn("reportedbyuserid").Ascending();
			}

			if (!Schema.Table("moderationactions").Exists())
			{
				Create.Table("moderationactions")
					.WithColumn("moderationactionid").AsCustom("citext").NotNullable().PrimaryKey()
					.WithColumn("moderationrequestid").AsCustom("citext").NotNullable()
					.WithColumn("departmentid").AsInt32().NotNullable()
					.WithColumn("actiontype").AsInt32().NotNullable()
					.WithColumn("performedbyuserid").AsCustom("citext").Nullable()
					.WithColumn("performedon").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
					.WithColumn("note").AsCustom("text").Nullable()
					.WithColumn("previousstatus").AsInt32().Nullable()
					.WithColumn("newstatus").AsInt32().Nullable()
					.WithColumn("actorrole").AsCustom("citext").Nullable()
					.WithColumn("ipaddress").AsCustom("citext").Nullable()
					.WithColumn("useragent").AsCustom("text").Nullable()
					.WithColumn("traceid").AsCustom("citext").Nullable()
					.WithColumn("servername").AsCustom("citext").Nullable()
					.WithColumn("detailsjson").AsCustom("text").Nullable()
					.WithColumn("evidencetext").AsCustom("text").Nullable()
					.WithColumn("evidencecontent").AsCustom("bytea").Nullable()
					.WithColumn("evidencemetadatajson").AsCustom("text").Nullable();

				Create.Index("ix_moderationactions_request_performedon")
					.OnTable("moderationactions")
					.OnColumn("moderationrequestid").Ascending()
					.OnColumn("performedon").Ascending();
			}

			if (!Schema.Table("moderationreports").Constraint("fk_moderationreports_moderationrequests").Exists())
			{
				Create.ForeignKey("fk_moderationreports_moderationrequests")
					.FromTable("moderationreports").ForeignColumn("moderationrequestid")
					.ToTable("moderationrequests").PrimaryColumn("moderationrequestid");
			}

			if (!Schema.Table("moderationactions").Constraint("fk_moderationactions_moderationrequests").Exists())
			{
				Create.ForeignKey("fk_moderationactions_moderationrequests")
					.FromTable("moderationactions").ForeignColumn("moderationrequestid")
					.ToTable("moderationrequests").PrimaryColumn("moderationrequestid");
			}

			Execute.WithConnection((connection, transaction) =>
			{
				using var command = connection.CreateCommand();
				command.Transaction = transaction;
				command.CommandText = "SELECT 1 FROM moderationrequests LIMIT 1;";

				if (command.ExecuteScalar() == null)
					ImportLegacyFlags(connection, transaction);
			});
		}

		private static void ImportLegacyFlags(IDbConnection connection, IDbTransaction transaction)
		{
			using var command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = @"
INSERT INTO moderationrequests
    (moderationrequestid, departmentid, itemtype, itemid, chatchannelid, contentauthoruserid,
     contentauthorunitid, contentcreatedon, originaltext, originalfilename, originalcontenttype,
     originalcontent, originalmetadatajson, status, disposition, createdon, modifiedon,
     completedbyuserid, completedon, adminnote)
SELECT flagged.moderationrequestid, flagged.departmentid, 0, flagged.chatmessageid,
	   flagged.chatchannelid, flagged.contentauthoruserid, flagged.contentauthorunitid,
	   flagged.contentcreatedon, flagged.originaltext, attachment.filename, attachment.contenttype,
	   attachment.data, flagged.originalmetadatajson, flagged.status, flagged.disposition,
	   flagged.createdon, flagged.modifiedon, flagged.completedbyuserid, flagged.completedon,
	   flagged.adminnote
FROM (
	SELECT MIN(f.chatmessageflagid::text) AS moderationrequestid, f.departmentid, f.chatmessageid,
		   MIN(f.chatchannelid::text) AS chatchannelid, MIN(m.senderuserid::text) AS contentauthoruserid,
		   MIN(m.senderunitid) AS contentauthorunitid, MIN(m.senton) AS contentcreatedon,
		   COALESCE(MIN(m.body), (SELECT e.priorbody FROM chatmessageedits e
								  WHERE e.chatmessageid = f.chatmessageid ORDER BY e.editedon DESC LIMIT 1)) AS originaltext,
		   MIN(m.metadatajson) AS originalmetadatajson,
		   CASE WHEN SUM(CASE WHEN f.status = 0 THEN 1 ELSE 0 END) > 0 THEN 0 ELSE 1 END AS status,
		   CASE WHEN SUM(CASE WHEN f.status = 0 THEN 1 ELSE 0 END) > 0 THEN 0
				WHEN SUM(CASE WHEN f.status = 3 THEN 1 ELSE 0 END) > 0 THEN 2 ELSE 1 END AS disposition,
		   MIN(f.flaggedon) AS createdon, MAX(COALESCE(f.reviewedon, f.flaggedon)) AS modifiedon,
		   MAX(f.reviewedbyuserid::text) AS completedbyuserid, MAX(f.reviewedon) AS completedon,
		   MAX(f.resolutionnote) AS adminnote
	FROM chatmessageflags f
	LEFT JOIN chatmessages m ON m.chatmessageid = f.chatmessageid
	GROUP BY f.departmentid, f.chatmessageid
) flagged
LEFT JOIN LATERAL (
	SELECT ca.filename, ca.contenttype, ca.data
	FROM chatattachments ca
	WHERE ca.chatmessageid = flagged.chatmessageid
	ORDER BY ca.uploadedon
	LIMIT 1
) attachment ON true;

INSERT INTO moderationreports
    (moderationreportid, moderationrequestid, departmentid, reportedbyuserid, reportergroupid,
     reason, note, reportedon)
SELECT MIN(f.chatmessageflagid::text),
       (SELECT MIN(f2.chatmessageflagid::text) FROM chatmessageflags f2
        WHERE f2.departmentid = f.departmentid AND f2.chatmessageid = f.chatmessageid),
       f.departmentid, f.flaggedbyuserid,
       (SELECT gm.departmentgroupid FROM departmentgroupmembers gm
        WHERE gm.departmentid = f.departmentid AND gm.userid = f.flaggedbyuserid
        ORDER BY gm.departmentgroupmemberid LIMIT 1),
       MIN(f.reason), MIN(f.note), MIN(f.flaggedon)
FROM chatmessageflags f
WHERE f.flaggedbyuserid IS NOT NULL
GROUP BY f.departmentid, f.chatmessageid, f.flaggedbyuserid;

INSERT INTO moderationrequests
    (moderationrequestid, departmentid, itemtype, itemid, callid, contentauthoruserid,
     contentcreatedon, originaltext, originalmetadatajson, status, disposition, createdon, modifiedon)
SELECT CONCAT('callnote-', n.callnoteid), c.departmentid, 2, n.callnoteid::text,
       n.callid, n.userid, n.timestamp, n.note,
       json_build_object('source', n.source, 'latitude', n.latitude, 'longitude', n.longitude)::text,
       0, 0, COALESCE(n.flaggedon, n.timestamp), COALESCE(n.flaggedon, n.timestamp)
FROM callnotes n
INNER JOIN calls c ON c.callid = n.callid
WHERE n.isflagged = true;

INSERT INTO moderationreports
    (moderationreportid, moderationrequestid, departmentid, reportedbyuserid, reportergroupid,
     reason, note, reportedon)
SELECT CONCAT('callnote-', n.callnoteid), CONCAT('callnote-', n.callnoteid), c.departmentid,
       n.flaggedbyuserid,
       (SELECT gm.departmentgroupid FROM departmentgroupmembers gm
        WHERE gm.departmentid = c.departmentid AND gm.userid = n.flaggedbyuserid
        ORDER BY gm.departmentgroupmemberid LIMIT 1),
       0, n.flaggedreason, COALESCE(n.flaggedon, n.timestamp)
FROM callnotes n
INNER JOIN calls c ON c.callid = n.callid
WHERE n.isflagged = true AND n.flaggedbyuserid IS NOT NULL;

INSERT INTO moderationrequests
    (moderationrequestid, departmentid, itemtype, itemid, callid, contentauthoruserid,
     contentcreatedon, originalfilename, originalcontenttype, originalcontent, originalmetadatajson,
     status, disposition, createdon, modifiedon)
SELECT CONCAT('callimage-', a.callattachmentid), c.departmentid, 3, a.callattachmentid::text,
       a.callid, a.userid, a.timestamp, a.filename, 'image/jpeg', a.data,
       json_build_object('name', a.name, 'size', a.size)::text,
       0, 0, COALESCE(a.flaggedon, a.timestamp, NOW() AT TIME ZONE 'utc'),
       COALESCE(a.flaggedon, a.timestamp, NOW() AT TIME ZONE 'utc')
FROM callattachments a
INNER JOIN calls c ON c.callid = a.callid
WHERE a.isflagged = true AND a.callattachmenttype = 2;

INSERT INTO moderationreports
    (moderationreportid, moderationrequestid, departmentid, reportedbyuserid, reportergroupid,
     reason, note, reportedon)
SELECT CONCAT('callimage-', a.callattachmentid), CONCAT('callimage-', a.callattachmentid),
       c.departmentid, a.flaggedbyuserid,
       (SELECT gm.departmentgroupid FROM departmentgroupmembers gm
        WHERE gm.departmentid = c.departmentid AND gm.userid = a.flaggedbyuserid
        ORDER BY gm.departmentgroupmemberid LIMIT 1),
       0, a.flaggedreason, COALESCE(a.flaggedon, a.timestamp, NOW() AT TIME ZONE 'utc')
FROM callattachments a
INNER JOIN calls c ON c.callid = a.callid
WHERE a.isflagged = true AND a.callattachmenttype = 2 AND a.flaggedbyuserid IS NOT NULL;

INSERT INTO moderationactions
    (moderationactionid, moderationrequestid, departmentid, actiontype, performedbyuserid,
     performedon, newstatus, actorrole, servername, detailsjson, evidencetext, evidencecontent,
     evidencemetadatajson)
SELECT r.moderationrequestid, r.moderationrequestid, r.departmentid, 0,
       (SELECT rp.reportedbyuserid FROM moderationreports rp
        WHERE rp.moderationrequestid = r.moderationrequestid ORDER BY rp.reportedon LIMIT 1),
       r.createdon, r.status, 'LegacyImport', inet_server_addr()::text, '{""imported"":true}',
       r.originaltext, r.originalcontent, r.originalmetadatajson
FROM moderationrequests r;";
			command.ExecuteNonQuery();
		}

		public override void Down()
		{
			if (Schema.Table("moderationactions").Exists())
				Delete.Table("moderationactions");

			if (Schema.Table("moderationreports").Exists())
				Delete.Table("moderationreports");

			if (Schema.Table("moderationrequests").Exists())
				Delete.Table("moderationrequests");

			if (Schema.Table("chatmessages").Exists() && Schema.Table("chatmessages").Column("ismoderated").Exists())
				Delete.Column("ismoderated").FromTable("chatmessages");
		}
	}
}
