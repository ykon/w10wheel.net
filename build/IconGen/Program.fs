// Draws the icons of W10Wheel: a flat circle with a white W, gray (running) or red (Pass Mode),
// at the sizes of the tray icons for each DPI scale (16 at 100% ... 48 at 300%), and the program
// icon up to 256 (PNG). Run from the root of the repository:
//
//   dotnet run --project build/IconGen -- W10Wheel/icon
//
// then compile the resource of the program icon again (the Windows SDK):
//
//   cd W10Wheel/icon
//   rc /nologo /fo AppIcon.res AppIcon.rc

open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Drawing.Imaging

let GRAY = Color.FromArgb(78, 78, 82)
let RED = Color.FromArgb(226, 55, 60)

let TRAY_SIZES = [16; 20; 24; 28; 32; 36; 40; 48]
let APP_SIZES = [16; 20; 24; 32; 40; 48; 64; 128; 256]

let circlePath (s: float32) =
    let p = new GraphicsPath()
    // Almost the whole square, unlike the old icons (smaller than the others in the tray)
    let m = max 0.5f (s * 0.03f)
    p.AddEllipse(RectangleF(m, m, s - 2.0f * m, s - 2.0f * m))
    p

let wPath (s: float32) =
    let p = new GraphicsPath()
    use family = new FontFamily("Segoe UI")
    p.AddString("W", family, int FontStyle.Bold, 100.0f, PointF(0.0f, 0.0f), StringFormat.GenericTypographic)
    let b = p.GetBounds()
    let scale = s * 0.6f / b.Width
    use m = new Matrix()
    m.Translate((s - b.Width * scale) / 2.0f, (s - b.Height * scale) / 2.0f + s * 0.01f)
    m.Scale(scale, scale)
    m.Translate(-b.X, -b.Y)
    p.Transform(m)
    p

let draw (color: Color) (size: int) =
    let bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb)
    use g = Graphics.FromImage(bmp)
    g.SmoothingMode <- SmoothingMode.AntiAlias
    g.PixelOffsetMode <- PixelOffsetMode.HighQuality
    use circle = circlePath (float32 size)
    use brush = new SolidBrush(color)
    g.FillPath(brush, circle)
    use w = wPath (float32 size)
    g.FillPath(Brushes.White, w)
    bmp

// An image of an .ico: a 32-bit DIB with alpha and its AND mask (1 where transparent), or a PNG at 256
let icoImage (bmp: Bitmap) =
    use ms = new IO.MemoryStream()
    if bmp.Width >= 256 then
        bmp.Save(ms, ImageFormat.Png)
    else
        use bw = new IO.BinaryWriter(ms)
        let w, h = bmp.Width, bmp.Height
        let maskStride = ((w + 31) / 32) * 4
        // BITMAPINFOHEADER: the height of the image and of its mask
        bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write(1s); bw.Write(32s)
        bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0)
        for y in h - 1 .. -1 .. 0 do
            for x in 0 .. w - 1 do
                let c = bmp.GetPixel(x, y)
                bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A)
        for y in h - 1 .. -1 .. 0 do
            let row = Array.zeroCreate<byte> maskStride
            for x in 0 .. w - 1 do
                if bmp.GetPixel(x, y).A = 0uy then
                    row.[x / 8] <- row.[x / 8] ||| (0x80uy >>> (x % 8))
            bw.Write(row)
        bw.Flush()
    ms.ToArray()

let writeIco (path: string) (color: Color) (sizes: int list) =
    let images = sizes |> List.map (draw color)
    let datas = images |> List.map icoImage
    use fs = IO.File.Create(path)
    use bw = new IO.BinaryWriter(fs)
    // ICONDIR, then an ICONDIRENTRY for each image (0 for 256)
    bw.Write(0s); bw.Write(1s); bw.Write(int16 images.Length)
    let mutable offset = 6 + 16 * images.Length
    for (bmp, data) in List.zip images datas do
        let dim (n: int) = if n >= 256 then 0uy else byte n
        bw.Write(dim bmp.Width); bw.Write(dim bmp.Height); bw.Write(0uy); bw.Write(0uy)
        bw.Write(1s); bw.Write(32s); bw.Write(data.Length); bw.Write(offset)
        offset <- offset + data.Length
    for data in datas do bw.Write(data)
    for bmp in images do bmp.Dispose()

[<EntryPoint>]
let main argv =
    let dir = if argv.Length > 0 then argv.[0] else "."
    writeIco (IO.Path.Combine(dir, "TrayIcon-Run.ico")) GRAY TRAY_SIZES
    writeIco (IO.Path.Combine(dir, "TrayIcon-Stop.ico")) RED TRAY_SIZES
    writeIco (IO.Path.Combine(dir, "AppIcon.ico")) GRAY APP_SIZES
    printfn "Written in %s" (IO.Path.GetFullPath dir)
    0
