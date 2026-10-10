using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace EasyEject {
    public static partial class Tests {
        const string DjiMasterA = "DJI_20261009120000_0001_D.MP4", DjiMasterB = "DJI_20261009120500_0002_D.MP4";
        static QuickShareProbe DjiProbe(string encoder = "DJI O4P", string comment = null, string duration = "5.0") {
            string tags = encoder == null && comment == null ? "" : ",\"tags\":{" + String.Join(",", new[] { encoder == null ? null : "\"encoder\":\"" + encoder + "\"", comment == null ? null : "\"comment\":\"" + comment.Replace("\n", "\\n") + "\"" }.Where(x => x != null)) + "}";
            return QuickShareProbe.Parse(Encoding.UTF8.GetBytes("{\"streams\":[{\"codec_type\":\"video\",\"codec_name\":\"hevc\",\"width\":3840,\"height\":2160,\"r_frame_rate\":\"60/1\",\"duration\":\"" + duration + "\",\"color_transfer\":\"bt709\"},{\"codec_type\":\"audio\",\"codec_name\":\"aac\",\"duration\":\"" + duration + "\",\"channels\":2}],\"format\":{\"duration\":\"" + duration + "\"" + tags + "}}"));
        }
        static CompletedBatch DjiBatch(Fixture fixture, params string[] names) {
            foreach (string name in names.Length == 0 ? new[] { DjiMasterA, DjiMasterB } : names) fixture.Put(name, "synthetic DJI O4 Pro master " + name);
            return Batch(fixture, fixture.Run(_ => {}));
        }
        static string DjiOriginal(CompletedBatch batch, string name) { return batch.Originals.Single(x => Path.GetFileName(x.Path).Equals(name, StringComparison.OrdinalIgnoreCase)).Path; }
        static string GyroflowExport(CompletedBatch batch, string name, string text = null) {
            string path = QuickShareMedia.GyroflowExportPath(DjiOriginal(batch, name)); File.WriteAllText(path, text ?? "synthetic Gyroflow export of " + name); return path;
        }
        // Masters report the DJI O4P encoder; exports report the Gyroflow comment when given.
        static FakeShareProcess DjiProcess(string exportComment = null, string masterEncoder = "DJI O4P", string masterDuration = "5.0") {
            return new FakeShareProcess { InputFor = path => Path.GetFileNameWithoutExtension(path).EndsWith(QuickShareMedia.GyroflowSuffix, StringComparison.Ordinal) ? DjiProbe(null, exportComment) : DjiProbe(masterEncoder, null, masterDuration) };
        }
        static QuickShareResult RunDji(Fixture fixture, CompletedBatch batch, FakeShareProcess process, QuickShareColor color = QuickShareColor.Standard, QuickShareTools tools = null, Action<string> audit = null, QuickShareCamera camera = QuickShareCamera.DjiO4Pro) {
            return new QuickShareEngine(process, new Operations(), audit, _ => UInt64.MaxValue).Run(batch, HistoryIdentity(), new FakeGuard(), tools ?? FixtureShareTools(fixture), QuickSharePreset.Hd, color, CancellationToken.None, _ => {}, camera);
        }
        static QuickShareReceipt ReadShareReceipt(string output) { using (var stream = SafePaths.ReadFile(QuickShareEngine.ReceiptPath(output))) return (QuickShareReceipt)new DataContractJsonSerializer(typeof(QuickShareReceipt)).ReadObject(stream); }
        static void QuickShareDjiTests() {
            Test("DJI O4 Pro master names and Gyroflow LUT marker are recognized exactly", delegate {
                Check(QuickShareMedia.IsDjiO4ProMasterName(@"C:\x\" + DjiMasterA) && QuickShareMedia.IsDjiO4ProMasterName(@"C:\x\DJI_20261009120000_0001_D.mp4"), "DJI master name refused");
                foreach (string name in new[] { "DJI_20261009120000_0001_D_stabilized.mp4", "DJI_2026_0001_D.mp4", "DJI_20261009120000_0001_D.MOV", "DJI_20261009120000_0001_W.MP4", "VID_20261009120000_0001_D.MP4" }) Check(!QuickShareMedia.IsDjiO4ProMasterName(@"C:\x\" + name), "Unexpected DJI master " + name);
                Check(QuickShareMedia.GyroflowExportPath(@"C:\x\" + DjiMasterA) == @"C:\x\DJI_20261009120000_0001_D_stabilized.mp4", "Gyroflow default export name differs");
                Check(QuickShareMedia.GyroflowAppliedLut(DjiProbe(null, "Gyroflow export LUT applied: DJI D-Log M")) && QuickShareMedia.GyroflowAppliedLut(DjiProbe(null, "Made with Gyroflow\nGyroflow export LUT applied: custom.cube")), "Gyroflow marker missed");
                foreach (string comment in new[] { "Gyroflow export LUT applied: ", "Gyroflow export LUT applied:   ", "note Gyroflow export LUT applied: x", "gyroflow export lut applied: x" }) Check(!QuickShareMedia.GyroflowAppliedLut(DjiProbe(null, comment)), "Marker inferred from " + comment);
                Check(!QuickShareMedia.GyroflowAppliedLut(DjiProbe(null, null)) && !QuickShareMedia.GyroflowAppliedLut(ShareInput()), "Marker inferred without a comment");
            });
            Test("DJI O4 Pro profile choices validate camera and LUT pairing and keep legacy profile bytes", delegate {
                var legacy = new ImportProfile { VolumeId = "v", SharingPreset = QuickShareProfile.Hd, SharingColor = QuickShareProfile.LunaILog };
                Check(QuickShareProfile.Camera(legacy) == QuickShareCamera.Luna && QuickShareProfile.Color(legacy) == QuickShareColor.LunaILog && !QuickShareProfile.GyroflowFirst(legacy), "Legacy profile changed meaning");
                Check(!ProfileJson(legacy).Contains("SharingCamera"), "Absent camera is now serialized");
                var dji = new ImportProfile { VolumeId = "v", SharingPreset = QuickShareProfile.Uhd, SharingCamera = QuickShareProfile.CameraValue(QuickShareCamera.DjiO4Pro), SharingColor = QuickShareProfile.ColorValue(QuickShareCamera.DjiO4Pro, true) };
                Check(QuickShareProfile.Camera(dji) == QuickShareCamera.DjiO4Pro && QuickShareProfile.Color(dji) == QuickShareColor.DjiO4DLogM && QuickShareProfile.GyroflowFirst(dji) && ProfileJson(dji).Contains("\"SharingCamera\":\"dji-o4-pro\""), "DJI choice not stored");
                dji.SharingColor = null; Check(QuickShareProfile.Color(dji) == QuickShareColor.DjiO4DLogM, "Missing DJI color did not prefer its LUT");
                dji.SharingPreset = null; Check(!QuickShareProfile.GyroflowFirst(dji), "Disabled DJI profile offers Gyroflow flow");
                foreach (var invalid in new[] { new ImportProfile { SharingCamera = "dji-o4-pro", SharingColor = QuickShareProfile.LunaILog }, new ImportProfile { SharingColor = QuickShareProfile.DjiO4DLogM }, new ImportProfile { SharingCamera = "luna", SharingColor = QuickShareProfile.DjiO4DLogM }, new ImportProfile { SharingCamera = "DJI-O4-PRO" }, new ImportProfile { SharingCamera = "" } })
                    Throws<SafetyException>(() => QuickShareProfile.Validate(invalid));
                Check(QuickShareProfile.CameraValue(QuickShareCamera.Luna) == null && QuickShareProfile.ColorValue(QuickShareCamera.Luna, false) == QuickShareProfile.Standard && QuickShareProfile.ColorValue(QuickShareCamera.Luna, true) == QuickShareProfile.LunaILog, "Luna values changed");
            });
            Test("DJI O4 Pro LUT must match the camera in plans requests and the engine", delegate {
                Throws<SafetyException>(() => new QuickSharePlan(ShareInput(), QuickSharePreset.Hd, QuickShareColor.DjiO4DLogM));
                Throws<SafetyException>(() => new QuickSharePlan(ShareInput(), QuickSharePreset.Hd, QuickShareColor.LunaILog, QuickShareCamera.DjiO4Pro));
                var plan = new QuickSharePlan(ShareInput(), QuickSharePreset.Hd, QuickShareColor.DjiO4DLogM, QuickShareCamera.DjiO4Pro); string filters = ShareOption(plan.EncodeArguments(@"C:\x\a.mp4", @"C:\x\b.mp4"), "-vf");
                Check(plan.RecipeName == QuickSharePlan.DjiRecipe && plan.LutSha == QuickShareMedia.DjiLutSha && filters.Contains("lut3d=file=camera.cube:interp=tetrahedral") && !filters.Contains("luna.cube"), "DJI plan recipe or LUT differs");
                var luna = new QuickSharePlan(ShareInput(), QuickSharePreset.Hd, QuickShareColor.LunaILog); Check(luna.RecipeName == QuickSharePlan.Recipe && ShareOption(luna.EncodeArguments(@"C:\x\a.mp4", @"C:\x\b.mp4"), "-vf").Contains("lut3d=file=luna.cube"), "Luna recipe changed");
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f); GyroflowExport(batch, DjiMasterA); GyroflowExport(batch, DjiMasterB); var process = DjiProcess();
                    Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process, QuickShareColor.LunaILog)).Message.Contains("does not match this device's camera"), "Luna LUT accepted for DJI");
                    Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process, QuickShareColor.DjiO4DLogM, camera: QuickShareCamera.Luna)).Message.Contains("does not match this device's camera"), "DJI LUT accepted for Luna");
                    Check(process.Probes == 0 && process.Encodes == 0, "Mismatched LUT reached media work");
                    Throws<SafetyException>(() => new ManualQuickShareRequest(batch, new QuickShareConfigurationSnapshot(), QuickSharePreset.Hd, QuickShareColor.LunaILog, QuickShareCamera.DjiO4Pro));
                }
            });
            Test("DJI O4 Pro Quick Share publishes only held Gyroflow exports bound to verified originals", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f); string a = GyroflowExport(batch, DjiMasterA), b = GyroflowExport(batch, DjiMasterB); byte[] exportA = File.ReadAllBytes(a);
                    var process = DjiProcess(); var result = RunDji(f, batch, process);
                    Check(result.Created == 2 && result.Reused == 0 && result.Skipped == 0 && process.Encodes == 2 && process.Decodes == 2 && process.Probes == 6, "DJI export stages differ");
                    Check(process.Inputs.OrderBy(x => x).SequenceEqual(new[] { a, b }.OrderBy(x => x), StringComparer.OrdinalIgnoreCase) && !process.Inputs.Any(x => batch.Originals.Any(o => o.Path.Equals(x, StringComparison.OrdinalIgnoreCase))), "Encoded something other than the Gyroflow exports");
                    string output = ShareFile(batch, "DJI_20261009120000_0001_D_stabilized"); var receipt = ReadShareReceipt(output);
                    Check(File.Exists(output) && receipt.Recipe == QuickSharePlan.DjiRecipe && receipt.SourceSha256 == ImportHistory.Hash(new MemoryStream(exportA), CancellationToken.None) && receipt.LutSha256 == null && receipt.Color == "Standard", "DJI receipt is not bound to the export");
                    Check(File.ReadAllText(result.AuditPath).Contains("DJI O4 Pro Gyroflow exports"), "Audit omits the DJI flow"); VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                    Check(File.ReadAllBytes(a).SequenceEqual(exportA), "Gyroflow export changed");
                    var again = DjiProcess(); var reuse = RunDji(f, batch, again); Check(reuse.Reused == 2 && reuse.Created == 0 && again.Encodes == 0, "Matching DJI copies not reused");
                    var luna = new FakeShareProcess(); Check(RunShare(f, batch, luna).Created == 0 && luna.Encodes == 0, "Luna camera shared DJI files");
                }
            });
            Test("DJI O4 Pro Quick Share refuses missing Gyroflow exports before any probe or encode", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f); GyroflowExport(batch, DjiMasterA); var process = DjiProcess();
                    string pending = QuickSharePresentation.GyroflowPending(batch); Check(pending != null && pending.Contains(DjiMasterB) && !pending.Contains(DjiMasterA), "Pending list differs");
                    var failure = Throws<QuickShareFailure>(() => RunDji(f, batch, process));
                    Check(failure.Message.Contains("Finish these O4 Pro videos in Gyroflow first: " + DjiMasterB) && failure.Partial.PublishedCopies == 0 && process.Probes == 0 && process.Encodes == 0 && !Directory.Exists(Path.Combine(batch.Folder, "Sharing Copies")), "Partial Gyroflow set was shared");
                    GyroflowExport(batch, DjiMasterB); Check(QuickSharePresentation.GyroflowPending(batch) == null && RunDji(f, batch, DjiProcess()).Created == 2, "Retry after export failed"); VerifyShareOriginals(batch);
                }
                using (var f = new Fixture()) {
                    var batch = LunaBatch(f); var process = DjiProcess();
                    Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process)).Message.Contains("Stabilize the O4 Pro originals in Gyroflow") && QuickSharePresentation.GyroflowPending(batch) != null && process.Probes == 0, "Batch without O4 Pro originals was offered");
                }
            });
            Test("DJI O4 Pro Quick Share refuses non-O4P originals and mismatched export durations", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f, DjiMasterA); GyroflowExport(batch, DjiMasterA);
                    foreach (var process in new[] { DjiProcess(masterEncoder: "DJI Mini"), DjiProcess(masterEncoder: null) }) {
                        Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process)).Message.Contains("matching a verified DJI O4 Pro original") && process.Encodes == 0, "Non-O4P original accepted");
                    }
                    var longer = DjiProcess(masterDuration: "5.6"); Check(Throws<QuickShareFailure>(() => RunDji(f, batch, longer)).Message.Contains("does not match") && longer.Encodes == 0, "Mismatched export accepted");
                    Check(RunDji(f, batch, DjiProcess(masterDuration: "5.4")).Created == 1, "Gyroflow trim tolerance changed");
                    VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("DJI O4 Pro Gyroflow LUT marker keeps existing colors and needs no LUT", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f, DjiMasterA); GyroflowExport(batch, DjiMasterA); var process = DjiProcess("Gyroflow export LUT applied: DJI O4 D-Log M");
                    var result = RunDji(f, batch, process, QuickShareColor.DjiO4DLogM); var receipt = ReadShareReceipt(result.Outputs.Single());
                    Check(result.Created == 1 && !ShareOption(process.LastEncode, "-vf").Contains("lut3d") && receipt.Color == "Standard" && receipt.LutSha256 == null && File.ReadAllText(result.AuditPath).Contains("keeps the LUT already applied by Gyroflow"), "Gyroflow LUT applied twice");
                }
            });
            Test("DJI O4 Pro LUT is required only when needed and must be the official file", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f, DjiMasterA); GyroflowExport(batch, DjiMasterA); var process = DjiProcess();
                    Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process, QuickShareColor.DjiO4DLogM)).Message.Contains("Choose the official DJI O4 Air Unit Series D-Log M LUT") && process.Encodes == 0, "Missing DJI LUT accepted");
                    var tools = FixtureShareTools(f); string fake = Path.Combine(f.Root, "fake-dji.cube"); File.WriteAllText(fake, "LUT_3D_SIZE 2\n0 0 0\n");
                    var withFake = new QuickShareTools(tools.Ffmpeg, tools.Ffprobe, null, fake);
                    Check(Throws<QuickShareFailure>(() => RunDji(f, batch, process, QuickShareColor.DjiO4DLogM, withFake)).Message.Contains("official DJI O4 Air Unit Series") && process.Encodes == 0, "Unofficial DJI LUT accepted");
                    Throws<SafetyException>(() => QuickShareMedia.ReadLut(fake, QuickShareColor.DjiO4DLogM, CancellationToken.None));
                    Throws<SafetyException>(() => QuickShareMedia.ReadLut(fake, QuickShareColor.Standard, CancellationToken.None));
                    VerifyShareOriginals(batch); CheckSharePrivateGone(batch);
                }
            });
            Test("DJI O4 Pro Gyroflow exports stay held and a changed re-export gets a separate copy", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f, DjiMasterA); string export = GyroflowExport(batch, DjiMasterA); int blocked = 0; var process = DjiProcess();
                    process.OnStage = (stage, path) => { if (stage == "Before encode") { Throws<IOException>(() => File.WriteAllText(export, "changed during export")); blocked++; } };
                    var first = RunDji(f, batch, process); Check(first.Created == 1 && blocked == 1, "Export was not held during encoding"); byte[] former = File.ReadAllBytes(first.Outputs.Single());
                    File.WriteAllText(export, "a different Gyroflow re-export"); var second = RunDji(f, batch, DjiProcess());
                    Check(second.Created == 1 && second.Reused == 0 && second.Outputs.Single() != first.Outputs.Single() && File.ReadAllBytes(first.Outputs.Single()).SequenceEqual(former), "Changed export reused or replaced the earlier copy");
                    VerifyShareOriginals(batch);
                }
            });
            Test("DJI O4 Pro manual sharing keeps the profile camera and refuses a changed camera", delegate {
                using (var f = new Fixture()) {
                    var batch = DjiBatch(f, DjiMasterA); GyroflowExport(batch, DjiMasterA); string root = ManualShareSetup(f, batch);
                    var entry = Profiles.Catalog(root).Single(); var profile = entry.Profile; profile.SharingCamera = QuickShareProfile.DjiO4ProCamera; profile.SharingColor = QuickShareProfile.Standard; Profiles.SaveOffline(entry, profile, root);
                    var process = DjiProcess(); var service = ManualShares(root, process);
                    Check(service.GyroflowFirst(batch) && service.Prepare(batch).Camera == QuickShareCamera.DjiO4Pro, "DJI profile not offered as Gyroflow-first");
                    var request = service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard); Check(request.Camera == QuickShareCamera.DjiO4Pro, "Request lost camera");
                    Throws<SafetyException>(() => service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.LunaILog));
                    entry = Profiles.Catalog(root).Single(); profile = entry.Profile; profile.SharingCamera = null; Profiles.SaveOffline(entry, profile, root);
                    Check(Throws<SafetyException>(() => service.Run(request, CancellationToken.None, _ => {})).Message.Contains("camera changed") && process.Probes == 0, "Changed camera reached media work");
                    entry = Profiles.Catalog(root).Single(); profile = entry.Profile; profile.SharingCamera = QuickShareProfile.DjiO4ProCamera; Profiles.SaveOffline(entry, profile, root);
                    var result = service.Run(service.Prepare(batch).Choose(QuickSharePreset.Hd, QuickShareColor.Standard), CancellationToken.None, _ => {});
                    Check(result.Created == 1 && QuickSharePresentation.Success(batch, QuickSharePreset.Hd, result, QuickShareCamera.DjiO4Pro).Contains("1 1080p sharing copies ready"), "Manual DJI share failed");
                    Check(QuickSharePresentation.Success(batch, QuickSharePreset.Hd, new QuickShareResult { Folder = "x" }, QuickShareCamera.DjiO4Pro).Contains("no supported DJI O4 Pro master videos"), "DJI empty result names Luna");
                }
            });
            Test("DJI O4 Pro LUT setup is optional, validated and keeps earlier setup bytes", delegate {
                using (var f = new Fixture()) {
                    var tools = FixtureShareTools(f); var saved = QuickShareConfiguration.Save(QuickShareConfiguration.Load(f.Destination), tools.Ffmpeg, tools.Ffprobe, null, f.Destination, CancellationToken.None);
                    Check(saved.Value.DjiLut == null && !File.ReadAllText(Path.Combine(f.Destination, QuickShareConfiguration.FileName)).Contains("DjiLut"), "Absent DJI LUT changed setup record");
                    string fake = Path.Combine(f.Root, "fake-dji.cube"); File.WriteAllText(fake, "LUT_3D_SIZE 2\n");
                    Throws<SafetyException>(() => QuickShareConfiguration.Save(saved, tools.Ffmpeg, tools.Ffprobe, null, f.Destination, CancellationToken.None, null, fake));
                    Check(QuickShareConfiguration.Load(f.Destination).Hash == saved.Hash && !File.Exists(QuickShareConfiguration.LutPath(f.Destination, QuickShareColor.DjiO4DLogM)), "Refused DJI LUT changed setup");
                    var forged = saved.Value; forged.DjiLut = new QuickShareToolFile { Path = QuickShareConfiguration.LutPath(f.Destination), Sha256 = QuickShareMedia.OfficialLutSha, Bytes = 10 };
                    Throws<SafetyException>(() => forged.Validate(f.Destination));
                    Check(QuickShareConfiguration.LutPath(f.Destination, QuickShareColor.DjiO4DLogM).EndsWith("dji-o4-lut-" + QuickShareMedia.DjiLutSha + ".cube") && QuickShareConfiguration.LutPath(f.Destination) == QuickShareConfiguration.LutPath(f.Destination, QuickShareColor.LunaILog), "LUT paths differ");
                }
            });
        }
        static string ProfileJson(ImportProfile value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(ImportProfile)).WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); } }
    }
}
