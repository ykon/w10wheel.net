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
        // With the device context of the dialog once it exists (not the box's: the boxes of the
        // pages not shown yet have none): on a display of another DPI than the main one, the
        // screen's would measure at the DPI of the main display. Measured again at Load.
        let form = cb.FindForm()
        if not (isNull form) && form.IsHandleCreated then
            use g = form.CreateGraphics()
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

// Sets a stock icon of Windows (e.g. the shield of UAC) as the image of owner (get, set), as high
// as a line of the text of lineOf: made again only when the icon or that height changes (another
// DPI), the image it replaces disposed (and the last one with owner)
let private stockIconSetter (owner: Control) (lineOf: Control) (get: unit -> Image) (set: Image -> unit) =
    let mutable shown = None
    owner.Disposed.Add(fun _ -> let i = get() in if not (isNull i) then i.Dispose())
    fun (id: StockIconId) ->
        let size = lineOf.Font.Height
        if shown <> Some(id, size) then
            use ico = SystemIcons.GetStockIcon(id, size)
            let old = get()
            set (ico.ToBitmap())
            if not (isNull old) then old.Dispose()
            shown <- Some(id, size)

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
// validate: why the text cannot be used (an English text), checked as it is typed: shown under the
// box, and OK disabled (also while the box is empty), instead of an error after OK.
let askText (owner: IWin32Window) (conv: string -> string) (title: string) (prompt: string)
            (validate: string -> string option): string option =
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
    let error = new Label(AutoSize = true)
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
    table.Controls.Add(error)
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
    // The reason keeps a line when there is none (the dialog does not change its size as it is
    // typed): the reasons are short enough for a line (else they wrap at the width of the box)
    error.MaximumSize <- Size(box.Width, 0)
    error.MinimumSize <- Size(0, error.Font.Height)

    let check () =
        let why = if box.Text = "" then None else validate box.Text
        error.Text <- why |> Option.map conv |> Option.defaultValue ""
        ok.Enabled <- box.Text <> "" && why.IsNone
    box.TextChanged.Add(fun _ -> check ())
    check ()

    if f.ShowDialog(owner) = DialogResult.OK && box.Text <> "" then Some(box.Text) else None

// The kind of a note of a page (SettingsPage.AddNote, AddStatus): its icon (the stock icons of
// Windows) and its color. Warning: a setting does not work as it is set (e.g. no trigger), in the
// color of the texts; Info: an explanation (e.g. what a setting depends on), in gray; Plain: in
// gray without an icon, as the explanation of the setting above it (e.g. the state of the custom
// table): the text where the text of its check box starts, and the icons only on the notes.
[<RequireQualifiedAccess>]
type NoteKind = Info | Warning | Plain

