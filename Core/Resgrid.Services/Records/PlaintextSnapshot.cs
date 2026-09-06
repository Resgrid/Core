using System;
using System.Collections.Generic;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// The plaintext of an entity's cataloged columns, taken before the ADP seam seals it in place and put back
	/// after the sealed row is stored. The caller keeps working with the same instance it passed in (tests and
	/// in-memory stores rely on that identity) while the database row carries the envelopes.
	/// </summary>
	public sealed class PlaintextSnapshot<T> where T : class
	{
		private readonly T _entity;
		private readonly List<(Action<T, string> Set, string Value)> _values = new List<(Action<T, string>, string)>();

		private PlaintextSnapshot(T entity)
		{
			_entity = entity;
		}

		public static PlaintextSnapshot<T> Take(T entity, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> accessors)
		{
			var snapshot = new PlaintextSnapshot<T>(entity);
			if (entity != null)
				foreach (var accessor in accessors)
					snapshot._values.Add((accessor.Value.Set, accessor.Value.Get(entity)));
			return snapshot;
		}

		/// <summary>Puts the plaintext back on the same instance. Marker columns are left as the seam set them.</summary>
		public void Restore()
		{
			if (_entity == null)
				return;
			foreach (var (set, value) in _values)
				set(_entity, value);
		}
	}
}
