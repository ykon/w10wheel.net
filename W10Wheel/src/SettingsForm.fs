module SettingsForm

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System
open System.Drawing
open System.Windows.Forms

// The settings dialog, built in code. A control changes its setting at once (then the dialog's
// Changed is called, e.g. to store the properties), and shows the setting again when the dialog
// is refreshed. The texts are English (as in the menu) converted by conv to the current language.

// What a control does when the dialog is refreshed (show its setting) and retexted (set its texts
// in the current language)
type internal Binding = { refresh: unit -> unit; retext: unit -> unit }

// The controls of the visual styles of .NET 11 (Program.main: Net11) have more padding than the
// classic ones (Classic: Application.EnableVisualStyles only)
let private isModernStyles () =
    Application.DefaultVisualStylesMode <> VisualStylesMode.Classic &&
    Application.DefaultVisualStylesMode <> VisualStylesMode.Disabled

// A ComboBox (DropDownList) does not size itself: wide enough for its longest item
let private fitComboBox (cb: ComboBox) =
    let textWidth =
        let widths (measure: string -> Size) =
            cb.Items |> Seq.cast<obj> |> Seq.map (fun item -> (measure (string item)).Width) |> Seq.fold max 0
        // With the device context of the box once it exists: on a display of another DPI than
        // the main one, the screen's would measure at the DPI of the main display
        if cb.IsHandleCreated then
            use g = cb.CreateGraphics()
            widths (fun text -> TextRenderer.MeasureText(g, text, cb.Font))
        else
            widths (fun text -> TextRenderer.MeasureText(text, cb.Font))
    let button = SystemInformation.GetVerticalScrollBarWidthForDpi(cb.DeviceDpi)
    let padding = if isModernStyles() then 40 else 10
    cb.Width <- textWidth + button + cb.LogicalToDeviceUnits(padding)

// Sets the numbers being typed in the NumericUpDowns under c: reading Value takes the text typed
// (ValueChanged then sets it, see SettingsPage.AddNumber)
let rec private commitNumbers (c: Control) =
    for child in c.Controls do
        match child with
        | :? NumericUpDown as n -> n.Value |> ignore
        | _ -> commitNumbers child

// The highlight color of the theme mixed with the window color (amount: 0.0 - 1.0 of the
// highlight), as the selected item in the list of the settings of the browsers
let private highlightTint (amount: float) =
    let h = SystemColors.Highlight
    let w = SystemColors.Window
    let mix (a: byte) (b: byte) = int (float b + (float a - float b) * amount)
    Color.FromArgb(mix h.R w.R, mix h.G w.G, mix h.B w.B)

// A text asked in a small dialog centered on owner (e.g. the name of new properties), in the
// language of W10Wheel (the input box of Visual Basic used the language of Windows and could not
// be centered on the settings dialog). None if canceled or empty.
let askText (owner: IWin32Window) (conv: string -> string) (title: string) (prompt: string): string option =
    use f = new Form(Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                     MinimizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent,
                     AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink)
    f.SuspendLayout()
    // Built at 96 DPI and scaled when the layout resumes (all the controls added before)
    f.AutoScaleDimensions <- SizeF(96.0f, 96.0f)
    f.AutoScaleMode <- AutoScaleMode.Dpi

    let table = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                     ColumnCount = 1, Padding = Padding(12))
    let box = new TextBox(Width = 300, Anchor = (AnchorStyles.Left ||| AnchorStyles.Right))
    let label = new Label(AutoSize = true)
    let ok = new Button(AutoSize = true, DialogResult = DialogResult.OK)
    let cancel = new Button(AutoSize = true, DialogResult = DialogResult.Cancel)
    // OK on the left of Cancel, both on the right: added in this order, so that Tab goes from the
    // box to OK, then Cancel (RightToLeft with Cancel added first went to Cancel first)
    let buttons = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                      WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding(0, 8, 0, 0))
    buttons.Controls.Add(ok)
    buttons.Controls.Add(cancel)
    table.Controls.Add(label)
    table.Controls.Add(box)
    table.Controls.Add(buttons)
    f.Controls.Add(table)
    f.AcceptButton <- ok
    f.CancelButton <- cancel
    f.ResumeLayout(false)
    // The texts after the scaling (as SettingsDialog.retext): set before, the auto sized buttons
    // were scaled again (twice as high at 175%)
    label.Text <- prompt
    ok.Text <- conv "OK"
    cancel.Text <- conv "Cancel"

    if f.ShowDialog(owner) = DialogResult.OK && box.Text <> "" then Some(box.Text) else None

