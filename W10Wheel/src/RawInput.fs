module RawInput

open System
open System.Diagnostics
open System.Runtime.InteropServices
open System.Windows.Forms
open System.Threading

open WinAPI.RawInput

let mutable private sendWheelRaw: int -> int -> unit = (fun x y -> ())

let setSendWheelRaw f =
    sendWheelRaw <- f

let private procInput (lParam: nativeint): unit =
    let mutable pcbSize = 0u
    let cbSizeHeader = uint32 (Marshal.SizeOf(typeof<WinAPI.RAWINPUTHEADER>))
    //Debug.WriteLine(sprintf "GetRawInputData: %d, pcbSize: %d" res pcbSize)

    if WinAPI.GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, &pcbSize, cbSizeHeader) = 0u then
        let buf = Marshal.AllocHGlobal(int pcbSize)
        if WinAPI.GetRawInputData(lParam, RID_INPUT, buf, &pcbSize, cbSizeHeader) = pcbSize then
            let rawInput = Marshal.PtrToStructure(buf, typeof<WinAPI.RAWINPUT>) :?> WinAPI.RAWINPUT
            if (rawInput.header.dwType = RIM_TYPEMOUSE) && (rawInput.mouse.usFlags = MOUSE_MOVE_RELATIVE) then
                let x = rawInput.mouse.lLastX
                let y = rawInput.mouse.lLastY
                sendWheelRaw x y

type MessageWindow() =
    inherit NativeWindow()
    do
        let cp = CreateParams()
        cp.Parent <- HWND_MESSAGE
        base.CreateHandle(cp)

    override self.WndProc(m:Message byref): unit =
        if m.Msg = WM_INPUT then
            procInput(m.LParam)
        
        base.WndProc(&m)

let private messageWindow = new MessageWindow()

let private registerMouseRawInputDevice (dwFlags:uint32) (hwnd:nativeint) =
    let rid = [| WinAPI.RAWINPUTDEVICE(HID_USAGE_PAGE_GENERIC, HID_USAGE_GENERIC_MOUSE, dwFlags, hwnd) |]
    let ridSize = uint32 (Marshal.SizeOf(typeof<WinAPI.RAWINPUTDEVICE>))
    WinAPI.RegisterRawInputDevices(rid, 1u, ridSize)

let register () =
    if (registerMouseRawInputDevice RIDEV_INPUTSINK messageWindow.Handle) = false then
        Debug.WriteLine(sprintf "Failed register RawInput: %d" (Marshal.GetLastWin32Error()))
         
let unregister () =
    if (registerMouseRawInputDevice RIDEV_REMOVE IntPtr.Zero) = false then
        Debug.WriteLine(sprintf "Failed unregister RawInput: %d" (Marshal.GetLastWin32Error()))
