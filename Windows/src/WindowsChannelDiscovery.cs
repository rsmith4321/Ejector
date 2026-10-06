using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEject {
    internal enum WindowsChannelResource { Metadata, Signature }
    internal interface IWindowsChannelTransport {
        Task<byte[]> FetchAsync(ApprovedWindowsChannel channel, WindowsChannelResource resource, CancellationToken cancel);
    }
    // Only two code-approved bounded metadata resources. No archive/download endpoint,
    // credentials, cookies, redirects, decompression or TLS validation override.
    internal sealed class WindowsChannelHttpTransport : IWindowsChannelTransport {
        readonly Func<HttpMessageHandler> handler; readonly TimeSpan deadline;
        internal WindowsChannelHttpTransport() : this(() => CreateHandler(), TimeSpan.FromSeconds(20)) { }
        internal WindowsChannelHttpTransport(Func<HttpMessageHandler> handler, TimeSpan deadline) {
            if (handler == null || deadline <= TimeSpan.Zero || deadline > TimeSpan.FromSeconds(60)) throw new ArgumentException("A bounded channel HTTP handler/deadline is required."); this.handler = handler; this.deadline = deadline;
        }
        internal static HttpClientHandler CreateHandler() {
            return new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false, UseDefaultCredentials = false, Credentials = null, DefaultProxyCredentials = null, ClientCertificateOptions = ClientCertificateOption.Manual, CheckCertificateRevocationList = true };
        }
        public async Task<byte[]> FetchAsync(ApprovedWindowsChannel channel, WindowsChannelResource resource, CancellationToken cancel) {
            if (channel == null) throw new SafetyException("No approved Windows channel is configured; metadata retrieval is unavailable.");
            if (resource != WindowsChannelResource.Metadata && resource != WindowsChannelResource.Signature) throw new ArgumentOutOfRangeException("resource"); cancel.ThrowIfCancellationRequested();
            Uri uri = resource == WindowsChannelResource.Metadata ? channel.Metadata : channel.Signature; int maximum = resource == WindowsChannelResource.Metadata ? WindowsManifestTrust.MaximumManifest : WindowsManifestTrust.MaximumSignature;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                timeout.CancelAfter(deadline); CancellationToken token = timeout.Token;
                try {
                    HttpMessageHandler instance = handler(); if (instance == null) throw new SafetyException("Channel HTTP handler is unavailable.");
                    using (var client = new HttpClient(instance, true)) using (var request = new HttpRequestMessage(HttpMethod.Get, uri)) {
                        client.Timeout = Timeout.InfiniteTimeSpan; client.MaxResponseContentBufferSize = WindowsManifestTrust.MaximumSignature;
                        request.Headers.UserAgent.ParseAdd("EasyEject-Windows-Channel/1"); request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(resource == WindowsChannelResource.Metadata ? "application/json" : "application/pkcs7-signature"));
                        using (var response = await Bounded(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token), token, result => result.Dispose()).ConfigureAwait(false)) {
                            if (response == null || response.StatusCode != HttpStatusCode.OK || response.RequestMessage == null || response.RequestMessage.RequestUri == null || response.RequestMessage.RequestUri.AbsoluteUri != uri.AbsoluteUri || response.Content == null || response.Content.Headers.ContentEncoding.Count != 0) throw new SafetyException("Windows channel retrieval refused redirected, unsuccessful, encoded or retargeted metadata.");
                            long? declared = response.Content.Headers.ContentLength; if (declared.HasValue && (declared.Value <= 0 || declared.Value > maximum)) throw new SafetyException("Windows channel HTTP content length exceeds its supported bound.");
                            using (token.Register(() => response.Dispose())) using (var stream = await Bounded(response.Content.ReadAsStreamAsync(), token, result => result.Dispose()).ConfigureAwait(false)) using (var output = new MemoryStream()) {
                                if (stream == null) throw new SafetyException("Channel response has no readable metadata stream."); byte[] buffer = new byte[4096]; int count;
                                while (true) {
                                    token.ThrowIfCancellationRequested(); count = await Bounded(stream.ReadAsync(buffer, 0, buffer.Length, token), token, null).ConfigureAwait(false); if (count == 0) break;
                                    if (output.Length + count > maximum) throw new SafetyException("Windows channel metadata expands beyond its permitted response bound."); output.Write(buffer, 0, count);
                                }
                                token.ThrowIfCancellationRequested(); if (output.Length == 0 || declared.HasValue && output.Length != declared.Value) throw new SafetyException("Windows channel response has missing or mismatched actual content length."); return output.ToArray();
                            }
                        }
                    }
                } catch (Exception ex) {
                    cancel.ThrowIfCancellationRequested(); if (token.IsCancellationRequested) throw new SafetyException("The bounded Windows channel metadata request timed out.");
                    if (ex is HttpRequestException || ex is IOException) throw new SafetyException("Windows channel metadata could not be retrieved: " + ex.Message); throw;
                }
            }
        }
        // The deadline covers headers AND stream reads, even a misbehaving injected
        // async operation that ignores its cancellation token. Late results are disposed.
        static async Task<T> Bounded<T>(Task<T> operation, CancellationToken cancel, Action<T> dispose) {
            if (operation == null) throw new SafetyException("Channel transport returned no asynchronous operation."); var signal = new TaskCompletionSource<bool>();
            using (cancel.Register(() => signal.TrySetResult(true))) {
                await Task.WhenAny(operation, signal.Task).ConfigureAwait(false);
                if (cancel.IsCancellationRequested) {
                    ObserveLate(operation, dispose);
                    cancel.ThrowIfCancellationRequested();
                }
                return await operation.ConfigureAwait(false);
            }
        }
        static void ObserveLate<T>(Task<T> operation, Action<T> dispose) {
            // This cleanup intentionally outlives the caller's deadline. Observe every
            // late fault, including disposal faults, without awaiting an ignored token.
            var cleanup = operation.ContinueWith(done => { if (done.Status == TaskStatus.RanToCompletion && dispose != null) dispose(done.Result); else if (done.IsFaulted) { var ignored = done.Exception; } }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            cleanup.ContinueWith(done => { var ignored = done.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    internal sealed class WindowsChannelDiscovery {
        readonly ApprovedWindowsChannel channel; readonly IWindowsChannelTransport transport; readonly IWindowsManifestEnvelopeTrust trust; readonly Func<DateTime> clock;
        readonly Func<bool> allowed; readonly WindowsChannelHighWater highWater; int checking;
        // The activity/lifetime callback must be thread safe. Foreground integration
        // must publish an owned scope, rather than reading Form properties off-thread.
        internal WindowsChannelDiscovery(ApprovedWindowsChannel channel, IWindowsChannelTransport transport, IWindowsManifestEnvelopeTrust trust, Func<DateTime> clock, Func<bool> allowed) {
            if (transport == null || trust == null || clock == null || allowed == null) throw new ArgumentNullException("discovery dependencies");
            this.channel = channel; this.transport = transport; this.trust = trust; this.clock = clock; this.allowed = allowed; if (channel != null) highWater = new WindowsChannelHighWater(channel);
        }
        internal bool Configured { get { return channel != null; } }
        void Guard(CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); if (!Configured) throw new SafetyException(WindowsUpdateSafety.ChannelStatus); if (!allowed()) throw new SafetyException("Finish the active import, eject, scan or other dialog before checking Windows releases; a closed review cannot accept results."); }
        static byte[] Freeze(byte[] bytes, int maximum) { if (bytes == null || bytes.Length == 0 || bytes.Length > maximum) throw new SafetyException("The Windows channel transport returned missing or excessive metadata."); return (byte[])bytes.Clone(); }
        internal async Task<WindowsChannelOffer> CheckAsync(CancellationToken cancel) {
            Guard(cancel); if (Interlocked.CompareExchange(ref checking, 1, 0) != 0) throw new SafetyException("A Windows release check is already in progress.");
            try {
                Guard(cancel); byte[] body = Freeze(await transport.FetchAsync(channel, WindowsChannelResource.Metadata, cancel).ConfigureAwait(false), WindowsManifestTrust.MaximumManifest); Guard(cancel);
                byte[] signature = Freeze(await transport.FetchAsync(channel, WindowsChannelResource.Signature, cancel).ConfigureAwait(false), WindowsManifestTrust.MaximumSignature); Guard(cancel);
                var offer = await Task.Run(() => new WindowsChannelOffer(body, signature, channel, trust, clock, cancel), cancel).ConfigureAwait(false); Guard(cancel);
                await Task.Run(() => highWater.Accept(offer, cancel, allowed), cancel).ConfigureAwait(false); Guard(cancel); return offer;
            } finally { Interlocked.Exchange(ref checking, 0); }
        }
    }
}
