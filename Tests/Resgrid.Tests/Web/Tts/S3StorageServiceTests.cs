using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Resgrid.Web.Tts.Configuration;
using Resgrid.Web.Tts.Services;

namespace Resgrid.Tests.Web.Tts
{
	[TestFixture]
	public class S3StorageServiceTests
	{
		[Test]
		public async Task upload_async_should_buffer_non_seekable_stream_for_retries()
		{
			var uploadedPayloads = new List<byte[]>();
			var attempt = 0;
			var handler = new RecordingHttpMessageHandler(async (request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Put);
				var body = await request.Content.ReadAsByteArrayAsync();
				uploadedPayloads.Add(body);
				attempt++;

				if (attempt == 1)
				{
					throw new IOException("transient upload failure");
				}

				return new HttpResponseMessage(HttpStatusCode.OK);
			});

			var service = CreateService(handler);

			await using var content = new NonSeekableReadStream(new byte[] { 1, 2, 3, 4 });

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			uploadedPayloads.Should().HaveCount(2);
			uploadedPayloads[0].Should().Equal(1, 2, 3, 4);
			uploadedPayloads[1].Should().Equal(1, 2, 3, 4);
		}

		[Test]
		public async Task exists_async_should_return_true_when_head_returns_200()
		{
			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			});

			var service = CreateService(handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeTrue();
			handler.Requests.Should().HaveCount(1);
			handler.Requests[0].Method.Should().Be(HttpMethod.Head);
		}

		[Test]
		public async Task exists_async_should_return_false_when_head_returns_404()
		{
			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			});

			var service = CreateService(handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeFalse();
			handler.Requests.Should().HaveCount(1);
		}

		[Test]
		public async Task exists_async_should_return_false_when_head_returns_403()
		{
			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
			});

			var service = CreateService(handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeFalse();
			handler.Requests.Should().HaveCount(1);
		}

		[Test]
		public async Task upload_async_should_succeed_on_200_response()
		{
			byte[] capturedPayload = null;
			var handler = new RecordingHttpMessageHandler(async (request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Put);
				request.Content.Headers.ContentType?.MediaType.Should().Be("audio/wav");
				capturedPayload = await request.Content.ReadAsByteArrayAsync();
				return new HttpResponseMessage(HttpStatusCode.OK);
			});

			var service = CreateService(handler);

			await using var content = new MemoryStream(new byte[] { 9, 8, 7, 6 }, writable: false);

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			capturedPayload.Should().Equal(9, 8, 7, 6);
			handler.Requests.Should().HaveCount(1);
			handler.Requests[0].RequestUri!.PathAndQuery.Should().Contain("tts/audio.wav");
		}

		[Test]
		public async Task upload_async_should_retry_on_transient_error()
		{
			byte[] capturedPayload = null;
			var attempt = 0;
			var handler = new RecordingHttpMessageHandler(async (request, _) =>
			{
				attempt++;
				if (attempt == 1)
				{
					throw new HttpRequestException("simulated transient failure", null, HttpStatusCode.ServiceUnavailable);
				}

				capturedPayload = await request.Content.ReadAsByteArrayAsync();
				return new HttpResponseMessage(HttpStatusCode.OK);
			});

			var service = CreateService(handler);

			await using var content = new MemoryStream(new byte[] { 7, 5, 3, 1 }, writable: false);

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			capturedPayload.Should().Equal(7, 5, 3, 1);
			handler.Requests.Should().HaveCount(2);
		}

		[Test]
		public async Task get_object_url_async_should_produce_valid_absolute_uri()
		{
			var handler = new RecordingHttpMessageHandler((_, _) =>
				Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

			var service = CreateService(handler);

			var url = await service.GetObjectUrlAsync("tts/audio.wav", CancellationToken.None);

			url.IsAbsoluteUri.Should().BeTrue();
			url.AbsoluteUri.Should().Contain("tts/audio.wav");
			url.Scheme.Should().Be("https");
		}

		[Test]
		public async Task get_object_url_async_should_contain_object_key()
		{
			var handler = new RecordingHttpMessageHandler((_, _) =>
				Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

			var service = CreateService(handler);

			var url = await service.GetObjectUrlAsync("tts/audio.wav", CancellationToken.None);

			url.PathAndQuery.Should().Contain("tts/audio.wav");
		}

		private static S3StorageService CreateService(RecordingHttpMessageHandler handler = null, bool useSsl = true, string endpoint = null)
		{
			handler ??= new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
			var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
			httpClientFactory.Setup(x => x.CreateClient(nameof(S3StorageService))).Returns(new HttpClient(handler, disposeHandler: false));

			return new S3StorageService(
				httpClientFactory.Object,
				Options.Create(new S3StorageOptions
				{
					Bucket = "tts-bucket",
					AccessKey = "access-key",
					SecretKey = "secret-key",
					UseSsl = useSsl,
					Endpoint = endpoint
				}),
				Mock.Of<ILogger<S3StorageService>>());
		}

		private sealed class RecordingHttpMessageHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

			public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
			{
				_handler = handler;
			}

			public List<HttpRequestMessage> Requests { get; } = new();

			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				Requests.Add(request);
				return await _handler(request, cancellationToken);
			}
		}

		private sealed class NonSeekableReadStream : Stream
		{
			private readonly MemoryStream _inner;

			public NonSeekableReadStream(byte[] bytes)
			{
				_inner = new MemoryStream(bytes);
			}

			public override bool CanRead => true;

			public override bool CanSeek => false;

			public override bool CanWrite => false;

			public override long Length => _inner.Length;

			public override long Position
			{
				get => throw new NotSupportedException();
				set => throw new NotSupportedException();
			}

			public override void Flush()
			{
			}

			public override int Read(byte[] buffer, int offset, int count)
			{
				return _inner.Read(buffer, offset, count);
			}

			public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			{
				return _inner.ReadAsync(buffer, offset, count, cancellationToken);
			}

			public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			{
				return _inner.ReadAsync(buffer, cancellationToken);
			}

			public override long Seek(long offset, SeekOrigin origin)
			{
				throw new NotSupportedException();
			}

			public override void SetLength(long value)
			{
				throw new NotSupportedException();
			}

			public override void Write(byte[] buffer, int offset, int count)
			{
				throw new NotSupportedException();
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing)
				{
					_inner.Dispose();
				}

				base.Dispose(disposing);
			}

			public override async ValueTask DisposeAsync()
			{
				await _inner.DisposeAsync();
				await base.DisposeAsync();
			}
		}
	}
}