// A page: rows of a label and a control (with the range of a number), or of a check box. A
// change by the user sets the setting and calls changed (changedLater for a number); a change by
// refresh (isRefreshing) does not. tip (of the Add methods): an English text shown on the controls
// (setTip), converted as the other texts.
type SettingsPage internal (conv: string -> string, table: TableLayoutPanel, register: Binding -> unit,
                            isRefreshing: unit -> bool, changed: unit -> unit, changedLater: unit -> unit,
                            flushChanged: unit -> unit, setTip: Control -> string -> unit) =
    let mutable gap = false

    let tipOn (controls: Control list) (tip: string option) =
        tip |> Option.iter (fun t -> for c in controls do setTip c t)

    let addRow (controls: (Control * int) list) =
        // The space between the groups of settings (AddGap)
        let extra = if gap then 10 else 0
        gap <- false

        let row = table.RowCount
        table.RowCount <- row + 1
        table.RowStyles.Add(RowStyle(SizeType.AutoSize)) |> ignore
        controls |> List.fold (fun col (c, span) ->
            c.Margin <- Padding(c.Margin.Left, c.Margin.Top + extra, c.Margin.Right, c.Margin.Bottom)
            table.Controls.Add(c, col, row)
            table.SetColumnSpan(c, span)
            col + span) 0 |> ignore

    let label (engText: string) =
        let l = new Label(AutoSize = true, Anchor = AnchorStyles.Left)
        register { refresh = ignore; retext = (fun () -> l.Text <- conv engText) }
        l

    let onChange (set: unit -> unit) =
        if not (isRefreshing()) then
            set ()
            changed ()

    // items: the IDs of the choices and their texts; isCurrent: whether the ID is the setting
    let comboBox (items: (string * string) list) (isCurrent: string -> bool) (set: string -> unit) =
        let ids = items |> List.map fst |> Array.ofList
        let cb = new ComboBox(DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left)
        cb.SelectedIndexChanged.Add(fun _ ->
            if cb.SelectedIndex >= 0 then onChange (fun () -> set ids.[cb.SelectedIndex]))
        register {
            refresh = (fun () -> cb.SelectedIndex <- Array.tryFindIndex isCurrent ids |> Option.defaultValue -1)
            retext = (fun () ->
                cb.Items.Clear()
                items |> List.iter (fun (_, text) -> cb.Items.Add(conv text) |> ignore)
                fitComboBox cb) }
        cb

    let checkBox (engText: string) (get: unit -> bool) (set: bool -> unit) =
        let cb = new CheckBox(AutoSize = true, Anchor = AnchorStyles.Left)
        cb.CheckedChanged.Add(fun _ -> onChange (fun () -> set cb.Checked))
        register { refresh = (fun () -> cb.Checked <- get()); retext = (fun () -> cb.Text <- conv engText) }
        cb

    member _.AddGap () =
        gap <- true

    // enabled: whether it can be changed (e.g. Dragged Lock for a drag trigger only); indent: an
    // option of the setting above (e.g. of the mouse trigger)
    member _.AddCheck (engText: string, get: unit -> bool, set: bool -> unit, ?enabled: unit -> bool, ?indent: bool,
                       ?tip: string) =
        let cb = checkBox engText get set
        enabled |> Option.iter (fun f -> register { refresh = (fun () -> cb.Enabled <- f()); retext = ignore })
        if defaultArg indent false then
            cb.Margin <- Padding(cb.Margin.Left + 16, cb.Margin.Top, cb.Margin.Right, cb.Margin.Bottom)
        tipOn [cb] tip
        addRow [cb :> Control, 3]

    // A check box with the ComboBox of its choice on its right, enabled while it is checked (e.g.
    // the keyboard trigger and its key)
    member _.AddCheckComboBox (engText: string, get: unit -> bool, set: bool -> unit,
                               items: (string * string) list, isCurrent: string -> bool, setItem: string -> unit,
                               ?tip: string) =
        let check = checkBox engText get set
        let cb = comboBox items isCurrent setItem
        register { refresh = (fun () -> cb.Enabled <- check.Checked); retext = ignore }
        tipOn [check; cb] tip
        addRow [check :> Control, 1; cb :> Control, 2]

    // A button of an action (e.g. Restart as administrator), with an image before its text. enabled:
    // as AddCheck (e.g. not while the action is in progress).
    member _.AddButton (engText: string, onClick: unit -> unit, ?image: Image, ?enabled: unit -> bool, ?tip: string) =
        // GrowAndShrink: with its image before the layout is scaled, GrowOnly kept the scaled size
        // (twice as high at 175%)
        let b = new Button(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left,
                           TextImageRelation = TextImageRelation.ImageBeforeText, Padding = Padding(8, 2, 8, 2))
        image |> Option.iter (fun i -> b.Image <- i)
        // A number waiting to be stored first, as the buttons of the dialog do
        b.Click.Add(fun _ ->
            flushChanged ()
            onClick ())
        register {
            refresh = (fun () -> enabled |> Option.iter (fun f -> b.Enabled <- f()))
            retext = (fun () -> b.Text <- conv engText) }
        tipOn [b] tip
        addRow [b :> Control, 3]

    // A short note in gray (e.g. what a setting of the page depends on)
    member _.AddNote (engText: string) =
        let l = new Label(AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText)
        register { refresh = ignore; retext = (fun () -> l.Text <- conv engText) }
        addRow [l :> Control, 3]

    // A number out of its range cannot be entered (NumericUpDown keeps it in the range). enabled:
    // as AddCheck; unit: shown after the range (e.g. ms).
    member _.AddNumber (name: string, low: int, up: int, get: unit -> int, set: int -> unit, ?enabled: unit -> bool,
                        ?unit: string, ?tip: string) =
        // Wider in the modern styles: their up and down buttons are side by side (at 96 DPI, scaled
        // with the dialog)
        let n = new NumericUpDown(Minimum = decimal low, Maximum = decimal up,
                                  Width = (if isModernStyles() then 90 else 70),
                                  Anchor = AnchorStyles.Left, TextAlign = HorizontalAlignment.Right)
        // The value last shown (set by refresh or by the user): reading n.Value would also take a
        // number being typed
        let mutable shown = Int32.MinValue
        n.ValueChanged.Add(fun _ ->
            shown <- int n.Value
            // Applied at once, stored a little later: holding a button or turning the wheel on the
            // box changes it many times
            if not (isRefreshing()) then
                set shown
                changedLater ())
        register {
            refresh = (fun () ->
                let v = Math.Clamp(get(), low, up)
                // Only if the setting has changed (e.g. Reload): a number being typed is dropped
                // first (reading Value takes it, while refreshing: not set), or setting Value would
                // parse and keep it. Otherwise it is left as typed, to be set when it is entered.
                if v <> shown then
                    n.Value |> ignore
                    n.Value <- decimal v
                    shown <- v)
            retext = ignore }

        let rangeText =
            match unit with
            | Some u -> sprintf "(%d - %d %s)" low up u
            | None -> sprintf "(%d - %d)" low up
        let range = new Label(AutoSize = true, Anchor = AnchorStyles.Left, Text = rangeText)
        let l = label name
        enabled |> Option.iter (fun f ->
            register {
                refresh = (fun () ->
                    let b = f()
                    for c in [l :> Control; n; range] do c.Enabled <- b)
                retext = ignore })
        tipOn [l; n] tip
        addRow [l :> Control, 1; n :> Control, 1; range :> Control, 1]

    // items: the IDs of the choices and their texts; isCurrent: whether the ID is the setting
    member _.AddComboBox (engText: string, items: (string * string) list, isCurrent: string -> bool, set: string -> unit,
                          ?tip: string) =
        let cb = comboBox items isCurrent set
        let l = label engText
        tipOn [l; cb] tip
        addRow [l :> Control, 1; cb :> Control, 2]

    // A group of its own (one panel): the other radio buttons of the page are not unchecked.
    // enabled: as AddCheck.
    member _.AddRadioButtons (items: (string * string) list, isCurrent: string -> bool, set: string -> unit,
                              ?enabled: unit -> bool, ?tip: string) =
        let panel = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                        FlowDirection = FlowDirection.TopDown, WrapContents = false,
                                        Margin = Padding(16, 0, 0, 0))
        items |> List.iter (fun (id, engText) ->
            let rb = new RadioButton(AutoSize = true)
            rb.CheckedChanged.Add(fun _ -> if rb.Checked then onChange (fun () -> set id))
            register {
                refresh = (fun () -> rb.Checked <- isCurrent id)
                retext = (fun () -> rb.Text <- conv engText) }
            tipOn [rb] tip
            panel.Controls.Add(rb))
        enabled |> Option.iter (fun f -> register { refresh = (fun () -> panel.Enabled <- f()); retext = ignore })
        addRow [panel :> Control, 3]

