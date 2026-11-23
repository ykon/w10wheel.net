module MenuStyle

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Drawing
open System.Drawing.Drawing2D
open System.Runtime.InteropServices
open System.Windows.Forms

// The tray menu drawn as the menus of Windows 11: WinForms draws its ContextMenuStrip itself (the
// look of Office 2003), which the visual styles of .NET 11 do not change, and the native menus of
// Windows (ContextMenu) are not in .NET any more. Rounded corners (DWM, Windows 11 only), a plain
// background, a rounded highlight inset in the menu, thin separators and taller items. In high
// contrast the menu is left as WinForms draws it (the colors of the theme).

[<DllImport("dwmapi.dll")>]
extern int DwmSetWindowAttribute(nativeint hwnd, int attribute, int& value, int size)

let private DWMWA_WINDOW_CORNER_PREFERENCE = 33
// The small radius of the auxiliary UI (menus, tooltips)
let private DWMWCP_ROUNDSMALL = 3

// Windows 11 rounds the corners of a window that asks (DWMWA_WINDOW_CORNER_PREFERENCE, build 22000)
let private canRoundCorners = Environment.OSVersion.Version.Build >= 22000

let private roundCorners (c: Control) =
    if canRoundCorners then
        let mutable pref = DWMWCP_ROUNDSMALL
        DwmSetWindowAttribute(c.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, &pref, sizeof<int>) |> ignore

// The font of the check mark (the glyph CheckMark): Segoe Fluent Icons (Windows 11), Segoe MDL2
// Assets (Windows 10), or none (the default check)
let private glyphFont =
    lazy (["Segoe Fluent Icons"; "Segoe MDL2 Assets"]
          |> List.tryFind (fun name -> FontFamily.Families |> Array.exists (fun f -> f.Name = name)))

[<Literal>]
let private CHECK_MARK = "\uE73E"

let private roundedRect (r: Rectangle) (radius: int) =
    let d = radius * 2
    let path = new GraphicsPath()
    path.AddArc(r.X, r.Y, d, d, 180.0f, 90.0f)
    path.AddArc(r.Right - d, r.Y, d, d, 270.0f, 90.0f)
    path.AddArc(r.Right - d, r.Bottom - d, d, d, 0.0f, 90.0f)
    path.AddArc(r.X, r.Bottom - d, d, d, 90.0f, 90.0f)
    path.CloseFigure()
    path

type private Windows11Renderer() =
    inherit ToolStripRenderer()

    // The colors of the menus of Windows 11, light or dark as the dialogs (Program.main: the mode
    // of Windows at startup)
    let dark () = Application.IsDarkModeEnabled
    let background () = if dark() then Color.FromArgb(44, 44, 44) else Color.FromArgb(249, 249, 249)
    let highlight () = if dark() then Color.FromArgb(61, 61, 61) else Color.FromArgb(233, 233, 233)
    let line () = if dark() then Color.FromArgb(64, 64, 64) else Color.FromArgb(224, 224, 224)
    let foreground () = if dark() then Color.White else Color.Black

    // In the units of the display of the menu (at 96 DPI: n)
    let scaled (ts: ToolStrip) (n: int) = ts.LogicalToDeviceUnits(n)

    override _.OnRenderToolStripBackground(e) =
        use b = new SolidBrush(background())
        e.Graphics.FillRectangle(b, e.AffectedBounds)

    // With rounded corners, Windows draws the border (rounded too)
    override _.OnRenderToolStripBorder(e) =
        if not canRoundCorners then
            use p = new Pen(line())
            let r = e.AffectedBounds
            e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1)

    // The highlight inset in the menu, rounded
    override _.OnRenderMenuItemBackground(e) =
        if e.Item.Selected && e.Item.Enabled then
            let s = scaled e.ToolStrip
            let r = Rectangle(s 4, 0, e.Item.Width - s 8, e.Item.Height)
            let g = e.Graphics
            let mode = g.SmoothingMode
            g.SmoothingMode <- SmoothingMode.AntiAlias
            use path = roundedRect r (s 4)
            use b = new SolidBrush(highlight())
            g.FillPath(b, path)
            g.SmoothingMode <- mode

    override _.OnRenderItemText(e) =
        e.TextColor <- if e.Item.Enabled then foreground() else SystemColors.GrayText
        base.OnRenderItemText(e)

    override _.OnRenderSeparator(e) =
        let s = scaled e.ToolStrip
        let y = e.Item.Height / 2
        use p = new Pen(line())
        e.Graphics.DrawLine(p, s 12, y, e.Item.Width - s 12, y)

    // The check mark alone (no box), as Windows 11 draws it: centered in the height of the item
    // (its rectangle is at the top of a taller item) and in the inset highlight
    override _.OnRenderItemCheck(e) =
        match glyphFont.Value with
        | Some name ->
            use font = new Font(name, float32 e.Item.Font.Height * 0.8f, GraphicsUnit.Pixel)
            let color = if e.Item.Enabled then foreground() else SystemColors.GrayText
            let r = e.ImageRectangle
            let box = Rectangle(r.X + scaled e.ToolStrip 4, (e.Item.Height - r.Height) / 2, r.Width, r.Height)
            TextRenderer.DrawText(e.Graphics, CHECK_MARK, font, box, color,
                                  TextFormatFlags.HorizontalCenter ||| TextFormatFlags.VerticalCenter ||| TextFormatFlags.NoPadding)
        | None -> base.OnRenderItemCheck(e)

// The spacing of the menus of Windows 11, in the units of the display (the menu can open on a
// display of another DPI than before)
let private setSpacing (menu: ContextMenuStrip) =
    let s (n: int) = menu.LogicalToDeviceUnits(n)
    // Taller items, as Windows 11 lays them out. Not wider: the menu ignores the horizontal padding
    // of its items, and a minimum width would not widen them (nor their highlight).
    for item in menu.Items do
        item.Margin <- Padding.Empty
        match item with
        | :? ToolStripSeparator -> ()
        | _ -> item.Padding <- Padding(0, s 2, 0, s 2)
    // A space above the first item and below the last (the highlight not against the rounded
    // border): by their margins, the menu sets its own padding back (2 at the top and bottom)
    let shown = menu.Items |> Seq.cast<ToolStripItem> |> Seq.filter (fun i -> i.Available) |> List.ofSeq
    match shown with
    | [] -> ()
    | first :: _ ->
        let last = List.last shown
        first.Margin <- Padding(0, s 4, 0, first.Margin.Bottom)
        last.Margin <- Padding(0, last.Margin.Top, 0, s 4)

// Draws menu as the menus of Windows 11 (not in high contrast)
let apply (menu: ContextMenuStrip) =
    if not SystemInformation.HighContrast then
        menu.Renderer <- new Windows11Renderer()
        // The check mark in a margin of its own, no margin of images (there are none)
        menu.ShowImageMargin <- false
        menu.ShowCheckMargin <- true
        menu.Opening.Add(fun _ -> setSpacing menu)
        menu.HandleCreated.Add(fun _ -> roundCorners menu)
