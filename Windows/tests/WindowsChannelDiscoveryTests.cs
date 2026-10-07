using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    public static partial class Tests {
        sealed class ChannelHttpFixture : HttpMessageHandler {
            internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond;
            internal int Calls, Disposals;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { Interlocked.Increment(ref Calls); return Respond(request, cancel); }
            protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
        }
        sealed class ChannelBodyFixture : Stream {
            internal readonly byte[] Bytes; internal int PositionValue, Reads, Disposals;
            internal Func<byte[], int, int, CancellationToken, Task<int>> ReadOperation;
            internal ChannelBodyFixture(byte[] bytes) { Bytes = bytes; }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { int size = Math.Min(count, Bytes.Length - PositionValue); Array.Copy(Bytes, PositionValue, buffer, offset, size); PositionValue += size; return size; }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) { Interlocked.Increment(ref Reads); return ReadOperation == null ? Task.FromResult(Read(buffer, offset, count)) : ReadOperation(buffer, offset, count, cancel); }
            protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
            public override void Flush() { throw new NotSupportedException(); } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
        sealed class ChannelTransportFixture : IWindowsChannelTransport {
            internal Func<ApprovedWindowsChannel, WindowsChannelResource, CancellationToken, Task<byte[]>> Fetch;
            internal int Metadata, Signature;
            public Task<byte[]> FetchAsync(ApprovedWindowsChannel channel, WindowsChannelResource resource, CancellationToken cancel) { if (resource == WindowsChannelResource.Metadata) Interlocked.Increment(ref Metadata); else Interlocked.Increment(ref Signature); return Fetch(channel, resource, cancel); }
        }
        sealed class ChannelPendingContent : HttpContent {
            internal readonly TaskCompletionSource<Stream> Pending = new TaskCompletionSource<Stream>(); internal int Opened, Disposals;
            protected override Task<Stream> CreateContentReadStreamAsync() { Interlocked.Increment(ref Opened); return Pending.Task; }
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { if (disposing) Interlocked.Increment(ref Disposals); base.Dispose(disposing); }
        }
        static T ChannelAwait<T>(Func<Task<T>> operation) {
            // Earlier hidden WinForms tests may install a synchronization context.
            // Run on a worker, and bound the test itself against regression deadlocks.
            var task = Task.Run(operation); if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(8000)) throw new Exception("Channel fixture exceeded eight seconds."); return task.GetAwaiter().GetResult();
        }
        static HttpResponseMessage ChannelResponse(HttpRequestMessage request, HttpContent content, HttpStatusCode status = HttpStatusCode.OK) { return new HttpResponseMessage(status) { RequestMessage = request, Content = content }; }
        static byte[] ChannelFetch(ApprovedWindowsChannel channel, ChannelHttpFixture handler, WindowsChannelResource resource = WindowsChannelResource.Metadata, CancellationToken cancel = default(CancellationToken), int deadline = 2000) { return ChannelAwait(() => new WindowsChannelHttpTransport(() => handler, TimeSpan.FromMilliseconds(deadline)).FetchAsync(channel, resource, cancel)); }
        static ChannelTransportFixture ChannelTransport(WindowsChannelFixture f, string text = null) {
            byte[] body = Encoding.UTF8.GetBytes(text ?? f.Text()), signature = f.Archive.Package.Signing.Sign(body);
            return new ChannelTransportFixture { Fetch = (channel, resource, cancel) => Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature) };
        }
        static WindowsChannelDiscovery ChannelDiscovery(WindowsChannelFixture f, ChannelTransportFixture transport, Func<bool> allowed = null) { return new WindowsChannelDiscovery(f.Channel, transport, new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, allowed ?? (() => true)); }
        static WindowsChannelOffer ChannelCheck(WindowsChannelDiscovery discovery, CancellationToken cancel = default(CancellationToken)) { return ChannelAwait(() => discovery.CheckAsync(cancel)); }
        static void WindowsChannelDiscoveryTests() {
            Test("Channel HTTP native policy forbids redirects, cookies, credentials and certificate overrides", () => {
                using (var handler = WindowsChannelHttpTransport.CreateHandler()) Check(!handler.AllowAutoRedirect && handler.AutomaticDecompression == DecompressionMethods.None && !handler.UseCookies && !handler.UseDefaultCredentials && handler.Credentials == null && handler.DefaultProxyCredentials == null && handler.ClientCertificateOptions == ClientCertificateOption.Manual && handler.ClientCertificates.Count == 0 && handler.CheckCertificateRevocationList && handler.ServerCertificateCustomValidationCallback == null, "Native metadata handler relaxed transport policy");
            });
            Test("Channel HTTP only GETs the exact approved metadata/signature without profile or authorization", () => {
                using (var f = new WindowsChannelFixture()) foreach (WindowsChannelResource resource in new[] { WindowsChannelResource.Metadata, WindowsChannelResource.Signature }) {
                    var handler = new ChannelHttpFixture { Respond = (request, cancel) => {
                        Check(request.Method == HttpMethod.Get && request.RequestUri == (resource == WindowsChannelResource.Metadata ? f.Channel.Metadata : f.Channel.Signature) && request.Content == null, "Wrong method, endpoint or request body");
                        Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie") && request.Headers.UserAgent.ToString() == "EasyEject-Windows-Channel/1" && request.Headers.CacheControl.NoCache && request.Headers.CacheControl.NoStore && request.Headers.Count() == 3, "Unsolicited credentials/profile or incorrect cache policy");
                        Check(request.Headers.Accept.Single().MediaType == (resource == WindowsChannelResource.Metadata ? "application/json" : "application/pkcs7-signature"), "Wrong metadata representation"); return Task.FromResult(ChannelResponse(request, new ByteArrayContent(new byte[] { 7, 8 }))); } };
                    Check(ChannelFetch(f.Channel, handler, resource).SequenceEqual(new byte[] { 7, 8 }) && handler.Calls == 1 && handler.Disposals == 1, "Metadata retrieval retried, changed bytes or leaked handler");
                }
            });
            Test("Channel HTTP unconfigured, invalid resource and pre-cancelled requests do not create a handler", () => {
                int factories = 0; var transport = new WindowsChannelHttpTransport(() => { factories++; return null; }, TimeSpan.FromSeconds(1));
                Throws<SafetyException>(() => ChannelAwait(() => transport.FetchAsync(null, WindowsChannelResource.Metadata, CancellationToken.None)));
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) {
                    Throws<ArgumentOutOfRangeException>(() => ChannelAwait(() => transport.FetchAsync(f.Channel, (WindowsChannelResource)7, cancel.Token))); cancel.Cancel();
                    Throws<OperationCanceledException>(() => ChannelAwait(() => transport.FetchAsync(f.Channel, WindowsChannelResource.Metadata, cancel.Token))); Check(factories == 0, "Rejected request constructed transport");
                }
                Throws<ArgumentException>(() => new WindowsChannelHttpTransport(null, TimeSpan.FromSeconds(1))); foreach (int seconds in new[] { 0, -1, 61 }) Throws<ArgumentException>(() => new WindowsChannelHttpTransport(() => null, TimeSpan.FromSeconds(seconds)));
            });
            Test("Channel HTTP refuses redirects and every non-200 status without another request", () => {
                using (var f = new WindowsChannelFixture()) foreach (HttpStatusCode status in new[] { HttpStatusCode.MovedPermanently, HttpStatusCode.Redirect, HttpStatusCode.TemporaryRedirect, HttpStatusCode.NotModified, HttpStatusCode.NoContent, HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError }) {
                    var handler = new ChannelHttpFixture { Respond = (request, cancel) => { var response = ChannelResponse(request, new ByteArrayContent(new byte[] { 1 }), status); response.Headers.Location = new Uri("https://other.invalid/foreign.json"); return Task.FromResult(response); } };
                    Throws<SafetyException>(() => ChannelFetch(f.Channel, handler)); Check(handler.Calls == 1 && handler.Disposals == 1, "Refusal retried or leaked handler");
                }
            });
            Test("Channel HTTP refuses retargeted and encoded responses", () => {
                using (var f = new WindowsChannelFixture()) foreach (bool retarget in new[] { false, true }) {
                    var stream = new ChannelBodyFixture(new byte[] { 1 }); var handler = new ChannelHttpFixture { Respond = (request, cancel) => { var content = new StreamContent(stream); if (!retarget) content.Headers.ContentEncoding.Add("gzip"); return Task.FromResult(ChannelResponse(retarget ? new HttpRequestMessage(HttpMethod.Get, "https://other.invalid/channel.json") : request, content)); } };
                    Throws<SafetyException>(() => ChannelFetch(f.Channel, handler)); Check(stream.Reads == 0 && stream.Disposals > 0 && handler.Calls == 1, "Rejected representation was read or leaked");
                }
            });
            Test("Channel HTTP accepts exact independent 64K/256K response bounds", () => {
                using (var f = new WindowsChannelFixture()) foreach (WindowsChannelResource resource in new[] { WindowsChannelResource.Metadata, WindowsChannelResource.Signature }) {
                    byte[] bytes = Enumerable.Repeat((byte)31, resource == WindowsChannelResource.Metadata ? WindowsManifestTrust.MaximumManifest : WindowsManifestTrust.MaximumSignature).ToArray();
                    var handler = new ChannelHttpFixture { Respond = (request, cancel) => Task.FromResult(ChannelResponse(request, new ByteArrayContent(bytes))) }; Check(ChannelFetch(f.Channel, handler, resource).SequenceEqual(bytes), "Exact transport bound lost bytes");
                }
            });
            Test("Channel HTTP refuses empty and excessive declared lengths before reading", () => {
                using (var f = new WindowsChannelFixture()) foreach (long length in new[] { 0L, WindowsManifestTrust.MaximumManifest + 1L, Int64.MaxValue }) {
                    var stream = new ChannelBodyFixture(new byte[] { 1 }); var handler = new ChannelHttpFixture { Respond = (request, cancel) => { var content = new StreamContent(stream); content.Headers.ContentLength = length; return Task.FromResult(ChannelResponse(request, content)); } };
                    Throws<SafetyException>(() => ChannelFetch(f.Channel, handler)); Check(stream.Reads == 0 && stream.Disposals > 0, "Invalid length read body or leaked stream");
                }
            });
            Test("Channel HTTP accepts unknown streamed length and refuses actual expansion", () => {
                using (var f = new WindowsChannelFixture()) foreach (int size in new[] { 12345, WindowsManifestTrust.MaximumManifest + 1 }) {
                    byte[] bytes = Enumerable.Repeat((byte)17, size).ToArray(); var stream = new ChannelBodyFixture(bytes); var handler = new ChannelHttpFixture { Respond = (request, cancel) => Task.FromResult(ChannelResponse(request, new StreamContent(stream))) };
                    if (size > WindowsManifestTrust.MaximumManifest) Throws<SafetyException>(() => ChannelFetch(f.Channel, handler)); else Check(ChannelFetch(f.Channel, handler).SequenceEqual(bytes), "Unknown-length content changed"); Check(stream.Disposals > 0, "Stream was not released");
                }
            });
            Test("Channel HTTP refuses empty, truncated and inconsistent streamed content", () => {
                using (var f = new WindowsChannelFixture()) foreach (int declared in new[] { -1, 1, 3 }) {
                    var stream = new ChannelBodyFixture(declared == -1 ? new byte[0] : new byte[] { 1, 2 }); var handler = new ChannelHttpFixture { Respond = (request, cancel) => { var content = new StreamContent(stream); if (declared >= 0) content.Headers.ContentLength = declared; return Task.FromResult(ChannelResponse(request, content)); } };
                    Throws<SafetyException>(() => ChannelFetch(f.Channel, handler)); Check(stream.Disposals > 0, "Inconsistent body was retained");
                }
            });
            Test("Channel HTTP wraps request and read failures and releases owned transport", () => {
                using (var f = new WindowsChannelFixture()) {
                    var requestFailure = new ChannelHttpFixture { Respond = (request, cancel) => { throw new HttpRequestException("fixture network refusal"); } };
                    Check(Throws<SafetyException>(() => ChannelFetch(f.Channel, requestFailure)).Message.Contains("could not be retrieved"), "Request error lost actionable refusal"); Check(requestFailure.Disposals == 1, "Failed request leaked handler");
                    var stream = new ChannelBodyFixture(new byte[0]) { ReadOperation = (buffer, offset, count, cancel) => { throw new IOException("fixture stream failure"); } }; var readFailure = new ChannelHttpFixture { Respond = (request, cancel) => Task.FromResult(ChannelResponse(request, new StreamContent(stream))) };
                    Check(Throws<SafetyException>(() => ChannelFetch(f.Channel, readFailure)).Message.Contains("could not be retrieved") && stream.Disposals > 0 && readFailure.Disposals == 1, "Failed stream leaked ownership");
                }
            });
            Test("Channel HTTP total deadline ends ignored header cancellation and disposes a late response", () => {
                using (var f = new WindowsChannelFixture()) using (var entered = new ManualResetEventSlim()) {
                    var pending = new TaskCompletionSource<HttpResponseMessage>(); HttpRequestMessage seen = null;
                    var handler = new ChannelHttpFixture { Respond = (request, cancel) => { seen = request; entered.Set(); return pending.Task; } };
                    Check(Throws<SafetyException>(() => ChannelFetch(f.Channel, handler, deadline: 250)).Message.Contains("timed out") && entered.IsSet && handler.Disposals == 1, "Ignored header operation escaped deadline");
                    var stream = new ChannelBodyFixture(new byte[] { 1 }); pending.SetResult(ChannelResponse(seen, new StreamContent(stream))); Check(SpinWait.SpinUntil(() => stream.Disposals > 0, 2000), "Late header response leaked its content");
                }
            });
            Test("Channel HTTP caller cancellation wins over late header failures and permits a fresh request", () => {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) using (var entered = new ManualResetEventSlim()) {
                    var pending = new TaskCompletionSource<HttpResponseMessage>(); var handler = new ChannelHttpFixture { Respond = (request, token) => { entered.Set(); return pending.Task; } };
                    var work = Task.Run(() => new WindowsChannelHttpTransport(() => handler, TimeSpan.FromSeconds(2)).FetchAsync(f.Channel, WindowsChannelResource.Metadata, cancel.Token)); Check(entered.Wait(2000), "Header fixture did not start"); cancel.Cancel();
                    Throws<OperationCanceledException>(() => ChannelAwait(() => work)); pending.SetException(new HttpRequestException("late fixture failure")); Check(handler.Disposals == 1, "Cancelled headers leaked handler");
                    var fresh = new ChannelHttpFixture { Respond = (request, token) => Task.FromResult(ChannelResponse(request, new ByteArrayContent(new byte[] { 9 }))) }; Check(ChannelFetch(f.Channel, fresh).Single() == 9 && fresh.Calls == 1, "Manual fresh request failed");
                }
            });
            Test("Channel HTTP body deadline ends token-ignoring reads and releases the private stream", () => {
                using (var f = new WindowsChannelFixture()) {
                    var pending = new TaskCompletionSource<int>(); var stream = new ChannelBodyFixture(new byte[0]) { ReadOperation = (buffer, offset, count, token) => pending.Task };
                    var handler = new ChannelHttpFixture { Respond = (request, cancel) => Task.FromResult(ChannelResponse(request, new StreamContent(stream))) };
                    Check(Throws<SafetyException>(() => ChannelFetch(f.Channel, handler, deadline: 250)).Message.Contains("timed out") && stream.Reads == 1 && stream.Disposals > 0 && handler.Disposals == 1, "Ignored read escaped total deadline or leaked resources"); pending.SetResult(1);
                }
            });
            Test("Channel HTTP total deadline covers a stalled stream handoff and disposes its late stream", () => {
                using (var f = new WindowsChannelFixture()) {
                    var content = new ChannelPendingContent(); var handler = new ChannelHttpFixture { Respond = (request, cancel) => Task.FromResult(ChannelResponse(request, content)) };
                    Check(Throws<SafetyException>(() => ChannelFetch(f.Channel, handler, deadline: 250)).Message.Contains("timed out") && content.Opened == 1 && content.Disposals > 0 && handler.Disposals == 1, "Stream acquisition escaped deadline or leaked content");
                    var late = new ChannelBodyFixture(new byte[] { 1 }); content.Pending.SetResult(late); Check(SpinWait.SpinUntil(() => late.Disposals > 0, 2000), "Late stream handoff leaked");
                }
            });
            Test("Channel HTTP mid-body caller cancellation returns cancellation without accepting late bytes", () => {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) using (var entered = new ManualResetEventSlim()) {
                    var pending = new TaskCompletionSource<int>(); byte[] privateBuffer = null; var stream = new ChannelBodyFixture(new byte[0]) { ReadOperation = (buffer, offset, count, token) => { privateBuffer = buffer; entered.Set(); return pending.Task; } };
                    var handler = new ChannelHttpFixture { Respond = (request, token) => Task.FromResult(ChannelResponse(request, new StreamContent(stream))) };
                    var work = Task.Run(() => new WindowsChannelHttpTransport(() => handler, TimeSpan.FromSeconds(2)).FetchAsync(f.Channel, WindowsChannelResource.Metadata, cancel.Token)); Check(entered.Wait(2000), "Body fixture did not start"); cancel.Cancel(); Throws<OperationCanceledException>(() => ChannelAwait(() => work)); privateBuffer[0] = 33; pending.SetResult(1);
                    Check(work.IsCanceled && stream.Disposals > 0 && handler.Disposals == 1, "Late bytes became a response or resources leaked");
                }
            });
            Test("Channel discovery authenticates metadata then signature with fresh native trust and no archive fetch", () => {
                using (var f = new WindowsChannelFixture()) {
                    var transport = ChannelTransport(f); var discovery = ChannelDiscovery(f, transport); var offer = ChannelCheck(discovery);
                    Check(discovery.Configured && offer.Release.Version == "0.1.0.0" && transport.Metadata == 1 && transport.Signature == 1 && f.Archive.Package.CertificateTrust.Calls == 2, "Wrong fetch/trust sequence or extra operation");
                }
            });
            Test("Channel discovery unconfigured or denied admission never fetches or authenticates", () => {
                using (var f = new WindowsChannelFixture()) {
                    var transport = ChannelTransport(f); var unavailable = new WindowsChannelDiscovery(null, transport, new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, () => true);
                    Check(!unavailable.Configured, "Missing channel became configured"); Throws<SafetyException>(() => ChannelCheck(unavailable));
                    foreach (string state in new[] { "import", "eject", "scan", "dialog", "closed" }) { var discovery = ChannelDiscovery(f, transport, () => false); Throws<SafetyException>(() => ChannelCheck(discovery)); }
                    Check(transport.Metadata == 0 && transport.Signature == 0 && f.Archive.Package.CertificateTrust.Calls == 0, "Denied admission performed work");
                }
            });
            Test("Channel discovery pre-cancellation and null dependencies cannot start work", () => {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) {
                    var transport = ChannelTransport(f); var discovery = ChannelDiscovery(f, transport); cancel.Cancel(); Throws<OperationCanceledException>(() => ChannelCheck(discovery, cancel.Token)); Check(transport.Metadata == 0 && transport.Signature == 0, "Cancelled discovery fetched data");
                    Throws<ArgumentNullException>(() => new WindowsChannelDiscovery(f.Channel, null, new WindowsManifestTrust(f.Archive.Package.CertificateTrust), () => f.Now, () => true));
                }
            });
            Test("Channel discovery freezes metadata before another transport callback can mutate it", () => {
                using (var f = new WindowsChannelFixture()) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body); var transport = new ChannelTransportFixture { Fetch = (channel, resource, cancel) => { if (resource == WindowsChannelResource.Metadata) return Task.FromResult(body); body[0] = 0; return Task.FromResult(signature); } };
                    Check(ChannelCheck(ChannelDiscovery(f, transport)).Release.Version == "0.1.0.0" && body[0] == 0, "Caller mutated frozen metadata");
                }
            });
            Test("Channel discovery refuses missing or excessive transport metadata before signature/trust", () => {
                using (var f = new WindowsChannelFixture()) foreach (byte[] bytes in new[] { null, new byte[0], new byte[WindowsManifestTrust.MaximumManifest + 1] }) {
                    var transport = new ChannelTransportFixture { Fetch = (channel, resource, cancel) => Task.FromResult(bytes) }; Throws<SafetyException>(() => ChannelCheck(ChannelDiscovery(f, transport))); Check(transport.Metadata == 1 && transport.Signature == 0 && f.Archive.Package.CertificateTrust.Calls == 0, "Unbounded metadata advanced discovery");
                }
            });
            Test("Channel discovery refuses missing or excessive signature before native trust", () => {
                using (var f = new WindowsChannelFixture()) foreach (byte[] signature in new[] { null, new byte[0], new byte[WindowsManifestTrust.MaximumSignature + 1] }) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text()); var transport = new ChannelTransportFixture { Fetch = (channel, resource, cancel) => Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature) };
                    Throws<SafetyException>(() => ChannelCheck(ChannelDiscovery(f, transport))); Check(transport.Metadata == 1 && transport.Signature == 1 && f.Archive.Package.CertificateTrust.Calls == 0, "Unbounded signature reached trust");
                }
            });
            Test("Channel discovery refuses mismatched exact bytes without retries and permits manual retry", () => {
                using (var f = new WindowsChannelFixture()) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body); body[0] = 0;
                    var transport = new ChannelTransportFixture { Fetch = (channel, resource, cancel) => Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature) }; var discovery = ChannelDiscovery(f, transport);
                    Throws<SafetyException>(() => ChannelCheck(discovery)); Check(transport.Metadata == 1 && transport.Signature == 1 && f.Archive.Package.CertificateTrust.Calls == 0, "Cryptographic mismatch retried or trusted"); body[0] = (byte)'{'; Check(ChannelCheck(discovery).Release.Version == "0.1.0.0" && transport.Metadata == 2, "Failed check kept its slot");
                }
            });
            Test("Channel discovery scope loss after either fetch prevents subsequent work", () => {
                using (var f = new WindowsChannelFixture()) foreach (WindowsChannelResource lostAt in new[] { WindowsChannelResource.Metadata, WindowsChannelResource.Signature }) {
                    int allowed = 1; byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body);
                    var transport = new ChannelTransportFixture { Fetch = (channel, resource, cancel) => { if (resource == lostAt) Interlocked.Exchange(ref allowed, 0); return Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature); } };
                    Throws<SafetyException>(() => ChannelCheck(ChannelDiscovery(f, transport, () => Volatile.Read(ref allowed) != 0))); Check(transport.Metadata == 1 && transport.Signature == (lostAt == WindowsChannelResource.Metadata ? 0 : 1) && f.Archive.Package.CertificateTrust.Calls == 0, "Lost scope advanced fetch/authentication");
                }
            });
            Test("Channel discovery cancellation after each fetch stops signature or native authentication", () => {
                using (var f = new WindowsChannelFixture()) foreach (WindowsChannelResource cancelledAt in new[] { WindowsChannelResource.Metadata, WindowsChannelResource.Signature }) using (var cancel = new CancellationTokenSource()) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text()), signature = f.Archive.Package.Signing.Sign(body);
                    var transport = new ChannelTransportFixture { Fetch = (channel, resource, token) => { if (resource == cancelledAt) cancel.Cancel(); return Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature); } };
                    Throws<OperationCanceledException>(() => ChannelCheck(ChannelDiscovery(f, transport), cancel.Token)); Check(transport.Metadata == 1 && transport.Signature == (cancelledAt == WindowsChannelResource.Metadata ? 0 : 1) && f.Archive.Package.CertificateTrust.Calls == 0, "Cancelled retrieval advanced discovery");
                }
            });
            Test("Channel discovery refuses a signed record that expires during metadata retrieval", () => {
                using (var f = new WindowsChannelFixture()) {
                    byte[] body = Encoding.UTF8.GetBytes(f.Text(expires: f.Seconds + 60)), signature = f.Archive.Package.Signing.Sign(body);
                    var transport = new ChannelTransportFixture { Fetch = (channel, resource, token) => { if (resource == WindowsChannelResource.Signature) f.Now = f.Now.AddMinutes(2); return Task.FromResult(resource == WindowsChannelResource.Metadata ? body : signature); } }; var discovery = ChannelDiscovery(f, transport);
                    Check(Throws<SafetyException>(() => ChannelCheck(discovery)).Message.Contains("expired"), "Expired network result became an offer"); transport.Fetch = ChannelTransport(f).Fetch; Check(ChannelCheck(discovery).Release.Version == "0.1.0.0", "Expired check retained its slot");
                }
            });
            Test("Channel discovery refuses concurrent checks and releases a cancelled slot", () => {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) using (var entered = new ManualResetEventSlim()) {
                    var pending = new TaskCompletionSource<byte[]>(); var transport = new ChannelTransportFixture { Fetch = (channel, resource, token) => { token.Register(() => pending.TrySetCanceled()); entered.Set(); return pending.Task; } }; var discovery = ChannelDiscovery(f, transport);
                    var work = Task.Run(() => discovery.CheckAsync(cancel.Token)); Check(entered.Wait(2000), "Pending discovery did not enter"); Check(Throws<SafetyException>(() => ChannelCheck(discovery)).Message.Contains("already in progress") && transport.Metadata == 1, "Duplicate started another request"); cancel.Cancel(); Throws<OperationCanceledException>(() => ChannelAwait(() => work));
                    transport.Fetch = ChannelTransport(f).Fetch; Check(ChannelCheck(discovery).Release.Version == "0.1.0.0" && transport.Metadata == 2 && transport.Signature == 1, "Cancelled check did not release slot");
                }
            });
            Test("Channel discovery native trust failure retains manual retry and returns no offer", () => {
                using (var f = new WindowsChannelFixture()) {
                    var transport = ChannelTransport(f); var discovery = ChannelDiscovery(f, transport); f.Archive.Package.CertificateTrust.Throw = true; Throws<SafetyException>(() => ChannelCheck(discovery)); f.Archive.Package.CertificateTrust.Throw = false;
                    Check(ChannelCheck(discovery).Release.Version == "0.1.0.0" && transport.Metadata == 2 && transport.Signature == 2 && f.Archive.Package.CertificateTrust.Calls == 3, "Failed trust consumed slot or automatically retried");
                }
            });
            Test("Channel discovery scope loss during initial/final native trust never consumes replay state", () => {
                using (var f = new WindowsChannelFixture()) foreach (int lostAt in new[] { 1, 2 }) {
                    int allowed = 1, start = f.Archive.Package.CertificateTrust.Calls; var transport = ChannelTransport(f, f.Text("0.2.0.0", f.Seconds - 20)); var discovery = ChannelDiscovery(f, transport, () => Volatile.Read(ref allowed) != 0);
                    f.Archive.Package.CertificateTrust.During = () => { if (f.Archive.Package.CertificateTrust.Calls - start == lostAt) Interlocked.Exchange(ref allowed, 0); };
                    Throws<SafetyException>(() => ChannelCheck(discovery)); Check(f.Archive.Package.CertificateTrust.Calls - start == lostAt, "Lost native scope continued authentication"); f.Archive.Package.CertificateTrust.During = null; Interlocked.Exchange(ref allowed, 1);
                    transport.Fetch = ChannelTransport(f, f.Text("0.1.0.0", f.Seconds - 50)).Fetch; Check(ChannelCheck(discovery).Release.Version == "0.1.0.0", "Failed scope advanced high water");
                }
            });
            Test("Channel discovery cancellation during final native trust does not consume replay state", () => {
                using (var f = new WindowsChannelFixture()) using (var cancel = new CancellationTokenSource()) {
                    var transport = ChannelTransport(f, f.Text("0.2.0.0", f.Seconds - 20)); var discovery = ChannelDiscovery(f, transport); f.Archive.Package.CertificateTrust.During = () => { if (f.Archive.Package.CertificateTrust.Calls == 2) cancel.Cancel(); };
                    Throws<OperationCanceledException>(() => ChannelCheck(discovery, cancel.Token)); f.Archive.Package.CertificateTrust.During = null; transport.Fetch = ChannelTransport(f, f.Text("0.1.0.0", f.Seconds - 50)).Fetch;
                    Check(ChannelCheck(discovery).Release.Version == "0.1.0.0", "Cancelled native acceptance advanced replay state");
                }
            });
            Test("Channel discovery enforces accepted process replay ordering across later checks", () => {
                using (var f = new WindowsChannelFixture()) {
                    var transport = ChannelTransport(f, f.Text("0.2.0.0", f.Seconds - 20)); var discovery = ChannelDiscovery(f, transport); Check(ChannelCheck(discovery).Release.Version == "0.2.0.0", "Initial offer failed");
                    transport.Fetch = ChannelTransport(f, f.Text("0.1.0.0", f.Seconds - 50)).Fetch; Check(Throws<SafetyException>(() => ChannelCheck(discovery)).Message.Contains("rolled back"), "Older discovery overwrote accepted high water");
                    transport.Fetch = ChannelTransport(f, f.Text("0.2.0.0", f.Seconds - 20)).Fetch; Check(ChannelCheck(discovery).Release.Version == "0.2.0.0" && transport.Metadata == 3 && transport.Signature == 3, "Refusal blocked equivalent manual retry");
                }
            });
        }
    }
}
