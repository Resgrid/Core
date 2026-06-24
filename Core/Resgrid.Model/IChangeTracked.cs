using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Entities that carry a <see cref="ModifiedOn"/> change cursor. It is stamped on every insert and update
	/// and drives two offline-first concerns: the delta-sync "changed since" query (pull) and last-write-wins
	/// conflict resolution on reconnect. See <c>docs/architecture/offline-first-architecture.md</c>.
	/// </summary>
	public interface IChangeTracked
	{
		DateTime? ModifiedOn { get; set; }
	}
}
