using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyEject {
    // Immutable options are resolved before an operation; editing defaults cannot alter a running copy.
    public sealed class MediaOptions {
        public readonly bool VideosOnly, IncludePreviews, CleanLayout, AskToEject, DeleteOriginals, RecoverTrash;
        public readonly string MacTrashUid;
        public MediaOptions(bool videosOnly, bool includePreviews, bool cleanLayout = false, bool askToEject = false, bool deleteOriginals = false, bool recoverTrash = false, string macTrashUid = null) { VideosOnly = videosOnly; IncludePreviews = includePreviews; CleanLayout = cleanLayout; AskToEject = askToEject; DeleteOriginals = deleteOriginals; RecoverTrash = recoverTrash; MacTrashUid = macTrashUid; }
    }
    public sealed class MediaPlan {
        public List<string> Media, Indexes;
        public bool Structured;
        public List<string> Files { get { return Media.Concat(Indexes).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(); } }
    }
    public static class MediaSelection {
        static HashSet<string> Set(string words) { return new HashSet<string>(words.Split(' '), StringComparer.OrdinalIgnoreCase); }
        static readonly HashSet<string> videos = Set("mp4 mov m4v insv lrv avi mkv mts m2ts m2t mpg mpeg mxf 360 braw r3d crm");
        static readonly HashSet<string> photos = Set("jpg jpeg gif bmp webp avif jxl gpr heic heif hif png tif tiff dng insp arw srf sr2 cr2 cr3 crw nef nrw orf rw2 raw raf pef srw x3f 3fr fff iiq rwl mos mrw kdc dcr erf mef mdc mpo");
        static readonly HashSet<string> sidecars = Set("srt thm lrf xmp xml rmd bim sidecar gyro bbl bfl gcsv");
        static readonly HashSet<string> folders = Set("AVCHD BDMV BPAV XDROOT M4ROOT PXROOT CINEROOT CRM XFVC CONTENTS VIDEO_TS AUDIO_TS PROXY PROXIES");
        static readonly HashSet<string> structuredExtensions = Set("mts m2ts m2t mxf r3d crm ari arx cif sif mpl cpi");
        static readonly HashSet<string> completeRoots = Set("PRIVATE AVCHD BPAV M4ROOT XDROOT PXROOT CINEROOT CRM XFVC");
        static readonly HashSet<string> reelExtensions = Set("rdc rdm"), proxyFolders = Set("PROXY PROXIES"), previews = Set("lrv lrf thm"), packageIndexes = Set("pro.prj lastclip.txt index.bdm");
        static string Ext(string file) { return Path.GetExtension(file).TrimStart('.'); }
        static string Key(string text) { return text.Normalize(NormalizationForm.FormC).ToLowerInvariant(); }
        static string Stem(string file) { return Key(Path.Combine(Path.GetDirectoryName(file), Path.GetFileNameWithoutExtension(file))); }
        public static bool IsDeviceIndex(string file) { return Path.GetFileName(file).Equals("fileinfo_list.list", StringComparison.OrdinalIgnoreCase); }
        static bool StructuredComponent(string name) { return folders.Contains(name) || reelExtensions.Contains(Ext(name)); }
        static bool HardMarker(string name) { return StructuredComponent(name) && !proxyFolders.Contains(name); }
        static IEnumerable<string> Directories(string file, string root) {
            string[] relative = SafePaths.Relative(file, root).Split('\\');
            return new[] { Path.GetFileName(root.TrimEnd('\\')) }.Concat(relative.Take(relative.Length - 1));
        }
        public static string Association(string file) {
            string key = Key(Path.GetFileNameWithoutExtension(file));
            if (sidecars.Contains(Ext(file)) && videos.Contains(Ext(key))) key = Path.GetFileNameWithoutExtension(key);
            var insta = Regex.Match(key, @"^(?:pro_vid|vid|lrv|img)_([0-9]{8}_[0-9]{6})_(?:[0-9]{2}_)?([0-9]+)$");
            if (insta.Success) return "insta-" + insta.Groups[1].Value.Replace('_', '-') + "-" + insta.Groups[2].Value;
            if (Regex.IsMatch(key, @"^g[hxl][0-9]{6}$")) return "gopro-" + key.Substring(2);
            return key;
        }
        static string MasterKey(string file) { return Key(Path.GetDirectoryName(file)) + "\\" + Association(file); }
        static bool Preview(string file, string root) {
            var dirs = Directories(file, root).ToArray();
            return !dirs.Any(HardMarker) && (previews.Contains(Ext(file)) || dirs.Any(proxyFolders.Contains));
        }
        static List<string> Ordinary(List<string> files, bool videosOnly) {
            var candidates = files.Where(x => !IsDeviceIndex(x)).ToList(); if (!videosOnly) return candidates;
            var photoStems = new HashSet<string>(candidates.Where(x => photos.Contains(Ext(x))).Select(Stem));
            var masters = candidates.Where(x => videos.Contains(Ext(x)) && !Ext(x).Equals("lrv", StringComparison.OrdinalIgnoreCase) && !photoStems.Contains(Stem(x))).ToList();
            var keys = new HashSet<string>(masters.Select(MasterKey));
            var chosen = new HashSet<string>(masters.Concat(candidates.Where(x => Ext(x).Equals("lrv", StringComparison.OrdinalIgnoreCase) && keys.Contains(MasterKey(x)))));
            var videoStems = new HashSet<string>(chosen.Select(Stem)); var videoPaths = new HashSet<string>(chosen.Select(Key));
            var otherStems = new HashSet<string>(candidates.Where(x => !videos.Contains(Ext(x)) && !sidecars.Contains(Ext(x))).Select(Stem));
            return candidates.Where(x => chosen.Contains(x) || (sidecars.Contains(Ext(x)) && (videoPaths.Contains(Stem(x)) || (videoStems.Contains(Stem(x)) && !otherStems.Contains(Stem(x)))))).ToList();
        }
        public static HashSet<string> Recognized(List<string> files) {
            var audio = Set("wav mp3 aac m4a");
            var masters = files.Where(x => (videos.Contains(Ext(x)) || photos.Contains(Ext(x)) || audio.Contains(Ext(x))) && !Ext(x).Equals("lrv", StringComparison.OrdinalIgnoreCase)).ToList();
            var masterKeys = new HashSet<string>(masters.Where(x => videos.Contains(Ext(x))).Select(MasterKey));
            var media = new HashSet<string>(masters.Concat(files.Where(x => Ext(x).Equals("lrv", StringComparison.OrdinalIgnoreCase) && masterKeys.Contains(MasterKey(x)))));
            var keys = new HashSet<string>(media.Select(Stem).Concat(media.Select(Key)));
            return new HashSet<string>(files.Where(x => media.Contains(x) || (sidecars.Contains(Ext(x)) && keys.Contains(Stem(x)))));
        }
        internal static HashSet<string> Deletable(MediaPlan plan, MediaOptions options) {
            return new HashSet<string>((options.VideosOnly ? Ordinary(plan.Media, true) : Recognized(plan.Media).ToList()).Where(x => !IsDeviceIndex(x)), StringComparer.OrdinalIgnoreCase);
        }
        public static MediaPlan Plan(List<string> scanned, string source, string root, MediaOptions options) {
            if (options == null) throw new ArgumentNullException("options");
            var candidates = scanned.Where(x => options.IncludePreviews || !Preview(x, root)).ToList();
            bool structured = candidates.Any(x => Directories(x, root).Any(StructuredComponent) || structuredExtensions.Contains(Ext(x)) || packageIndexes.Contains(Path.GetFileName(x)));
            bool required = Directories(root.TrimEnd('\\') + "\\placeholder", source).Any(HardMarker) || scanned.Any(x => Directories(x, root).Skip(1).Any(HardMarker) || structuredExtensions.Contains(Ext(x)) || packageIndexes.Contains(Path.GetFileName(x)));
            bool complete = !required || root.TrimEnd('\\').Equals(source.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || completeRoots.Contains(Path.GetFileName(root.TrimEnd('\\')));
            bool rootPackage = StructuredComponent(Path.GetFileName(root.TrimEnd('\\'))) || Path.GetFileName(root.TrimEnd('\\')).Equals("PRIVATE", StringComparison.OrdinalIgnoreCase);
            bool supported = !scanned.Any(x => Path.GetFileName(x).Equals("pro.prj", StringComparison.OrdinalIgnoreCase) || (structuredExtensions.Contains(Ext(x)) && !rootPackage && !Directories(x, root).Skip(1).Any(StructuredComponent)));
            if (structured && (!complete || (options.VideosOnly && !supported))) throw new SafetyException("Select the complete camera package or whole card. For unrecognized structured recordings, choose all media and sidecars. Originals are kept.");
            var media = Ordinary(candidates, options.VideosOnly);
            if (options.VideosOnly) {
                var package = candidates.Where(x => rootPackage || Directories(x, root).Skip(1).Any(StructuredComponent)).ToList();
                bool p2 = package.Any(x => Directories(x, root).Any(d => d.Equals("CONTENTS", StringComparison.OrdinalIgnoreCase)));
                var included = new HashSet<string>(media.Concat(package));
                media = candidates.Where(x => !IsDeviceIndex(x) && (included.Contains(x) || (p2 && Path.GetFileName(x).Equals("LASTCLIP.TXT", StringComparison.OrdinalIgnoreCase)))).ToList();
            }
            return new MediaPlan { Media = media, Indexes = scanned.Where(IsDeviceIndex).ToList(), Structured = structured };
        }
    }
}
