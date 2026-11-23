module MessageForm

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Drawing
open System.Windows.Forms

// The dialogs that tell something and ask at most a choice: at startup whether to restart as
// administrator, and About. Forms of their own, not task dialogs: the task dialogs (and the message
// boxes) are drawn by Windows and stay light, while the forms take the color mode of the program
// (Program.main: dark if Windows is at startup), as the settings dialog does. Laid out as the
// message boxes of the Java version (W10Wheel, FlatLaf): an icon on the left, a bold heading and
// the contents on its right, the buttons at the bottom right, all of the same size.

// The default color of the links (pure blue) is hard to read on the dark background: the link
// color of the dark theme of the Java version (FlatLaf) then, else that of Windows
let private linkColor () =
    if Application.IsDarkModeEnabled then Color.FromArgb(0x58, 0x9D, 0xF6) else SystemColors.HotTrack

let private setLinkColors (l: LinkLabel) =
    l.LinkColor <- linkColor ()
    l.ActiveLinkColor <- linkColor ()

// Whether a click on a link opens it: LinkLabel raises LinkClicked for any button, and W10Wheel
// is used with the right or the middle button as a trigger (a right click on a link opened the
// browser). Enter on a focused link comes as the left button.
let private isLinkOpen (e: LinkLabelLinkClickedEventArgs) =
    e.Button = MouseButtons.Left

// A dialog. Sizes at 96 DPI.
type private Spec =
    { title: string
      // The size of the icon on the left, and the icon at a size in pixels (null if it cannot be had)
      iconSize: int
      makeIcon: int -> Icon
      heading: string
      // Under the heading
      headingGap: int
      // The width at which the texts wrap, and the least width of the heading and the contents
      // (the dialog not narrower than it)
      textWidth: int
      minWidth: int
      // Between the contents and the buttons
      buttonsGap: int
      // On the left of the buttons, under the contents (e.g. links)
      footer: Control option
      // Under the heading, from the top (their margins set)
      body: Control list
      // From the left
      buttons: Button list
      // Sets the texts of the body and of the buttons: once, after the layout has been scaled
      // (see SettingsForm.askText)
      texts: unit -> unit
      // At Load and at a new DPI, after the controls have been scaled to it (e.g. an icon on a
      // button, made at the size of its text)
      fit: unit -> unit
      // Before the dialog is shown (e.g. the default button)
      prepare: Form -> unit }

// Sets an image made at the size of the display, disposing the one it replaces (and the last one
// with owner)
let private imageSetter (owner: Control) (get: unit -> Image) (set: Image -> unit) =
    owner.Disposed.Add(fun _ -> let i = get() in if not (isNull i) then i.Dispose())
    fun (make: unit -> Image) ->
        let old = get()
        set (make())
        if not (isNull old) then old.Dispose()

// The text of the default button. In the visual styles of .NET 11 (Program.main) it is drawn in the
// accent color of Windows, with a text that .NET picks by contrast: black on the default blue
// (#0078D4), hard to read, more in dark mode. White then on a dark accent (perceived brightness),
// as the default buttons of the Java version (FlatLaf); left to .NET (Empty) on a light one (e.g.
// yellow: black), and in high contrast, where the buttons are drawn in the classic style, not in
// the accent color (white would be lost on a light theme).
let private defaultButtonText () =
    let modern =
        Application.DefaultVisualStylesMode <> VisualStylesMode.Classic &&
        Application.DefaultVisualStylesMode <> VisualStylesMode.Disabled
    let accent = Application.SystemVisualSettings.AccentColor
    let brightness = (299 * int accent.R + 587 * int accent.G + 114 * int accent.B) / 1000
    if modern && not SystemInformation.HighContrast && brightness < 128 then Color.White else Color.Empty

