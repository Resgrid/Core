using System;
using System.Threading;

namespace Resgrid.Framework
{
	/// <summary>
	/// The department the current request (or background job) works for, flowing with async calls. Set by the web hosts'
	/// middleware from the signed-in user's department claim; read by code that has no department parameter but must use a
	/// department's own provider account (geocoding, setting 125). 0 = none: the install's providers.
	/// </summary>
	public static class RequestDepartment
	{
		private static readonly AsyncLocal<int> Current = new AsyncLocal<int>();

		public static int Id => Current.Value;

		/// <summary>Sets the department until the returned scope is disposed, then restores the previous one.</summary>
		public static IDisposable Enter(int departmentId)
		{
			var previous = Current.Value;
			Current.Value = departmentId < 0 ? 0 : departmentId;
			return new Scope(previous);
		}

		private sealed class Scope : IDisposable
		{
			private readonly int _previous;
			private bool _disposed;

			public Scope(int previous)
			{
				_previous = previous;
			}

			public void Dispose()
			{
				if (_disposed)
					return;

				_disposed = true;
				Current.Value = _previous;
			}
		}
	}
}