// A page: rows of a label and a control (with the range of a number), or of a check box. A
// change by the user sets the setting and calls changed (changedLater for a number); a change by
// refresh (isRefreshing) does not. isReloading: the settings have been loaded (Reload, a switch of
// the properties): a number being typed is dropped. tip (of the Add methods): an English text
// shown on the controls (setTip), converted as the other texts; key: the name of the setting in
// the properties file, shown after it (to find it in the file and the Readme).
type SettingsPage internal (conv: string -> string, table: TableLayoutPanel, register: Binding -> unit,
                            isRefreshing: unit -> bool, changed: unit -> unit, changedLater: unit -> unit,
                            flushChanged: unit -> unit, setTip: Control -> string -> string option -> unit,
                            isReloading: unit -> bool) =
    let mutable gap = false

    let tipOn (controls: Control list) (tip: string option) (key: string option) =
        tip |> Option.iter (fun t -> for c in controls do setTip c t key)

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

    // A note with the icon of its kind before it (getNote: its kind and its text, already
    // converted; None: an empty row). Its row keeps the height of a line when empty, so that the
    // page does not grow beyond its area when it shows (the area is fitted at Show). The icon is as
    // high as a line of the text: made again when the font changes (another DPI) or the kind.
    let addNote (getNote: unit -> (NoteKind * string) option) (indent: bool) =
        // The margins of the label it replaces (Label: 3, 0, 3, 0)
        let panel = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                        WrapContents = false, Anchor = AnchorStyles.Left,
                                        Margin = Padding((if indent then 19 else 3), 0, 3, 0))
        // Anchor Left (not Top): centered on the line of the text
        let icon = new PictureBox(SizeMode = PictureBoxSizeMode.AutoSize, Anchor = AnchorStyles.Left,
                                  Margin = Padding.Empty, Visible = false)
        let l = new Label(AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding(4, 0, 0, 0))
        panel.Controls.Add(icon)
        panel.Controls.Add(l)
        let setIcon = stockIconSetter icon l (fun () -> icon.Image) (fun i -> icon.Image <- i)
        let show () =
            let note = getNote()
            l.Text <- note |> Option.map snd |> Option.defaultValue ""
            l.ForeColor <-
                match note with
                | Some(NoteKind.Warning, _) -> SystemColors.ControlText
                | _ -> SystemColors.GrayText
            // Hidden: the text moves to its place
            match note with
            | Some(NoteKind.Warning, _) -> setIcon StockIconId.Warning; icon.Visible <- true
            | Some(NoteKind.Info, _) -> setIcon StockIconId.Info; icon.Visible <- true
            | Some(NoteKind.Plain, _) | None -> icon.Visible <- false
        register {
            refresh = show
            retext = (fun () ->
                l.MinimumSize <- Size(0, l.Font.Height)
                show ()) }
        addRow [panel :> Control, 3]

    member _.AddGap () =
        gap <- true

    // enabled: whether it can be changed (e.g. Dragged Lock for a drag trigger only); indent: an
    // option of the setting above (e.g. of the mouse trigger)
    member _.AddCheck (engText: string, get: unit -> bool, set: bool -> unit, ?enabled: unit -> bool, ?indent: bool,
                       ?tip: string, ?key: string) =
        let cb = checkBox engText get set
        enabled |> Option.iter (fun f -> register { refresh = (fun () -> cb.Enabled <- f()); retext = ignore })
        if defaultArg indent false then
            cb.Margin <- Padding(cb.Margin.Left + 16, cb.Margin.Top, cb.Margin.Right, cb.Margin.Bottom)
        tipOn [cb] tip key
        addRow [cb :> Control, 3]

    // A check box with the ComboBox of its choice on its right, enabled while it is checked (e.g.
    // the keyboard trigger and its key)
    member _.AddCheckComboBox (engText: string, get: unit -> bool, set: bool -> unit,
                               items: (string * string) list, isCurrent: string -> bool, setItem: string -> unit,
                               ?tip: string, ?key: string) =
        let check = checkBox engText get set
        let cb = comboBox items isCurrent setItem
        register { refresh = (fun () -> cb.Enabled <- check.Checked); retext = ignore }
        tipOn [check; cb] tip key
        addRow [check :> Control, 1; cb :> Control, 2]

    // A button of an action (e.g. Restart as administrator), with an icon before its text (a stock
    // icon of Windows, sized as the icons of the notes: also on a display of another DPI). enabled:
    // as AddCheck (e.g. not while the action is in progress).
    member _.AddButton (engText: string, onClick: unit -> unit, ?icon: StockIconId, ?enabled: unit -> bool,
                        ?tip: string) =
        // GrowAndShrink: GrowOnly kept a size scaled twice with an image (twice as high at 175%)
        let b = new Button(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left,
                           TextImageRelation = TextImageRelation.ImageBeforeText, Padding = Padding(8, 2, 8, 2))
        let setIcon = stockIconSetter b b (fun () -> b.Image) (fun i -> b.Image <- i)
        // A number waiting to be stored first, as the buttons of the dialog do
        b.Click.Add(fun _ ->
            flushChanged ()
            onClick ())
        register {
            refresh = (fun () -> enabled |> Option.iter (fun f -> b.Enabled <- f()))
            retext = (fun () ->
                b.Text <- conv engText
                icon |> Option.iter setIcon) }
        tipOn [b] tip None
        addRow [b :> Control, 3]

    // A short note (e.g. what a setting of the page depends on): Info if no kind
    member _.AddNote (engText: string, ?kind: NoteKind) =
        let kind = defaultArg kind NoteKind.Info
        addNote (fun () -> Some(kind, conv engText)) false

    // A note that follows the settings (getNote: its kind and its text, already converted; None if
    // none): e.g. the state of the custom table. indent: as AddCheck.
    member _.AddStatus (getNote: unit -> (NoteKind * string) option, ?indent: bool) =
        addNote getNote (defaultArg indent false)

    // A number out of its range cannot be entered (NumericUpDown keeps it in the range). enabled:
    // as AddCheck; unit: shown after the range (e.g. ms).
    member _.AddNumber (name: string, low: int, up: int, get: unit -> int, set: int -> unit, ?enabled: unit -> bool,
                        ?unit: string, ?tip: string, ?key: string) =
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
                // Only if the setting has changed, or the settings have been loaded (Reload,
                // --sendReloadProp: the file is taken, even if its number is the same): a number
                // being typed is dropped first (reading Value takes it, while refreshing: not set),
                // or setting Value would parse and keep it. Otherwise it is left as typed, to be set
                // when it is entered.
                if v <> shown || isReloading() then
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
        tipOn [l; n] tip key
        addRow [l :> Control, 1; n :> Control, 1; range :> Control, 1]

    // items: the IDs of the choices and their texts; isCurrent: whether the ID is the setting
    member _.AddComboBox (engText: string, items: (string * string) list, isCurrent: string -> bool, set: string -> unit,
                          ?tip: string, ?key: string) =
        let cb = comboBox items isCurrent set
        let l = label engText
        tipOn [l; cb] tip key
        addRow [l :> Control, 1; cb :> Control, 2]

    // A group of its own (one panel): the other radio buttons of the page are not unchecked.
    // enabled: as AddCheck.
    member _.AddRadioButtons (items: (string * string) list, isCurrent: string -> bool, set: string -> unit,
                              ?enabled: unit -> bool, ?tip: string, ?key: string) =
        let panel = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                        FlowDirection = FlowDirection.TopDown, WrapContents = false,
                                        Margin = Padding(16, 0, 0, 0))
        items |> List.iter (fun (id, engText) ->
            let rb = new RadioButton(AutoSize = true)
            rb.CheckedChanged.Add(fun _ -> if rb.Checked then onChange (fun () -> set id))
            register {
                refresh = (fun () -> rb.Checked <- isCurrent id)
                retext = (fun () -> rb.Text <- conv engText) }
            tipOn [rb] tip key
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
    // Also on the controls inside c: a NumericUpDown is covered by its text box and its buttons,
    // which have their own tooltips (and are found under the mouse when it is disabled). key: the
    // name of the setting in the properties file, on a line of its own.
    let setTip (c: Control) (engText: string) (key: string option) =
        register {
            refresh = ignore
            retext = (fun () ->
                let text =
                    match key with
                    | Some k -> sprintf "%s%s(%s)" (conv engText) Environment.NewLine k
                    | None -> conv engText
                tips.SetToolTip(c, text)
                for child in c.Controls do tips.SetToolTip(child, text)) }

    // The tooltips of the grayed controls (e.g. Custom Table, Dragged Lock): a disabled control gets
    // no mouse messages (Windows sends them to its parent, the page), so the ToolTip never shows its
    // text. The page shows it while the mouse is on such a control (also a radio button in a
    // disabled panel). As the ToolTip does: after its delay (InitialDelay, not as soon as the mouse
    // crosses such a control), and not again on the same control after it has gone (AutoPopDelay).
    let showTipsOfDisabled (page: Control) =
        // The control whose tip is shown, or waits for the delay
        let mutable shownFor: Control = null
        let mutable at = Point.Empty
        let delay = new System.Windows.Forms.Timer(Interval = tips.InitialDelay)
        let hide () =
            delay.Stop()
            tips.Hide(page)
            shownFor <- null
        delay.Tick.Add(fun _ ->
            delay.Stop()
            // Still disabled: it may have been enabled while waiting (e.g. --sendReloadProp)
            if not (isNull shownFor) && not shownFor.Enabled then
                tips.Show(tips.GetToolTip(shownFor), page, at, tips.AutoPopDelay))
        page.MouseMove.Add(fun e ->
            let rec under (c: Control) (pt: Point) =
                match c.GetChildAtPoint(pt) with
                | null -> c
                | child -> under child (Point(pt.X - child.Left, pt.Y - child.Top))
            let c = under page e.Location
            // A NumericUpDown is covered by its text box and its buttons: the tip stays from one to
            // the other
            let c = match c.Parent with :? NumericUpDown as n -> n :> Control | _ -> c
            let target =
                if not (obj.ReferenceEquals(c, page)) && not c.Enabled && tips.GetToolTip(c) <> "" then c else null
            // Where the mouse is when it shows
            at <- Point(e.X, e.Y + page.LogicalToDeviceUnits(20))
            if not (obj.ReferenceEquals(target, shownFor)) then
                hide ()
                if not (isNull target) then
                    shownFor <- target
                    delay.Start())
        page.MouseLeave.Add(fun _ -> hide ())
        // Also when the mouse does not leave the page: another page shown (the arrow keys in the
        // list), the dialog closed (Esc) or another window activated (Alt+Tab). Else the tip stayed
        // on the screen until its AutoPopDelay, and was not shown again on the same control. The
        // dialog hidden does not raise VisibleChanged of its pages (nor Deactivate if it was not
        // active).
        page.VisibleChanged.Add(fun _ -> if not page.Visible then hide ())
        form.VisibleChanged.Add(fun _ -> if not form.Visible then hide ())
        form.Deactivate.Add(fun _ -> hide ())
        page.Disposed.Add(fun _ -> delay.Dispose())

    // The controls set from the settings do not change them (see SettingsPage.onChange)
    let withRefreshing f =
        refreshing <- true
        try f () finally refreshing <- false

    let fitPages () =
        // Laid out once at the end: each minimum size set laid out the list, and the dialog with
        // it, again (twice for each item)
        middle.SuspendLayout()
        nav.SuspendLayout()
        try
            // The items of the list are as wide as the widest
            for b in navButtons do b.MinimumSize <- Size.Empty
            let navW = navButtons |> Seq.map (fun b -> b.GetPreferredSize(Size.Empty).Width) |> Seq.fold max 0
            for b in navButtons do b.MinimumSize <- Size(navW, 0)

            // The area of the pages does not size itself: as large as the largest page (it does
            // not change with the page), as high as the list, and as wide as the rows above and
            // below
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
        finally
            nav.ResumeLayout(true)
            middle.ResumeLayout(true)

    // Runs f, which may resize the dialog (fitPages), and moves it into the working area of its
    // display if it has grown out of it (e.g. a longer state of the custom table after Reload, with
    // the dialog at the right of the screen: Close went out of it). Not moved if its size has not
    // changed: it can be left partly out of the screen.
    let keepingOnScreen f =
        let size = form.Size
        f ()
        if form.Visible && form.Size <> size then
            let wa = Screen.FromControl(form).WorkingArea
            let x = max wa.Left (min form.Left (wa.Right - form.Width))
            let y = max wa.Top (min form.Top (wa.Bottom - form.Height))
            if x <> form.Left || y <> form.Top then form.Location <- Point(x, y)

    // The pages fitted again after all the controls have been refreshed: a text that follows the
    // settings may have become wider or narrower (e.g. the state of the custom table after Reload).
    // The list of the properties fits them when it is read again, but before the controls after it
    // have been refreshed.
    let refresh () =
        keepingOnScreen (fun () ->
            withRefreshing (fun () -> for b in bindings do b.refresh())
            if form.IsHandleCreated then fitPages())

    let centerOn (screen: Screen) =
        let wa = screen.WorkingArea
        form.Location <- Point(max wa.Left (wa.Left + (wa.Width - form.Width) / 2),
                               max wa.Top (wa.Top + (wa.Height - form.Height) / 2))

    // The containers under c (c too), from the outermost: their layouts suspended while the texts
    // are set (each text laid out its row, its page and the containers up to the dialog again, all
    // auto sized: hundreds of times for a switch of the language, slow, more on the screen), and
    // resumed from the innermost, each laid out once
    let rec containersIn (c: Control) =
        seq {
            if c.Controls.Count > 0 then yield c
            for child in c.Controls do yield! containersIn child }

    let retext () =
        // Kept on the screen: the dialog may have grown (e.g. another language)
        keepingOnScreen (fun () ->
            // The containers of the dialog and of the area of the pages, resumed last; the others
            // (the rows, the list, the pages) first
            let outer: Control[] = [| form; root; middle; content |]
            let inner =
                containersIn form |> Seq.filter (fun c -> not (Array.exists (fun o -> obj.ReferenceEquals(o, c)) outer))
                |> Array.ofSeq
            for c in outer do c.SuspendLayout()
            try
                for c in inner do c.SuspendLayout()
                try
                    withRefreshing (fun () ->
                        form.Text <- sprintf "%s - %s" AppDef.PROGRAM_NAME_NET (conv "Settings")
                        for b in bindings do b.retext()
                        for b in bindings do b.refresh())
                finally
                    for c in Array.rev inner do c.ResumeLayout(true)
                // The sizes of the texts: after the first show (Load), when the handles have been
                // created. The rows and the pages laid out (measured before, a page kept the size it
                // had if only a row inside a row of it had changed), not yet the dialog: it is laid
                // out and resized once, at its new size.
                if form.IsHandleCreated then fitPages()
            finally
                for c in Array.rev outer do c.ResumeLayout(true))

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
        register { refresh = ignore; retext = (fun () -> hint.Text <- conv "Changes are applied and saved at once") }
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
            // The texts measured again with the handle of the dialog (fitComboBox), and the pages
            // fitted
            retext ()
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
            // The tints computed again at each Show (retext): the colors of the theme may have
            // changed since (light or dark)
            register {
                refresh = ignore
                retext = (fun () ->
                    item.FlatAppearance.CheckedBackColor <- highlightTint 0.2
                    item.FlatAppearance.MouseOverBackColor <- highlightTint 0.1
                    item.FlatAppearance.MouseDownBackColor <- highlightTint 0.3) }
        item.CheckedChanged.Add(fun _ ->
            if item.Checked then
                pages |> Seq.iteri (fun i page -> page.Visible <- (i = index)))
        register { refresh = ignore; retext = (fun () -> item.Text <- conv engText) }
        navButtons.Add(item)
        nav.Controls.Add(item)
        if index = 0 then item.Checked <- true

        SettingsPage(conv, table, register, (fun () -> refreshing), (fun () -> changed ()), changedLater,
                     flushChanged, setTip, (fun () -> readLists))

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

        // fit: the area of the pages fitted to the row of the properties again (not when the dialog
        // is refreshed or retexted: fitted after all the controls)
        let reload (fit: bool) =
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
            if fit && form.IsHandleCreated then fitPages()

        cb.DropDown.Add(fun _ -> withRefreshing (fun () -> reload true))
        cb.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && cb.SelectedIndex >= 0 then
                // Taken first: storing a number waiting (flushChanged) refreshes the dialog, which
                // selects the current properties again
                let id = ids.[cb.SelectedIndex]
                flushChanged ()
                switchTo id)
        register {
            // Read again after a load, Add, Delete or Reload (RefreshAll): a deleted name left in
            // the list could be chosen with the keys or the wheel, and its file written again.
            // After a change of a setting, only for a name not in the list.
            refresh = (fun () ->
                if readLists || not (Array.exists isCurrent ids) then reload false else selectCurrent ())
            retext = (fun () -> reload false) }
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