// The dialog: the properties at the top (AddPropertiesComboBox, AddPropertiesButton), the pages
// (AddPage) with their list on the left, and Close. Closing hides it: it is made once, and shown
// again (Show).
type SettingsDialog(conv: string -> string) =
    let bindings = ResizeArray<Binding>()
    let mutable refreshing = false
    // Set by RefreshAll: the list of the properties is read again (the files may have been added
    // or deleted), not at every change of a setting
    let mutable readLists = false
    let mutable changed: unit -> unit = (fun () -> ())

    // A change of a number calls changed 300 ms after the last one (changedLater): changed stores
    // the properties, which a button held on a number box would do many times. Called at once
    // (flushChanged) before the dialog is closed and before the buttons and the switch of the
    // properties act on them.
    let changedTimer = new System.Windows.Forms.Timer(Interval = 300)
    do changedTimer.Tick.Add(fun _ ->
        changedTimer.Stop()
        changed ())

    let changedLater () =
        changedTimer.Stop()
        changedTimer.Start()

    let flushChanged () =
        if changedTimer.Enabled then
            changedTimer.Stop()
            changed ()

    let form = new Form()
    // The layout is suspended until the first Show: see Show
    let mutable laidOut = false
    let root = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                    ColumnCount = 1, Padding = Padding(8))
    let top = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                  WrapContents = false, Anchor = (AnchorStyles.Left ||| AnchorStyles.Right),
                                  Margin = Padding(3, 3, 3, 8))
    // The pages are chosen in a list on the left (as in the settings of the browsers), and shown
    // on the right, one at a time
    let middle = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                      ColumnCount = 2, Anchor = (AnchorStyles.Left ||| AnchorStyles.Right))
    let nav = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                  FlowDirection = FlowDirection.TopDown, WrapContents = false,
                                  Anchor = (AnchorStyles.Top ||| AnchorStyles.Left), Margin = Padding(3, 3, 8, 3))
    let content = new Panel(BackColor = SystemColors.Window, Padding = Padding(12),
                            Anchor = (AnchorStyles.Top ||| AnchorStyles.Left))
    let navButtons = ResizeArray<RadioButton>()
    let pages = ResizeArray<TableLayoutPanel>()
    // The buttons on the left (AddBottomButton, e.g. Reset to defaults), the hint, and Close
    let bottom = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                      ColumnCount = 3, Anchor = (AnchorStyles.Left ||| AnchorStyles.Right))
    let bottomLeft = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                         WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding(0))
    let closeButton = new Button(AutoSize = true, Anchor = AnchorStyles.Right)

    let register (b: Binding) = bindings.Add b

    // The short explanations of the settings, shown longer than the default (5 s) to be read
    let tips = new ToolTip(AutoPopDelay = 20000, InitialDelay = 500)
    let setTip (c: Control) (engText: string) =
        register { refresh = ignore; retext = (fun () -> tips.SetToolTip(c, conv engText)) }

    // The tooltips of the grayed controls (e.g. Custom Table, Dragged Lock): a disabled control gets
    // no mouse messages (Windows sends them to its parent, the page), so the ToolTip never shows its
    // text. The page shows it while the mouse is on such a control (also a radio button in a
    // disabled panel).
    let showTipsOfDisabled (page: Control) =
        let mutable shownFor: Control = null
        let hide () =
            tips.Hide(page)
            shownFor <- null
        page.MouseMove.Add(fun e ->
            let rec under (c: Control) (pt: Point) =
                match c.GetChildAtPoint(pt) with
                | null -> c
                | child -> under child (Point(pt.X - child.Left, pt.Y - child.Top))
            let c = under page e.Location
            let target = if not (obj.ReferenceEquals(c, page)) && not c.Enabled then c else null
            if not (obj.ReferenceEquals(target, shownFor)) then
                hide ()
                if not (isNull target) then
                    let text = tips.GetToolTip(target)
                    if text <> "" then
                        shownFor <- target
                        tips.Show(text, page, Point(e.X, e.Y + page.LogicalToDeviceUnits(20)), tips.AutoPopDelay))
        page.MouseLeave.Add(fun _ -> hide ())

    // The controls set from the settings do not change them (see SettingsPage.onChange)
    let withRefreshing f =
        refreshing <- true
        try f () finally refreshing <- false

    let refresh () =
        withRefreshing (fun () -> for b in bindings do b.refresh())

    let fitPages () =
        // The items of the list are as wide as the widest
        for b in navButtons do b.MinimumSize <- Size.Empty
        let navW = navButtons |> Seq.map (fun b -> b.GetPreferredSize(Size.Empty).Width) |> Seq.fold max 0
        for b in navButtons do b.MinimumSize <- Size(navW, 0)

        // The area of the pages does not size itself: as large as the largest page (it does not
        // change with the page), as high as the list, and as wide as the rows above and below
        let mutable w = 0
        let mutable h = 0
        for page in pages do
            let size = page.GetPreferredSize(Size.Empty)
            w <- max w size.Width
            h <- max h size.Height

        let navSize = nav.GetPreferredSize(Size.Empty)
        let rowWidth (row: Control) = row.GetPreferredSize(Size.Empty).Width + row.Margin.Horizontal
        let besideNav =
            max (rowWidth top) (rowWidth bottom)
            - (navSize.Width + nav.Margin.Horizontal) - content.Margin.Horizontal
        content.Size <- Size(max (w + content.Padding.Horizontal) besideNav,
                             max (h + content.Padding.Vertical) navSize.Height)

    let centerOn (screen: Screen) =
        let wa = screen.WorkingArea
        form.Location <- Point(max wa.Left (wa.Left + (wa.Width - form.Width) / 2),
                               max wa.Top (wa.Top + (wa.Height - form.Height) / 2))

    let retext () =
        withRefreshing (fun () ->
            form.Text <- sprintf "%s - %s" AppDef.PROGRAM_NAME_NET (conv "Settings")
            for b in bindings do b.retext()
            for b in bindings do b.refresh())
        // The sizes of the texts: after the first show (Load), when the handles have been created
        if form.IsHandleCreated then fitPages()

    do
        form.SuspendLayout()
        // Built at 96 DPI: scaled to the DPI of the display when the layout resumes (Show), and when
        // it moves to another display (Program.main: PerMonitorV2)
        form.AutoScaleDimensions <- SizeF(96.0f, 96.0f)
        form.AutoScaleMode <- AutoScaleMode.Dpi
        form.FormBorderStyle <- FormBorderStyle.FixedDialog
        form.MaximizeBox <- false
        form.MinimizeBox <- false
        form.StartPosition <- FormStartPosition.CenterScreen
        form.AutoSize <- true
        form.AutoSizeMode <- AutoSizeMode.GrowAndShrink
        try
            form.Icon <- Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        with
            | _ -> ()

        let hint = new Label(AutoSize = true, Anchor = AnchorStyles.Left)
        register { refresh = ignore; retext = (fun () -> hint.Text <- conv "Changes are applied and saved at once.") }
        register { refresh = ignore; retext = (fun () -> closeButton.Text <- conv "Close") }
        closeButton.Click.Add(fun _ -> form.Close())
        // Esc closes it. Not Enter (AcceptButton): it ends the input of a number.
        form.CancelButton <- closeButton

        bottom.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        bottom.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 100.0f)) |> ignore
        bottom.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        bottom.Controls.Add(bottomLeft, 0, 0)
        bottom.Controls.Add(hint, 1, 0)
        bottom.Controls.Add(closeButton, 2, 0)

        middle.Controls.Add(nav, 0, 0)
        middle.Controls.Add(content, 1, 0)

        root.Controls.Add(top, 0, 0)
        root.Controls.Add(middle, 0, 1)
        root.Controls.Add(bottom, 0, 2)
        form.Controls.Add(root)

        form.Load.Add(fun _ ->
            fitPages()
            // Centered again at its fitted size: CenterScreen placed it at its size before
            form.PerformLayout()
            centerOn (Screen.FromControl(form)))
        // After the controls have been scaled to the new DPI: the texts are measured again
        form.DpiChanged.Add(fun _ -> form.BeginInvoke(Action(retext)) |> ignore)

        form.FormClosing.Add(fun e ->
            // A number being typed is set (as leaving its box would), however the dialog is closed
            // (Esc, the close box, the exit of W10Wheel): the changes are not canceled
            commitNumbers form
            flushChanged ()
            if e.CloseReason = CloseReason.UserClosing then
                e.Cancel <- true
                form.Hide())

        // The components that are not controls of the form are not disposed with it
        form.Disposed.Add(fun _ ->
            changedTimer.Dispose()
            tips.Dispose())

    // Called after a setting has been changed in the dialog (not after a properties switch)
    member _.Changed with set (f: unit -> unit) = changed <- f

    member _.AddPage (engText: string) =
        let index = pages.Count
        // Top, not Fill: the rows would be spread over the height of the largest page
        let table = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                         ColumnCount = 3, Dock = DockStyle.Top, Visible = false)
        content.Controls.Add(table)
        pages.Add(table)
        showTipsOfDisabled table

        // A radio button drawn as a flat button: one is checked, and the arrow keys move in the list
        let item = new RadioButton(Appearance = Appearance.Button, AutoSize = true,
                                   TextAlign = ContentAlignment.MiddleLeft,
                                   Padding = Padding(6, 3, 12, 3), Margin = Padding(0, 0, 0, 2))
        // In high contrast, the standard pressed button shows the page (no colors of their own)
        if not SystemInformation.HighContrast then
            item.FlatStyle <- FlatStyle.Flat
            item.FlatAppearance.BorderSize <- 0
            item.FlatAppearance.CheckedBackColor <- highlightTint 0.2
            item.FlatAppearance.MouseOverBackColor <- highlightTint 0.1
            item.FlatAppearance.MouseDownBackColor <- highlightTint 0.3
        item.CheckedChanged.Add(fun _ ->
            if item.Checked then
                pages |> Seq.iteri (fun i page -> page.Visible <- (i = index)))
        register { refresh = ignore; retext = (fun () -> item.Text <- conv engText) }
        navButtons.Add(item)
        nav.Controls.Add(item)
        if index = 0 then item.Checked <- true

        SettingsPage(conv, table, register, (fun () -> refreshing), (fun () -> changed ()), changedLater,
                     flushChanged, setTip)

    member _.AddPropertiesLabel (engText: string) =
        let l = new Label(AutoSize = true, Anchor = AnchorStyles.Left)
        register { refresh = ignore; retext = (fun () -> l.Text <- conv engText) }
        top.Controls.Add(l)

    // getItems: the names of the properties and their texts, read again when the list opens (the
    // files may have been added or deleted); switchTo: switch to the name. Not Changed: no setting
    // has been changed by the user.
    member _.AddPropertiesComboBox (getItems: unit -> (string * string) list, isCurrent: string -> bool,
                                    switchTo: string -> unit) =
        let cb = new ComboBox(DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left)
        let mutable ids: string[] = [||]

        let selectCurrent () =
            cb.SelectedIndex <- Array.tryFindIndex isCurrent ids |> Option.defaultValue -1

        let reload () =
            let items = getItems()
            ids <- items |> List.map fst |> Array.ofList
            cb.Items.Clear()
            items |> List.iter (fun (_, text) -> cb.Items.Add(text) |> ignore)
            fitComboBox cb
            // The names are the user's: a long one would widen the dialog without a limit. The box
            // is at most 240 wide (at 96 DPI), its list as wide as the longest name.
            cb.DropDownWidth <- cb.Width
            cb.Width <- min cb.Width (cb.LogicalToDeviceUnits(240))
            selectCurrent ()
            // The area of the pages as wide as the row of the properties again
            if form.IsHandleCreated then fitPages()

        cb.DropDown.Add(fun _ -> withRefreshing reload)
        cb.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && cb.SelectedIndex >= 0 then
                flushChanged ()
                switchTo ids.[cb.SelectedIndex])
        register {
            // Read again after a load, Add, Delete or Reload (RefreshAll): a deleted name left in
            // the list could be chosen with the keys or the wheel, and its file written again.
            // After a change of a setting, only for a name not in the list.
            refresh = (fun () ->
                if readLists || not (Array.exists isCurrent ids) then reload () else selectCurrent ())
            retext = reload }
        top.Controls.Add(cb)

    // dropPending: the change waiting to be stored is not stored (Reload: storing would write the
    // settings over the file edited outside W10Wheel, which is about to be read)
    member _.AddPropertiesButton (engText: string, onClick: unit -> unit, ?enabled: unit -> bool, ?dropPending: bool) =
        let b = new Button(AutoSize = true, Anchor = AnchorStyles.Left)
        b.Click.Add(fun _ ->
            if defaultArg dropPending false then changedTimer.Stop() else flushChanged ()
            onClick ())
        register {
            refresh = (fun () -> enabled |> Option.iter (fun f -> b.Enabled <- f()))
            retext = (fun () -> b.Text <- conv engText) }
        top.Controls.Add(b)

    // A button at the bottom left, for the whole dialog (e.g. Reset to defaults)
    member _.AddBottomButton (engText: string, onClick: unit -> unit) =
        let b = new Button(AutoSize = true, Anchor = AnchorStyles.Left)
        b.Click.Add(fun _ ->
            flushChanged ()
            onClick ())
        register { refresh = ignore; retext = (fun () -> b.Text <- conv engText) }
        bottomLeft.Controls.Add(b)

    // The owner of the dialogs opened from it (askText, the confirmations): they are modal to it
    // and centered on it
    member _.Owner = form :> IWin32Window

    // Shows the settings again after a change in the dialog
    member _.RefreshValues () =
        refresh ()

    // Shows the settings and the list of the properties again (after a load, Add, Delete...).
    // Later, from the message loop: a load can be made by the mouse hook (--sendReloadProp), which
    // must return quickly, and a switch by the ComboBox of the properties in its handler. Not made
    // yet (no handle): Show refreshes it.
    member _.RefreshAllLater () =
        if form.IsHandleCreated then
            form.BeginInvoke(Action(fun () ->
                readLists <- true
                try refresh () finally readLists <- false)) |> ignore

    // The language has changed. Later: maybe called from the handler of a control that retext
    // changes (the language ComboBox)
    member _.RetextLater () =
        if form.IsHandleCreated then form.BeginInvoke(Action(retext)) |> ignore

    member _.Show () =
        // The controls are scaled from 96 DPI when the layout resumes: after all the pages have
        // been added (resumed in the constructor, those added after would not be scaled), and
        // before retext measures the texts in the units of the display
        if not laidOut then
            form.ResumeLayout(false)
            laidOut <- true

        if not form.Visible then
            // The settings may have been changed (e.g. Reload) or the language since it was shown
            retext ()
            // The focus on the list of the pages, not on the properties (the first control): its
            // text would be shown selected, as if it were to be changed
            navButtons |> Seq.tryFind (fun b -> b.Checked) |> Option.iter (fun b -> form.ActiveControl <- b)
            // Shown again where it was closed: not if that display is gone (e.g. disconnected),
            // then on the display of the cursor
            if form.IsHandleCreated &&
               not (Screen.AllScreens |> Array.exists (fun s -> s.WorkingArea.IntersectsWith(form.Bounds))) then
                centerOn (Screen.FromPoint(Cursor.Position))
            form.Show()

        form.Activate()