// A button of the dialogs: the text of the default button (the focused one, else the
// AcceptButton) as defaultButtonText, that of the others in the color of the dialog (Empty: not set)
type private DialogButton() as this =
    inherit Button()

    // GrowAndShrink: GrowOnly kept a size scaled twice with an image (see SettingsPage.AddButton)
    do
        this.AutoSize <- true
        this.AutoSizeMode <- AutoSizeMode.GrowAndShrink

    override this.NotifyDefault (value: bool) =
        base.NotifyDefault(value)
        this.ForeColor <- if value then defaultButtonText () else Color.Empty

// The image of an icon at size pixels (null if none)
let private iconImage (makeIcon: int -> Icon) (size: int): Image =
    match (try makeIcon size with _ -> null) with
    | null -> null
    | ico ->
        use ico = ico
        ico.ToBitmap()

// Shown modal without an owner (W10Wheel has no window of its own: in the taskbar, as the task
// dialogs were) on the display of the cursor
let private show (spec: Spec): DialogResult =
    // Disposed after the dialog (the title bar and the taskbar use it until then)
    use appIcon = try Icon.ExtractAssociatedIcon(Application.ExecutablePath) with _ -> null
    use form = new Form(Text = spec.title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                        MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen,
                        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink)
    form.SuspendLayout()
    // Built at 96 DPI and scaled when the layout resumes (all the controls added before), and when
    // it moves to another display (Program.main: PerMonitorV2)
    form.AutoScaleDimensions <- SizeF(96.0f, 96.0f)
    form.AutoScaleMode <- AutoScaleMode.Dpi
    if not (isNull appIcon) then form.Icon <- appIcon

    // The margins and the gaps as those of the Java version. No margin of its own (3 by default):
    // the right and the bottom of the dialog were wider than the left and the top.
    let table = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                     ColumnCount = 2, RowCount = 2, Padding = Padding(12), Margin = Padding.Empty)
    // At the top of the heading, as the icons of the message boxes
    let icon = new PictureBox(SizeMode = PictureBoxSizeMode.AutoSize, Anchor = (AnchorStyles.Top ||| AnchorStyles.Left),
                              Margin = Padding(0, 0, 16, 0))
    let setIcon = imageSetter icon (fun () -> icon.Image) (fun i -> icon.Image <- i)
    let heading = new Label(AutoSize = true, Margin = Padding(0, 0, 0, spec.headingGap))
    let content = new FlowLayoutPanel(FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                                      AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty)
    content.Controls.Add(heading)
    for c in spec.body do content.Controls.Add(c)
    // Added from the left, so that Tab goes from the left to the right (see SettingsForm.askText)
    let buttons = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                                      Anchor = AnchorStyles.Right, Margin = Padding(0, spec.buttonsGap, 0, 0))
    spec.buttons |> List.iteri (fun i b ->
        b.Margin <- Padding((if i = 0 then 0 else 6), 0, 0, 0)
        buttons.Controls.Add(b))
    table.Controls.Add(icon, 0, 0)
    table.Controls.Add(content, 1, 0)
    match spec.footer with
    | None ->
        table.Controls.Add(buttons, 0, 1)
        table.SetColumnSpan(buttons, 2)
    | Some footer ->
        // Under the contents, the buttons on the right of the column
        let bottom = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                                          RowCount = 1, Dock = DockStyle.Fill, Margin = Padding(0, spec.buttonsGap, 0, 0))
        bottom.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 100.0f)) |> ignore
        bottom.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        footer.Anchor <- AnchorStyles.Left
        // Not touching the buttons if it is long (as far from them as the buttons from each other)
        footer.Margin <- Padding(0, 0, 6, 0)
        buttons.Margin <- Padding.Empty
        bottom.Controls.Add(footer, 0, 0)
        bottom.Controls.Add(buttons, 1, 0)
        table.Controls.Add(bottom, 1, 1)
    form.Controls.Add(table)
    form.ResumeLayout(false)
    heading.Text <- spec.heading
    spec.texts ()

    // The bold font of the heading, made from that of the dialog (at its DPI)
    let mutable headingFont: Font = null
    form.Disposed.Add(fun _ -> if not (isNull headingFont) then headingFont.Dispose())

    // The sizes that depend on the DPI of the display: after the handle has been created (Load),
    // and after the controls have been scaled to a new DPI
    let fit () =
        form.SuspendLayout()
        try
            let old = headingFont
            headingFont <- new Font(form.Font, FontStyle.Bold)
            heading.Font <- headingFont
            if not (isNull old) then old.Dispose()
            let wrap = form.LogicalToDeviceUnits(spec.textWidth)
            for c in content.Controls do
                match c with
                | :? Label as l -> l.MaximumSize <- Size(wrap, 0)
                | _ -> ()
            content.MinimumSize <- Size(form.LogicalToDeviceUnits(spec.minWidth), 0)
            setIcon (fun () -> iconImage spec.makeIcon (form.LogicalToDeviceUnits(spec.iconSize)))
            // Without an icon, the box (100 x 50 without an image) hidden and the texts on the left
            icon.Visible <- not (isNull icon.Image)
            spec.fit ()
            // All the buttons as large as the largest (measured at their own size first), at least
            // as wide as the buttons of the message boxes
            for b in spec.buttons do b.MinimumSize <- Size.Empty
            let width = spec.buttons |> List.fold (fun w b -> max w b.PreferredSize.Width) (form.LogicalToDeviceUnits(75))
            let height = spec.buttons |> List.fold (fun h b -> max h b.PreferredSize.Height) 0
            for b in spec.buttons do b.MinimumSize <- Size(width, height)
        finally
            form.ResumeLayout(true)

    form.Load.Add(fun _ ->
        fit ()
        // Centered again at its fitted size: CenterScreen placed it at its size before
        form.PerformLayout()
        let wa = Screen.FromControl(form).WorkingArea
        form.Location <- Point(max wa.Left (wa.Left + (wa.Width - form.Width) / 2),
                               max wa.Top (wa.Top + (wa.Height - form.Height) / 2)))
    // Later: the event is raised before the controls are scaled to the new DPI
    // Not if the dialog has been closed meanwhile
    form.DpiChanged.Add(fun _ -> form.BeginInvoke(Action(fun () -> if not form.IsDisposed then fit ())) |> ignore)

    spec.prepare form
    form.ShowDialog()

