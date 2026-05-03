using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
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
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
				.Returns<PutObjectRequest, CancellationToken>(async (request, _) =>
				{
					using var captureStream = new MemoryStream();
					await request.InputStream.CopyToAsync(captureStream);
					uploadedPayloads.Add(captureStream.ToArray());
					attempt++;

					if (attempt == 1)
					{
						throw new IOException("transient upload failure");
					}

					return new PutObjectResponse();
				});

			var service = CreateService(s3Client.Object);

			await using var content = new NonSeekableReadStream(new byte[] { 1, 2, 3, 4 });

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			uploadedPayloads.Should().HaveCount(2);
			uploadedPayloads[0].Should().Equal(1, 2, 3, 4);
			uploadedPayloads[1].Should().Equal(1, 2, 3, 4);
			s3Client.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}

		[Test]
		public async Task exists_async_should_verify_with_presigned_head_when_metadata_unmarshalling_fails()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(CreateMetadataUnmarshallingException("bad metadata expiration header"));
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Verb.Should().Be(HttpVerb.HEAD);
					request.Protocol.Should().Be(Protocol.HTTP);
					return "http://download.example.com/tts/audio.wav?signature=head";
				});

			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				request.RequestUri.Should().Be(new Uri("http://download.example.com/tts/audio.wav?signature=head"));
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			});
			var service = CreateService(s3Client.Object, handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeTrue();
			handler.Requests.Should().HaveCount(1);
			s3Client.Verify(x => x.GetObjectMetadataAsync(It.Is<GetObjectMetadataRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav"), It.IsAny<CancellationToken>()), Times.Once);
			s3Client.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav" && request.Verb == HttpVerb.HEAD && request.Protocol == Protocol.HTTP)), Times.Once);
		}

		[Test]
		public async Task exists_async_should_return_false_when_presigned_head_reports_missing_after_metadata_unmarshalling_failure()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(CreateMetadataUnmarshallingException("bad metadata expiration header"));
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Verb.Should().Be(HttpVerb.HEAD);
					request.Protocol.Should().Be(Protocol.HTTP);
					return "http://download.example.com/tts/audio.wav?signature=head";
				});

			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			});
			var service = CreateService(s3Client.Object, handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeFalse();
			handler.Requests.Should().HaveCount(1);
			s3Client.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav" && request.Verb == HttpVerb.HEAD && request.Protocol == Protocol.HTTP)), Times.Once);
		}

		[Test]
		public async Task exists_async_should_verify_with_presigned_head_when_metadata_throws_raw_format_exception()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new FormatException("bad metadata expiration header"));
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Verb.Should().Be(HttpVerb.HEAD);
					request.Protocol.Should().Be(Protocol.HTTP);
					return "http://download.example.com/tts/audio.wav?signature=head-raw-format";
				});

			var handler = new RecordingHttpMessageHandler((request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Head);
				request.RequestUri.Should().Be(new Uri("http://download.example.com/tts/audio.wav?signature=head-raw-format"));
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
			});
			var service = CreateService(s3Client.Object, handler, useSsl: false);

			var exists = await service.ExistsAsync("tts/audio.wav", CancellationToken.None);

			exists.Should().BeTrue();
			handler.Requests.Should().HaveCount(1);
			s3Client.Verify(x => x.GetObjectMetadataAsync(It.Is<GetObjectMetadataRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav"), It.IsAny<CancellationToken>()), Times.Once);
			s3Client.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav" && request.Verb == HttpVerb.HEAD && request.Protocol == Protocol.HTTP)), Times.Once);
		}

		[Test]
		public async Task upload_async_should_treat_malformed_put_response_as_success_when_the_object_is_verified()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new FormatException("bad expiration header"));
			s3Client
				.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new GetObjectMetadataResponse());

			var handler = new RecordingHttpMessageHandler((_, _) =>
				Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
			var service = CreateService(s3Client.Object, handler);

			await using var content = new MemoryStream(new byte[] { 9, 8, 7, 6 }, writable: false);

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			handler.Requests.Should().BeEmpty();
			s3Client.Verify(x => x.PutObjectAsync(It.Is<PutObjectRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav" && request.ContentType == "audio/wav"), It.IsAny<CancellationToken>()), Times.Once);
			s3Client.Verify(x => x.GetObjectMetadataAsync(It.Is<GetObjectMetadataRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav"), It.IsAny<CancellationToken>()), Times.Once);
			s3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
		}

		[Test]
		public async Task upload_async_should_retry_with_a_presigned_put_when_verification_reports_the_object_missing()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			byte[] capturedPayload = null;
			byte[] presignedPayload = null;
			s3Client
				.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
				.Returns<PutObjectRequest, CancellationToken>(async (request, _) =>
				{
					using var captureStream = new MemoryStream();
					await request.InputStream.CopyToAsync(captureStream);
					capturedPayload = captureStream.ToArray();
					request.InputStream.Dispose();
					throw new FormatException("bad expiration header");
				});
			s3Client
				.Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(CreateNotFoundS3Exception());
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Verb.Should().Be(HttpVerb.PUT);
					request.Protocol.Should().Be(Protocol.HTTP);
					request.ContentType.Should().Be("audio/wav");
					return "http://upload.example.com/tts/audio.wav?signature=put";
				});

			var handler = new RecordingHttpMessageHandler(async (request, _) =>
			{
				request.Method.Should().Be(HttpMethod.Put);
				request.RequestUri.Should().Be(new Uri("http://upload.example.com/tts/audio.wav?signature=put"));
				request.Content.Headers.ContentType?.MediaType.Should().Be("audio/wav");
				presignedPayload = await request.Content.ReadAsByteArrayAsync();
				return new HttpResponseMessage(HttpStatusCode.OK);
			});
			var service = CreateService(s3Client.Object, handler, useSsl: false);

			await using var content = new MemoryStream(new byte[] { 7, 5, 3, 1 }, writable: false);

			await service.UploadAsync("tts/audio.wav", content, "audio/wav", CancellationToken.None);

			capturedPayload.Should().Equal(7, 5, 3, 1);
			presignedPayload.Should().Equal(7, 5, 3, 1);
			handler.Requests.Should().HaveCount(1);
			s3Client.Verify(x => x.GetObjectMetadataAsync(It.Is<GetObjectMetadataRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav"), It.IsAny<CancellationToken>()), Times.Once);
			s3Client.Verify(x => x.GetPreSignedURL(It.Is<GetPreSignedUrlRequest>(request => request.BucketName == "tts-bucket" && request.Key == "tts/audio.wav" && request.Verb == HttpVerb.PUT && request.Protocol == Protocol.HTTP && request.ContentType == "audio/wav")), Times.Once);
		}

		[Test]
		public async Task get_object_url_async_should_use_http_presigned_urls_when_ssl_is_disabled()
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Protocol.Should().Be(Protocol.HTTP);
					return "http://download.example.com/tts/audio.wav?signature=get";
				});

			var service = CreateService(s3Client.Object, useSsl: false);

			var url = await service.GetObjectUrlAsync("tts/audio.wav", CancellationToken.None);

			url.Should().Be(new Uri("http://download.example.com/tts/audio.wav?signature=get"));
			s3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Once);
		}

		[TestCase("http://rustfs.example.local:9000", true, Protocol.HTTP, "http://download.example.com/tts/audio.wav?signature=endpoint-http")]
		[TestCase("https://rustfs.example.local:9443", false, Protocol.HTTPS, "https://download.example.com/tts/audio.wav?signature=endpoint-https")]
		public async Task get_object_url_async_should_prefer_absolute_endpoint_scheme_over_use_ssl(string endpoint, bool useSsl, Protocol expectedProtocol, string presignedUrl)
		{
			var s3Client = new Mock<IAmazonS3>(MockBehavior.Strict);
			s3Client
				.Setup(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
				.Returns<GetPreSignedUrlRequest>(request =>
				{
					request.BucketName.Should().Be("tts-bucket");
					request.Key.Should().Be("tts/audio.wav");
					request.Protocol.Should().Be(expectedProtocol);
					return presignedUrl;
				});

			var service = CreateService(s3Client.Object, useSsl: useSsl, endpoint: endpoint);

			var url = await service.GetObjectUrlAsync("tts/audio.wav", CancellationToken.None);

			url.Should().Be(new Uri(presignedUrl));
			s3Client.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Once);
		}

		private static AmazonS3Exception CreateNotFoundS3Exception()
		{
			return new AmazonS3Exception(
				"Object was not found.",
				ErrorType.Unknown,
				"NoSuchKey",
				"request-id",
				HttpStatusCode.NotFound);
		}

		private static AmazonUnmarshallingException CreateMetadataUnmarshallingException(string message)
		{
			var innerException = new FormatException(message);

			foreach (var constructor in typeof(AmazonUnmarshallingException).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				var parameters = constructor.GetParameters();
				var arguments = new object[parameters.Length];
				var usedInnerException = false;
				var usedMessage = false;
				var supported = true;

				for (var index = 0; index < parameters.Length; index++)
				{
					var parameterType = parameters[index].ParameterType;

					if (!usedInnerException && typeof(Exception).IsAssignableFrom(parameterType))
					{
						arguments[index] = innerException;
						usedInnerException = true;
						continue;
					}

					if (parameterType == typeof(string))
					{
						arguments[index] = usedMessage ? "/HeadObjectResult" : message;
						usedMessage = true;
						continue;
					}

					if (parameterType == typeof(bool))
					{
						arguments[index] = false;
						continue;
					}

					if (parameterType == typeof(int))
					{
						arguments[index] = 0;
						continue;
					}

					supported = false;
					break;
				}

				if (!supported || !usedInnerException)
				{
					continue;
				}

				try
				{
					if (constructor.Invoke(arguments) is AmazonUnmarshallingException exception
						&& exception.InnerException is FormatException)
					{
						return exception;
					}
				}
				catch
				{
				}
			}

			throw new InvalidOperationException("Unable to construct AmazonUnmarshallingException for the test.");
		}

		private static S3StorageService CreateService(IAmazonS3 s3Client, RecordingHttpMessageHandler handler = null, bool useSsl = true, string endpoint = null)
		{
			handler ??= new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
			var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
			httpClientFactory.Setup(x => x.CreateClient(nameof(S3StorageService))).Returns(new HttpClient(handler, disposeHandler: false));

			return new S3StorageService(
				s3Client,
				Options.Create(new S3StorageOptions
				{
					Bucket = "tts-bucket",
					AccessKey = "access-key",
					SecretKey = "secret-key",
					UseSsl = useSsl,
					Endpoint = endpoint
				}),
				Mock.Of<ILogger<S3StorageService>>(),
				httpClientFactory.Object);
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
