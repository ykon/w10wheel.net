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

// A ComboBox (DropDownList) does not size itself: wide enough for its longest item
let private fitComboBox (cb: ComboBox) =
    let textWidth =
        cb.Items |> Seq.cast<obj>
        |> Seq.map (fun item -> TextRenderer.MeasureText(string item, cb.Font).Width)
        |> Seq.fold max 0
    let button = SystemInformation.GetVerticalScrollBarWidthForDpi(cb.DeviceDpi)
    cb.Width <- textWidth + button + cb.LogicalToDeviceUnits(10)

// The highlight color of the theme mixed with the window color (amount: 0.0 - 1.0 of the
// highlight), as the selected item in the list of the settings of the browsers
let private highlightTint (amount: float) =
    let h = SystemColors.Highlight
    let w = SystemColors.Window
    let mix (a: byte) (b: byte) = int (float b + (float a - float b) * amount)
    Color.FromArgb(mix h.R w.R, mix h.G w.G, mix h.B w.B)

// A page: rows of a label and a control (with the range of a number), or of a check box. A
// change by the user sets the setting and calls changed; a change by refresh (isRefreshing) does not.
type SettingsPage internal (conv: string -> string, table: TableLayoutPanel, register: Binding -> unit,
                            isRefreshing: unit -> bool, changed: unit -> unit) =
    let mutable gap = false

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

    member _.AddGap () =
        gap <- true

    // enabled: whether it can be changed (e.g. Dragged Lock for a drag trigger only)
    member _.AddCheck (engText: string, get: unit -> bool, set: bool -> unit, ?enabled: unit -> bool) =
        let cb = new CheckBox(AutoSize = true, Anchor = AnchorStyles.Left)
        cb.CheckedChanged.Add(fun _ -> onChange (fun () -> set cb.Checked))
        register {
            refresh = (fun () ->
                cb.Checked <- get()
                enabled |> Option.iter (fun f -> cb.Enabled <- f()))
            retext = (fun () -> cb.Text <- conv engText) }
        addRow [cb :> Control, 3]

    // A number out of its range cannot be entered (NumericUpDown keeps it in the range)
    member _.AddNumber (name: string, low: int, up: int, get: unit -> int, set: int -> unit) =
        let n = new NumericUpDown(Minimum = decimal low, Maximum = decimal up, Width = 70,
                                  Anchor = AnchorStyles.Left, TextAlign = HorizontalAlignment.Right)
        n.ValueChanged.Add(fun _ -> onChange (fun () -> set (int n.Value)))
        register { refresh = (fun () -> n.Value <- decimal (Math.Clamp(get(), low, up))); retext = ignore }

        let range = new Label(AutoSize = true, Anchor = AnchorStyles.Left, Text = sprintf "(%d - %d)" low up)
        addRow [label name :> Control, 1; n :> Control, 1; range :> Control, 1]

    // items: the IDs of the choices and their texts; isCurrent: whether the ID is the setting
    member _.AddComboBox (engText: string, items: (string * string) list, isCurrent: string -> bool, set: string -> unit) =
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
        addRow [label engText :> Control, 1; cb :> Control, 2]

    // A group of its own (one panel): the other radio buttons of the page are not unchecked
    member _.AddRadioButtons (items: (string * string) list, isCurrent: string -> bool, set: string -> unit) =
        let panel = new FlowLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                        FlowDirection = FlowDirection.TopDown, WrapContents = false,
                                        Margin = Padding(16, 0, 0, 0))
        items |> List.iter (fun (id, engText) ->
            let rb = new RadioButton(AutoSize = true)
            rb.CheckedChanged.Add(fun _ -> if rb.Checked then onChange (fun () -> set id))
            register {
                refresh = (fun () -> rb.Checked <- isCurrent id)
                retext = (fun () -> rb.Text <- conv engText) }
            panel.Controls.Add(rb))
        addRow [panel :> Control, 3]