// The shield of UAC on a button, before its text and as high as it
let private shieldSetter (b: Button) =
    let set = imageSetter b (fun () -> b.Image) (fun i -> b.Image <- i)
    fun () -> set (fun () -> iconImage (fun size -> SystemIcons.GetStockIcon(StockIconId.Shield, size)) b.Font.Height)

// At startup when W10Wheel is not running as administrator (before the hooks): whether to restart as
// administrator, and whether not to ask again (only with Continue: the choice to run without the
// rights, asked again by checking the setting in the settings dialog). Closed (Esc, the close box):
// continue, and asked again at the next startup. The link opens the wiki (startup as administrator,
// etc.) and leaves the dialog open. conv: the language (before the properties are loaded).
let askRestartAsAdmin (conv: string -> string) (openLink: string -> unit): bool * bool =
    let text = new Label(AutoSize = true, Margin = Padding(0, 0, 0, 8))
    let link = new LinkLabel(AutoSize = true, Margin = Padding(0, 0, 0, 8))
    let dontAsk = new CheckBox(AutoSize = true, Margin = Padding(0, 0, 0, 8))
    let restart = new DialogButton(DialogResult = DialogResult.Yes, TextImageRelation = TextImageRelation.ImageBeforeText,
                                   Padding = Padding(8, 2, 8, 2))
    let continueWithout = new DialogButton(DialogResult = DialogResult.No, Padding = Padding(8, 2, 8, 2))
    // What the restart does: the note of the command link of the task dialog before, a tip as in the
    // Java version
    use tips = new ToolTip()
    // Kept as it is checked: the check box is disposed with the dialog when show returns
    let dontAskChecked = ref false
    dontAsk.CheckedChanged.Add(fun _ -> dontAskChecked.Value <- dontAsk.Checked)
    link.LinkClicked.Add(fun e -> if isLinkOpen e then openLink AppDef.WIKI_URL)
    let setShield = shieldSetter restart

    let res =
        show { title = AppDef.PROGRAM_NAME_NET
               iconSize = 32
               makeIcon = (fun size -> SystemIcons.GetStockIcon(StockIconId.Info, size))
               heading = conv "W10Wheel is not running as administrator"
               headingGap = 8
               // Wider than in the Java version: the text in two lines, not a word on a third
               textWidth = 400
               minWidth = 0
               buttonsGap = 12
               footer = None
               body = [text; link; dontAsk]
               buttons = [restart :> Button; continueWithout]
               texts = (fun () ->
                   text.Text <- conv LocaleData.AdminMessage
                   link.Text <- conv "More info (Wiki)"
                   setLinkColors link
                   dontAsk.Text <- conv "Do not ask again"
                   restart.Text <- conv "Restart as administrator"
                   continueWithout.Text <- conv "Continue without administrator rights"
                   tips.SetToolTip(restart, conv "Windows asks for permission (User Account Control)"))
               fit = setShield
               prepare = (fun form ->
                   form.AcceptButton <- restart
                   form.ActiveControl <- restart
                   // Esc closes it as the close box does (not CancelButton: Continue would store
                   // "Do not ask again")
                   form.KeyPreview <- true
                   form.KeyDown.Add(fun e ->
                       // Esc alone (not with Shift, etc.), as CancelButton
                       if e.KeyData = Keys.Escape then
                           e.Handled <- true
                           form.DialogResult <- DialogResult.Cancel)) }
    res = DialogResult.Yes, (res = DialogResult.No && dontAskChecked.Value)

