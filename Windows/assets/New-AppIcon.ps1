$ErrorActionPreference = 'Stop'
# Windows rendering of the existing Ejector/AppIcon.icon artwork. No downloads.
Add-Type -AssemblyName System.Drawing
$drawingReferences = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.PointF].Assembly.Location) | Select-Object -Unique
if ($PSVersionTable.PSEdition -eq 'Core') {
    $drawingReferences += [Reflection.Assembly]::Load('System.Private.Windows.GdiPlus').Location
    $drawingReferences += [Reflection.Assembly]::Load('System.Private.Windows.Core').Location
}
Add-Type -ReferencedAssemblies $drawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class EasyEjectIconArtwork {
    static GraphicsPath Round(float x, float y, float w, float h, float r) {
        var p = new GraphicsPath(); float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90); p.AddArc(x+w-d, y, d, d, 270, 90);
        p.AddArc(x+w-d, y+h-d, d, d, 0, 90); p.AddArc(x, y+h-d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    static void Quad(GraphicsPath p, PointF a, PointF b, PointF c) {
        p.AddBezier(a, new PointF(a.X+(b.X-a.X)*2/3, a.Y+(b.Y-a.Y)*2/3),
            new PointF(c.X+(b.X-c.X)*2/3, c.Y+(b.Y-c.Y)*2/3), c);
    }
    static Bitmap Render(int size) {
        using (var hi = new Bitmap(size*4, size*4, PixelFormat.Format32bppArgb)) {
            using (var g = Graphics.FromImage(hi)) {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size*4/1024f, size*4/1024f);
                using (var p = Round(40,40,944,944,210))
                using (var fill = new LinearGradientBrush(new PointF(0,0),new PointF(0,1024),Color.FromArgb(231,79,51),Color.FromArgb(175,34,20))) g.FillPath(fill,p);
                // Exact white glyph coordinates from Eject.svg; quadratic curves converted to cubic.
                using (var p = new GraphicsPath()) {
                    Quad(p,new PointF(483,205),new PointF(512,168),new PointF(541,205));
                    p.AddLine(541,205,803,532); Quad(p,new PointF(803,532),new PointF(829,568),new PointF(786,568));
                    p.AddLine(786,568,238,568); Quad(p,new PointF(238,568),new PointF(195,568),new PointF(221,532));
                    p.AddLine(221,532,483,205); p.CloseFigure(); g.FillPath(Brushes.White,p);
                }
                using (var p = Round(216,632,592,126,32)) g.FillPath(Brushes.White,p);
            }
            var result = new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g = Graphics.FromImage(result)) { g.CompositingMode=CompositingMode.SourceCopy; g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.DrawImage(hi,0,0,size,size); }
            return result;
        }
    }
    public static void Write(string directory) {
        int[] sizes = {16,20,24,32,40,48,64,128,256}; var images = new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(var bitmap=Render(sizes[i])) using(var stream=new MemoryStream()) { bitmap.Save(stream,ImageFormat.Png); images[i]=stream.ToArray(); }
        using(var stream=File.Create(Path.Combine(directory,"EasyEject.ico"))) using(var writer=new BinaryWriter(stream)) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) { writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset+=images[i].Length; }
            foreach(var bytes in images) writer.Write(bytes);
        }
        using(var bitmap=Render(256)) bitmap.Save(Path.Combine(directory,"EasyEject.png"),ImageFormat.Png);
    }
}
'@
[EasyEjectIconArtwork]::Write($PSScriptRoot)