// The dialog: the properties at the top (AddPropertiesComboBox, AddPropertiesButton), the pages
// (AddPage) with their list on the left, and Close. Closing hides it: it is made once, and shown
// again (Show).
type SettingsDialog(conv: string -> string) =
    let bindings = ResizeArray<Binding>()
    let mutable refreshing = false
    let mutable changed: unit -> unit = (fun () -> ())

    let form = new Form()
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
    let bottom = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                      ColumnCount = 2, Anchor = (AnchorStyles.Left ||| AnchorStyles.Right))
    let closeButton = new Button(AutoSize = true, Anchor = AnchorStyles.Right)

    let register (b: Binding) = bindings.Add b

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
        // change with the page), as high as the list, and as wide as the properties above
        let mutable w = 0
        let mutable h = 0
        for page in pages do
            let size = page.GetPreferredSize(Size.Empty)
            w <- max w size.Width
            h <- max h size.Height

        let navSize = nav.GetPreferredSize(Size.Empty)
        let besideNav =
            top.GetPreferredSize(Size.Empty).Width + top.Margin.Horizontal
            - (navSize.Width + nav.Margin.Horizontal) - content.Margin.Horizontal
        content.Size <- Size(max (w + content.Padding.Horizontal) besideNav,
                             max (h + content.Padding.Vertical) navSize.Height)

    let retext () =
        withRefreshing (fun () ->
            form.Text <- sprintf "%s - %s" AppDef.PROGRAM_NAME_NET (conv "Settings")
            for b in bindings do b.retext()
            for b in bindings do b.refresh())
        // The sizes of the texts: after the first show (Load), when the handles have been created
        if form.IsHandleCreated then fitPages()

    do
        form.SuspendLayout()
        // Built at 96 DPI: scaled to the DPI of the display when it is loaded (and when it moves to
        // another display, see Program.main: PerMonitorV2)
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

        bottom.ColumnStyles.Add(ColumnStyle(SizeType.Percent, 100.0f)) |> ignore
        bottom.ColumnStyles.Add(ColumnStyle(SizeType.AutoSize)) |> ignore
        bottom.Controls.Add(hint, 0, 0)
        bottom.Controls.Add(closeButton, 1, 0)

        middle.Controls.Add(nav, 0, 0)
        middle.Controls.Add(content, 1, 0)

        root.Controls.Add(top, 0, 0)
        root.Controls.Add(middle, 0, 1)
        root.Controls.Add(bottom, 0, 2)
        form.Controls.Add(root)

        form.Load.Add(fun _ -> fitPages())
        // After the controls have been scaled to the new DPI: the texts are measured again
        form.DpiChanged.Add(fun _ -> form.BeginInvoke(Action(retext)) |> ignore)

        form.FormClosing.Add(fun e ->
            if e.CloseReason = CloseReason.UserClosing then
                // A number being entered is set (as leaving its box would), even by Esc: the
                // changes are not canceled
                form.Validate() |> ignore
                e.Cancel <- true
                form.Hide())

        form.ResumeLayout(false)

    // Called after a setting has been changed in the dialog (not after a properties switch)
    member _.Changed with set (f: unit -> unit) = changed <- f

    member _.AddPage (engText: string) =
        let index = pages.Count
        // Top, not Fill: the rows would be spread over the height of the largest page
        let table = new TableLayoutPanel(AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                         ColumnCount = 3, Dock = DockStyle.Top, Visible = false)
        content.Controls.Add(table)
        pages.Add(table)

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

        SettingsPage(conv, table, register, (fun () -> refreshing), (fun () -> changed ()))

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
            selectCurrent ()

        cb.DropDown.Add(fun _ -> withRefreshing reload)
        cb.SelectedIndexChanged.Add(fun _ ->
            if not refreshing && cb.SelectedIndex >= 0 then switchTo ids.[cb.SelectedIndex])
        register {
            // Read again only for a name not in the list (e.g. added): see getItems
            refresh = (fun () -> if Array.exists isCurrent ids then selectCurrent () else reload ())
            retext = reload }
        top.Controls.Add(cb)

    member _.AddPropertiesButton (engText: string, onClick: unit -> unit, ?enabled: unit -> bool) =
        let b = new Button(AutoSize = true, Anchor = AnchorStyles.Left)
        b.Click.Add(fun _ -> onClick ())
        register {
            refresh = (fun () -> enabled |> Option.iter (fun f -> b.Enabled <- f()))
            retext = (fun () -> b.Text <- conv engText) }
        top.Controls.Add(b)

    // Shows the settings again (e.g. after a properties load, or a change in the menu)
    member _.RefreshValues () =
        refresh ()

    // The language has changed. Later: maybe called from the handler of a control that retext
    // changes (the language ComboBox)
    member _.RetextLater () =
        if form.IsHandleCreated then form.BeginInvoke(Action(retext)) |> ignore

    member _.Show () =
        if not form.Visible then
            // The settings may have been changed (e.g. Reload) or the language since it was shown
            retext ()
            form.Show()

        form.Activate()