// The version of W10Wheel and of .NET, whether it runs as administrator (it does not work in the
// windows of the programs that do, if not), and the links to GitHub and the wiki (the dialog stays
// open)
let showAbout (conv: string -> string) (isAdmin: bool) (openLink: string -> unit) =
    let text = new Label(AutoSize = true, Margin = Padding.Empty)
    let links = new LinkLabel(AutoSize = true, Margin = Padding.Empty)
    let ok = new DialogButton(DialogResult = DialogResult.OK)
    links.LinkClicked.Add(fun e -> if isLinkOpen e && not (isNull e.Link) then openLink (string e.Link.LinkData))

    let arch = Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
    show { title = conv "About"
           iconSize = 48
           makeIcon = (fun size -> Icon.ExtractIcon(Application.ExecutablePath, 0, size))
           heading = AppDef.PROGRAM_NAME_NET
           // A line between the name and the version, as in the Java version
           headingGap = 16
           textWidth = 320
           // Not as narrow as its short lines
           minWidth = 200
           buttonsGap = 8
           footer = Some(links)
           body = [text]
           buttons = [ok :> Button]
           texts = (fun () ->
               text.Text <- sprintf "%s: %s\n%s: %s\n.NET: %s (%s)" (conv "Version") AppDef.PROGRAM_VERSION
                                    (conv "Running as administrator") (conv (if isAdmin then "Yes" else "No"))
                                    (string Environment.Version) arch
               // Two links in one label (a text sets a single link over it: the links after it)
               links.Text <- "GitHub    Wiki"
               links.Links.Clear()
               links.Links.Add(0, 6, AppDef.PROJECT_URL) |> ignore
               links.Links.Add(10, 4, AppDef.WIKI_URL) |> ignore
               setLinkColors links
               ok.Text <- conv "OK")
           fit = ignore
           prepare = (fun form ->
               form.AcceptButton <- ok
               form.CancelButton <- ok
               form.ActiveControl <- ok) }
    |> ignore
