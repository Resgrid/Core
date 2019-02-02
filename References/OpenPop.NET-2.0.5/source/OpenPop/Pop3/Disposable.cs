using System;

namespace OpenPop.Pop3
{
	/// <summary>
	/// Utility class that simplifies the usage of <see cref="IDisposable"/>
	/// </summary>
	public abstract class Disposable : IDisposable
	{
		/// <summary>
		/// Returns <see langword="true"/> if this instance has been disposed of, <see langword="false"/> otherwise
		/// </summary>
		protected bool IsDisposed { get; private set; }

		/// <summary>
		/// Releases unmanaged resources and performs other cleanup operations before the
		/// <see cref="Disposable"/> is reclaimed by garbage collection.
		/// </summary>
		~Disposable()
		{
			Dispose(false);
		}

		/// <summary>
		/// Releases unmanaged and - optionally - managed resources
		/// </summary>
		public void Dispose()
		{
			if (!IsDisposed)
			{
				try
				{
					Dispose(true);
				} finally
				{
					IsDisposed = true;
					GC.SuppressFinalize(this);
				}
			}
		}

		/// <summary>
		/// Releases unmanaged and - optionally - managed resources. Remember to call this method from your derived classes.
		/// </summary>
		/// <param name="disposing">
		/// Set to <c>true</c> to release both managed and unmanaged resources.<br/>
		/// Set to <c>false</c> to release only unmanaged resources.
		/// </param>
		protected virtual void Dispose(bool disposing)
		{
		}

		/// <summary>
		/// Used to assert that the object has not been disposed
		/// </summary>
		/// <exception cref="ObjectDisposedException">Thrown if the object is in a disposed state.</exception>
		/// <remarks>
		/// The method is to be used by the subclasses in order to provide a simple method for checking the 
		/// disposal state of the object.
		/// </remarks>
		protected void AssertDisposed()
		{
			if (IsDisposed)
			{
				string typeName = GetType().FullName;
				throw new ObjectDisposedException(typeName, String.Format(System.Globalization.CultureInfo.InvariantCulture, "Cannot access a disposed {0}.", typeName));
			}
		}
	}
}