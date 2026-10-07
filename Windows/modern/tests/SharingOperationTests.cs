using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        sealed class ModernShareProcess : IQuickShareProcess {
            internal int Probes, Encodes, Decodes; internal bool BadOutput;
            internal Action<string> Stage = stage => {};
            internal readonly List<string> Inputs = new List<string>();
            readonly Dictionary<string, QuickSharePlan> output = new Dictionary<string, QuickSharePlan>();
            static QuickShareProbe Input() { return QuickShareProbe.Parse(Encoding.UTF8.GetBytes("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"hevc\",\"width\":3840,\"height\":2160,\"r_frame_rate\":\"60/1\",\"duration\":\"5.0\",\"color_transfer\":\"bt709\"}],\"format\":{\"duration\":\"5.0\"}}")); }
            public byte[] Run(string executable, string[] args, string directory, double timeout, CancellationToken cancel, Action validate, Action<string> progress = null) {
                cancel.ThrowIfCancellationRequested(); validate();
                if (args.Contains("-show_streams")) {
                    Probes++; string path = args.Last(); var probe = Input(); QuickSharePlan plan;
                    if (output.TryGetValue(path, out plan)) {
                        probe.Video.Codec = BadOutput ? "hevc" : "h264"; probe.Video.Width = plan.Width; probe.Video.Height = plan.Height; probe.Video.FrameRate = "30/1";
                        probe.Video.PixelFormat = "yuv420p"; probe.Video.Range = "tv"; probe.Video.ColorSpace = probe.Video.Primaries = probe.Video.Transfer = "bt709"; probe.Format.Name = "mov,mp4,m4a,3gp,3g2,mj2";
                    }
                    using (var json = new MemoryStream()) { new DataContractJsonSerializer(typeof(QuickShareProbe)).WriteObject(json, probe); return json.ToArray(); }
                }
                if (args.Contains("null")) { Decodes++; Check(args.Contains("-xerror") && !args.Contains("-t"), "Not full decode"); Stage("decode"); }
                else {
                    Encodes++; string source = args[Array.IndexOf(args, "-i") + 1], target = args.Last(); Inputs.Add(source);
                    var plan = new QuickSharePlan(Input(), args[Array.IndexOf(args, "-b:v") + 1] == "24000000" ? QuickSharePreset.Uhd : QuickSharePreset.Hd, QuickShareColor.Standard);
                    Check(args.Contains("-n") && !File.Exists(target), "Encoder overwrite enabled");
                    File.WriteAllText(target, "synthetic " + plan.PresetName + " " + File.ReadAllText(source)); output[target] = plan; Stage("encoded");
                    if (progress != null) progress("out_time_us=1000000");
                }
                cancel.ThrowIfCancellationRequested(); validate(); return new byte[0];
            }
        }
        sealed class ModernSharingFixture {
            internal readonly ModernImportFixture Import = new ModernImportFixture();
            internal readonly ModernShareProcess Process = new ModernShareProcess();
            internal readonly ManualQuickShareService Service;
            internal CompletedBatch Batch;
            internal ModernSharingFixture() {
                Import.Profile.SharingPreset = QuickShareProfile.Hd; Import.Profile.SharingColor = QuickShareProfile.Standard; Import.Profile.VideosOnly = false; Profiles.Save(Import.Profile, Import.Config);
                Import.Put("VID_fixture.mp4", Encoding.UTF8.GetBytes("generated fixture Insta360 Luna Ultra identity")); Import.Put("photo.jpg"); Record();
                string tools = Path.Combine(Import.Root, "supplied-tools"); Directory.CreateDirectory(tools);
                string ffmpeg = Path.Combine(tools, "ffmpeg.exe"), ffprobe = Path.Combine(tools, "ffprobe.exe"); File.WriteAllText(ffmpeg, "synthetic never executed"); File.WriteAllText(ffprobe, "synthetic never executed");
                QuickShareConfiguration.Save(QuickShareConfiguration.Load(Import.Config), ffmpeg, ffprobe, null, Import.Config, CancellationToken.None);
                Service = new ManualQuickShareService(Import.Config, Import.Operations, Process,
                    batch => new WindowsDestinationGuard(Import.Discovery, batch.DestinationRoot, batch.Destination, Import.Resolve), path => UInt64.MaxValue);
            }
            internal void Record() { using (var review = Import.Manual()) { review.Confirm(); var result = review.Execute(Import.Importer, CancellationToken.None, message => {}); Batch = Import.Outcome(result, review).Batch; } }
            internal ManualQuickShareReview Review() { return new ManualQuickShareReview(Service.Prepare(Batch)); }
            internal QuickShareResult Run(ManualQuickShareRequest request, CancellationToken cancel = default(CancellationToken)) { return Service.Run(request, cancel, message => {}); }
            internal void Unchanged() { ImportHistory.Verify(Batch, Batch.Destination, batch => new WindowsDestinationGuard(Import.Discovery, batch.DestinationRoot, batch.Destination, Import.Resolve), Import.Operations, CancellationToken.None, message => {}); Check(Import.Operations.Available(new[] { 7, 8 }), "Reservations leaked"); }
        }
        static void SharingOperationTests() {
            Test("modern manual sharing cancellation and closed review never authorize export", delegate {
                var f = new ModernSharingFixture(); var review = f.Review(); review.Set(QuickSharePreset.Uhd, QuickShareColor.LunaILog); review.Cancel();
                Throws<SafetyException>(() => review.Confirm()); Throws<InvalidOperationException>(() => review.Set(QuickSharePreset.Hd, QuickShareColor.Standard));
                Check(f.Process.Probes == 0 && !Directory.Exists(Path.Combine(f.Batch.Folder, "Sharing Copies")), "Review/cancel started work"); f.Unchanged();
            });
            Test("modern each export freezes explicit choices independently of profile preferences", delegate {
                var f = new ModernSharingFixture(); var review = f.Review(); Check(review.Preset == QuickSharePreset.Hd && review.Color == QuickShareColor.Standard, "Preferences lost");
                var entry = Profiles.Catalog(f.Import.Config).Single(); var edited = entry.Profile; edited.SharingPreset = QuickShareProfile.Uhd; edited.SharingColor = QuickShareProfile.LunaILog; Profiles.SaveOffline(entry, edited, f.Import.Config);
                review.Batch.Originals.Clear(); review.Set(QuickSharePreset.Hd, QuickShareColor.Standard); var request = review.Confirm(); Throws<SafetyException>(() => review.Confirm());
                request.Batch.Originals.Clear(); var result = f.Run(request); Check(result.Created == 1 && result.Outputs.Single().EndsWith("-sharing-1080p.mp4") && request.Batch.Originals.Count == 2, "Explicit/immutable choice changed");
                var next = f.Review(); Check(next.Preset == QuickSharePreset.Uhd && next.Color == QuickShareColor.LunaILog, "New prompt did not use current preferences"); next.Cancel(); f.Unchanged();
            });
            Test("modern saved history shares exact originals after source absence and service restart", delegate {
                var f = new ModernSharingFixture(); f.Import.Discovery.State.Volumes.RemoveAt(0); f.Import.Discovery.State.Disks.RemoveAt(0); f.Import.Discovery.State.Epoch = ++f.Import.Discovery.Version;
                File.WriteAllText(Path.Combine(f.Batch.Folder, "VID_unrecorded.mp4"), "unrecorded Insta360 Luna Ultra");
                var service = new ManualQuickShareService(f.Import.Config, f.Import.Operations, f.Process, batch => new WindowsDestinationGuard(f.Import.Discovery, batch.DestinationRoot, batch.Destination, f.Import.Resolve), path => UInt64.MaxValue);
                var review = new ManualQuickShareReview(service.Prepare(ImportHistory.Load(f.Import.Config).Single())); review.Set(QuickSharePreset.Uhd, QuickShareColor.Standard);
                var result = service.Run(review.Confirm(), CancellationToken.None, message => {});
                Check(result.Created == 1 && f.Process.Decodes == 1 && f.Process.Probes == 2 && f.Process.Inputs.SequenceEqual(f.Batch.Originals.Where(x => x.Path.EndsWith(".mp4")).Select(x => x.Path)) && !Directory.GetFiles(result.Folder).Any(p => p.Contains("unrecorded")), "Source absence scanned/changed exact batch or skipped validation"); f.Unchanged();
            });
            Test("modern fresh manual retry reuses verified exports and preserves separate 4K copies", delegate {
                var f = new ModernSharingFixture(); var first = f.Run(f.Review().Confirm()); byte[] previous = File.ReadAllBytes(first.Outputs.Single());
                var reuse = f.Run(f.Review().Confirm()); Check(reuse.Created == 0 && reuse.Reused == 1 && f.Process.Encodes == 1, "Reuse reencoded/unverified");
                var four = f.Review(); four.Set(QuickSharePreset.Uhd, QuickShareColor.Standard); var result = f.Run(four.Confirm());
                Check(result.Created == 1 && File.ReadAllBytes(first.Outputs.Single()).SequenceEqual(previous) && result.Outputs.Single().EndsWith("-sharing-4K.mp4"), "4K overwrote first copy"); f.Unchanged();
            });
            Test("modern changed saved companion refuses sharing before any probe or encode", delegate {
                var f = new ModernSharingFixture(); var request = f.Review().Confirm(); File.WriteAllText(f.Batch.Originals.Single(x => x.Path.EndsWith(".jpg")).Path, "changed generated photo");
                Throws<QuickShareFailure>(() => f.Run(request)); Check(f.Process.Probes == 0 && !Directory.Exists(Path.Combine(f.Batch.Folder, "Sharing Copies")) && f.Import.Operations.Available(new[] { 7, 8 }), "Changed exact original reached tools/leaked reservation");
            });
            Test("modern disabled or forgotten profile rejects a previously confirmed export", delegate {
                var f = new ModernSharingFixture(); var request = f.Review().Confirm(); var entry = Profiles.Catalog(f.Import.Config).Single(); var value = entry.Profile; value.SharingPreset = null; Profiles.SaveOffline(entry, value, f.Import.Config);
                Throws<SafetyException>(() => f.Run(request)); Profiles.ForgetOffline(Profiles.Catalog(f.Import.Config).Single(), f.Import.Config); Throws<SafetyException>(() => f.Run(request));
                Check(f.Process.Probes == 0 && ImportHistory.Load(f.Import.Config).Single().Id == f.Batch.Id, "Opt-out/forget started work or deleted history"); f.Unchanged();
            });
            Test("modern setup change during prompt refuses before process work", delegate {
                var f = new ModernSharingFixture(); var request = f.Review().Confirm(); var setup = QuickShareConfiguration.Load(f.Import.Config); var selected = setup.Value;
                QuickShareConfiguration.Save(setup, selected.Ffmpeg.Path, selected.Ffprobe.Path, null, f.Import.Config, CancellationToken.None);
                Throws<SafetyException>(() => f.Run(request)); Check(f.Process.Probes == 0, "Stale tools admitted export"); f.Unchanged();
            });
            Test("modern output probe refusal publishes nothing and fresh manual action retries", delegate {
                var f = new ModernSharingFixture(); f.Process.BadOutput = true; var failure = Throws<QuickShareFailure>(() => f.Run(f.Review().Confirm()));
                Check(failure.Partial.PublishedCopies == 0 && failure.Partial.PublishedReceipts == 0 && f.Process.Decodes == 0, "Invalid output published/decoded");
                f.Process.BadOutput = false; Check(f.Run(f.Review().Confirm()).Created == 1, "Fresh retry failed"); f.Unchanged();
            });
            Test("modern cancellation after encode keeps originals and permits only fresh manual retry", delegate {
                var f = new ModernSharingFixture(); using (var stop = new CancellationTokenSource()) {
                    f.Process.Stage = stage => { if (stage == "encoded") stop.Cancel(); }; var failure = Throws<QuickShareFailure>(() => f.Run(f.Review().Confirm(), stop.Token));
                    Check(failure.Cancelled && failure.Partial.PublishedCopies == 0 && failure.Partial.PublishedReceipts == 0 && f.Process.Decodes == 0, "Cancelled output published");
                }
                f.Process.Stage = stage => {}; f.Unchanged(); Check(f.Run(f.Review().Confirm()).Created == 1, "Fresh cancellation retry failed"); f.Unchanged();
            });
            Test("modern full-decode failure refuses publication and retains originals", delegate {
                var f = new ModernSharingFixture(); f.Process.Stage = stage => { if (stage == "decode") throw new SafetyException("Synthetic full decode failure"); };
                var failure = Throws<QuickShareFailure>(() => f.Run(f.Review().Confirm()));
                Check(f.Process.Probes == 2 && f.Process.Decodes == 1 && failure.Partial.PublishedCopies == 0 && failure.Partial.PublishedReceipts == 0, "Decode failure was bypassed"); f.Unchanged();
            });
            Test("modern I-Log choice requires supplied official LUT and never executes missing setup", delegate {
                var f = new ModernSharingFixture(); var review = f.Review(); review.Set(QuickSharePreset.Uhd, QuickShareColor.LunaILog);
                Throws<SafetyException>(() => f.Run(review.Confirm())); Check(f.Process.Probes == 0 && !Directory.Exists(Path.Combine(f.Batch.Folder, "Sharing Copies")), "Missing LUT invoked export"); f.Unchanged();
            });
            Test("modern saved export never restores expired process completion removal authority", delegate {
                var f = new ModernSharingFixture(); var registry = new ImportCompletionRegistry();
                var entry = new ImportCompletion(new ImportCompletionOrigin(f.Import.Discovery.State, f.Batch.ProfileId, f.Import.Source), f.Batch.DeviceName, f.Batch.Folder, f.Batch.DestinationRoot, f.Batch.Destination, true, "fixture verified", f.Batch); registry.Insert(entry);
                registry.InvalidateNotifications(); var result = f.Run(f.Review().Confirm());
                Check(result.Created == 1 && registry.Get(entry.Id) == null && !QuickSharePresentation.ReturnToCompletion(registry, entry, true) && ImportHistory.Load(f.Import.Config).Single().Id == f.Batch.Id, "Saved export restored eject authority/deleted history"); f.Unchanged();
            });
            Test("modern changed destination refuses before any sharing process", delegate {
                var f = new ModernSharingFixture(); var request = f.Review().Confirm(); f.Import.Discovery.State.Volumes.Last().Serial = "replacement";
                Throws<SafetyException>(() => f.Run(request)); Check(f.Process.Probes == 0 && f.Import.Operations.Available(new[] { 7, 8 }), "Changed destination admitted/leaked");
            });
            Test("modern retained history eviction rejects an old pending export without scanning", delegate {
                var f = new ModernSharingFixture(); var old = f.Batch; var request = f.Review().Confirm(); for (int i = 0; i < 20; i++) f.Record();
                Check(ImportHistory.Load(f.Import.Config).Count == 20 && !ImportHistory.Load(f.Import.Config).Any(b => b.Id == old.Id), "Last-20 bound failed");
                Throws<SafetyException>(() => f.Run(request)); Check(f.Process.Probes == 0 && File.Exists(old.ReceiptPath), "Evicted choice ran/deleted old receipt"); f.Unchanged();
            });
            Test("modern supplied tool save supports pre-cancel stale refusal and clear without execution", delegate {
                var f = new ModernSharingFixture(); var setup = QuickShareConfiguration.Load(f.Import.Config); using (var stop = new CancellationTokenSource()) {
                    stop.Cancel(); Throws<OperationCanceledException>(() => QuickShareConfiguration.Save(setup, "", "", "", f.Import.Config, stop.Token));
                }
                Check(QuickShareConfiguration.Load(f.Import.Config).Hash == setup.Hash, "Pre-cancel changed setup");
                QuickShareConfiguration.Save(setup, "", "", "", f.Import.Config, CancellationToken.None);
                Throws<SafetyException>(() => QuickShareConfiguration.Save(setup, "", "", "", f.Import.Config, CancellationToken.None));
                Check(QuickShareConfiguration.Load(f.Import.Config).Value.Ffmpeg == null && f.Process.Probes == 0 && File.Exists(setup.Value.Ffmpeg.Path), "Clear removed supplied file or executed tools"); f.Unchanged();
            });
        }
    }
}
