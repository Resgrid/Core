using System;
using System.Diagnostics;
using System.Net.Mail;
using System.Threading.Tasks;
using EmailModule;

namespace Resgrid.Providers.EmailProvider
{
	public class SmtpClientWrapper : ISmtpClient
	{
		private SmtpClient realClient;
		private bool disposed;

		public SmtpClientWrapper(SmtpClient realClient)
		{
			Invariant.IsNotNull(realClient, "realClient");

			this.realClient = realClient;
		}

		[DebuggerStepThrough]
		~SmtpClientWrapper()
		{
			Dispose(false);
		}

		[DebuggerStepThrough]
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public async Task<bool> Send(MailMessage message)
		{
			Invariant.IsNotNull(message, "message");

			await realClient.SendMailAsync(message);

			return true;
		}

		[DebuggerStepThrough]
		protected virtual void DisposeCore()
		{
			if (realClient != null)
			{
				realClient.Dispose();
				realClient = null;
			}
		}

		[DebuggerStepThrough]
		private void Dispose(bool disposing)
		{
			if (!disposed && disposing)
			{
				DisposeCore();
			}

			disposed = true;
		}
	}
}
