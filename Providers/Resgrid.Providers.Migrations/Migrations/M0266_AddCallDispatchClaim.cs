using FluentMigrator;

namespace Resgrid.Providers.Migrations.Migrations
{
	/// <summary>
	/// The lease on a waiting call's dispatch. Dispatch Now and the scheduled-calls worker claim a pending or scheduled call
	/// by marking it dispatched before they send it, so two senders never page the same call twice. DispatchClaimedOn records
	/// when that claim was taken and is cleared once the broadcast is queued (or the claim is given back). A claim still set
	/// after its lease belongs to a process that died mid-dispatch, and the call can be claimed again instead of staying marked
	/// sent forever. No index: it is only ever read on the row being claimed and by the worker's dispatch-time window.
	/// </summary>
	[Migration(266)]
	public class M0266_AddCallDispatchClaim : Migration
	{
		public override void Up()
		{
			Execute.Sql("IF COL_LENGTH('Calls', 'DispatchClaimedOn') IS NULL ALTER TABLE [Calls] ADD [DispatchClaimedOn] datetime2 NULL;");
		}

		public override void Down()
		{
			Execute.Sql("IF COL_LENGTH('Calls', 'DispatchClaimedOn') IS NOT NULL ALTER TABLE [Calls] DROP COLUMN [DispatchClaimedOn];");
		}
	}
}
