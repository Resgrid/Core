using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Scanning
{
	/// <summary>
	/// ClamAV attachment scanner (RMS plan section 4.7 "malware scanning") speaking clamd's INSTREAM protocol
	/// over TCP: a null-terminated command, length-prefixed chunks, a zero-length chunk, one null-terminated
	/// reply. Bytes arrive here after media hygiene, so images are already re-encoded and active content
	/// refused; this is the signature pass on what remains (PDFs, documents, archives).
	///
	/// Outcomes always land on the attachment row: a signature hit is Rejected, a clean reply is Clean, and an
	/// unreachable or erroring engine is Rejected under <see cref="AttachmentScanningConfig.FailClosed"/> (the
	/// default) or Pending otherwise. With scanning disabled the result is Skipped and no socket is opened.
	/// </summary>
	public class ClamAvAttachmentScanner : IRecordAttachmentScanner
	{
		public const string DisabledDetail = "Attachment scanning is disabled.";
		private const string InstreamCommand = "zINSTREAM\0";
		private const int MaxReplyBytes = 4096;

		public async Task<RecordAttachmentScanResult> ScanAsync(string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default)
		{
			if (!AttachmentScanningConfig.Enabled)
				return new RecordAttachmentScanResult { State = RmsAttachmentScanState.Skipped, Detail = DisabledDetail };

			string reply;
			try
			{
				reply = await StreamToClamdAsync(data ?? Array.Empty<byte>(), cancellationToken);
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Logging.LogError($"ClamAV scan of '{fileName}' timed out after {AttachmentScanningConfig.TimeoutSeconds} seconds ({AttachmentScanningConfig.Host}:{AttachmentScanningConfig.Port}).");
				return Unavailable($"clamd did not answer within {AttachmentScanningConfig.TimeoutSeconds} seconds");
			}
			catch (Exception ex) when (ex is SocketException || ex is IOException || ex is InvalidOperationException)
			{
				Logging.LogException(ex, $"ClamAV scan of '{fileName}' could not reach {AttachmentScanningConfig.Host}:{AttachmentScanningConfig.Port}.");
				return Unavailable($"clamd unreachable: {ex.Message}");
			}

			var result = Interpret(reply);
			if (result.State == RmsAttachmentScanState.Rejected)
				Logging.LogError($"ClamAV rejected attachment '{fileName}': {result.Detail}");

			return result;
		}

		/// <summary>
		/// Maps a clamd reply ("stream: OK", "stream: Win.Test.EICAR_HDB-1 FOUND", "stream: ... ERROR") to a scan
		/// state. Anything that is neither OK nor FOUND is an engine problem and follows the fail-closed setting.
		/// </summary>
		public static RecordAttachmentScanResult Interpret(string reply)
		{
			var text = (reply ?? string.Empty).Trim('\0', '\r', '\n', ' ');

			if (text.EndsWith("OK", StringComparison.Ordinal))
				return new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean, Detail = "ClamAV: OK" };

			if (text.EndsWith("FOUND", StringComparison.Ordinal))
			{
				var body = text.Substring(text.IndexOf(':') + 1).Trim();
				var signature = body.Substring(0, body.Length - "FOUND".Length).Trim();
				return new RecordAttachmentScanResult { State = RmsAttachmentScanState.Rejected, Detail = $"ClamAV detected {signature}" };
			}

			return Unavailable(text.Length == 0 ? "clamd returned an empty reply" : $"clamd replied: {text}");
		}

		private static RecordAttachmentScanResult Unavailable(string reason)
		{
			return AttachmentScanningConfig.FailClosed
				? new RecordAttachmentScanResult { State = RmsAttachmentScanState.Rejected, Detail = $"Attachment could not be scanned ({reason})" }
				: new RecordAttachmentScanResult { State = RmsAttachmentScanState.Pending, Detail = $"Scan pending ({reason})" };
		}

		private static async Task<string> StreamToClamdAsync(byte[] data, CancellationToken cancellationToken)
		{
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, AttachmentScanningConfig.TimeoutSeconds)));
			var token = budget.Token;

			using var client = new TcpClient();
			await client.ConnectAsync(AttachmentScanningConfig.Host, AttachmentScanningConfig.Port, token);
			using var stream = client.GetStream();

			await stream.WriteAsync(Encoding.ASCII.GetBytes(InstreamCommand), token);

			var chunkSize = Math.Max(1024, AttachmentScanningConfig.ChunkSize);
			var header = new byte[4];
			for (var offset = 0; offset < data.Length; offset += chunkSize)
			{
				var length = Math.Min(chunkSize, data.Length - offset);
				BinaryPrimitives.WriteInt32BigEndian(header, length);
				await stream.WriteAsync(header, token);
				await stream.WriteAsync(data.AsMemory(offset, length), token);
			}

			BinaryPrimitives.WriteInt32BigEndian(header, 0);
			await stream.WriteAsync(header, token);
			await stream.FlushAsync(token);

			var buffer = new byte[MaxReplyBytes];
			var total = 0;
			while (total < buffer.Length)
			{
				var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), token);
				if (read == 0)
					break;

				total += read;
				if (Array.IndexOf(buffer, (byte)0, 0, total) >= 0)
					break;
			}

			return Encoding.ASCII.GetString(buffer, 0, total);
		}
	}
}
