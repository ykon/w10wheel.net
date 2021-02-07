module WinAPI

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

#nowarn "9"

open System
open System.Reflection
open System.Runtime.InteropServices

// https://msdn.microsoft.com/library/windows/desktop/dd162805.aspx 
// http://www.pinvoke.net/default.aspx/Structures/POINT.html
[<Struct; StructLayout(LayoutKind.Sequential)>]
type POINT =
    val x: int32
    val y: int32

    new (_x, _y) = { x = _x; y = _y}

// https://msdn.microsoft.com/library/windows/desktop/ms644970.aspx
// http://www.pinvoke.net/default.aspx/Structures/MSLLHOOKSTRUCT.html
[<StructLayout(LayoutKind.Sequential)>]
type MSLLHOOKSTRUCT =
    val pt          : POINT
    val mouseData   : uint32
    val flags       : uint32
    val time        : uint32
    val dwExtraInfo : unativeint

// https://msdn.microsoft.com/library/windows/desktop/ms644967.aspx
// http://pinvoke.net/default.aspx/Structures/KBDLLHOOKSTRUCT.html
[<StructLayout(LayoutKind.Sequential)>]
type KBDLLHOOKSTRUCT =
    val vkCode      : uint32
    val scanCode    : uint32
    val flags       : uint32
    val time        : uint32
    val dwExtraInfo : unativeint

// http://stackoverflow.com/questions/4177850/how-to-simulate-mouse-clicks-and-keypresses-in-f
// https://msdn.microsoft.com/library/windows/desktop/ms646273.aspx
// http://pinvoke.net/default.aspx/Structures/MOUSEINPUT.html
[<Struct; StructLayout(LayoutKind.Sequential)>]
type MOUSEINPUT =
    val dx          : int32
    val dy          : int32
    val mouseData   : uint32
    val dwFlags     : uint32
    val time        : uint32
    val dwExtraInfo : unativeint

    new(x, y, data, flags, _time, info) = {dx = x; dy = y; mouseData = data; dwFlags = flags; time = _time; dwExtraInfo = info}

// https://msdn.microsoft.com/library/ms646271.aspx
// http://pinvoke.net/default.aspx/Structures/KEYBDINPUT.html
[<Struct; StructLayout(LayoutKind.Sequential)>]
type KEYBDINPUT =
    val wVk         : int16
    val wScan       : int16
    val dwFlags     : uint32
    val time        : uint32
    val dwExtraInfo : unativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type HARDWAREINPUT =
    val uMsg    : int32
    val wParamL : int16
    val wParamH : int16

[<Struct; StructLayout(LayoutKind.Sequential)>]
type MINPUT =
    val ``type`` : uint32
    val mi        : MOUSEINPUT

    new (_mi) = {``type`` = 0u; mi = _mi}

module Message =
    [<Literal>]
    let WM_KEYDOWN = 0x0100
    [<Literal>]
    let WM_KEYUP = 0x0101
    [<Literal>]
    let WM_SYSKEYDOWN = 0x0104
    [<Literal>]
    let WM_SYSKEYUP = 0x0105
    [<Literal>]
    let WM_MOUSEMOVE = 0x0200
    [<Literal>]
    let WM_LBUTTONDOWN = 0x0201
    [<Literal>]
    let WM_LBUTTONUP = 0x0202
    [<Literal>]
    let WM_LBUTTONDBLCLK = 0x0203
    [<Literal>]
    let WM_RBUTTONDOWN = 0x0204
    [<Literal>]
    let WM_RBUTTONUP = 0x0205
    [<Literal>]
    let WM_RBUTTONDBLCLK = 0x0206
    [<Literal>]
    let WM_MBUTTONDOWN = 0x207
    [<Literal>]
    let WM_MBUTTONUP = 0x208
    [<Literal>]
    let WM_MBUTTONDBLCLK = 0x0209
    [<Literal>]
    let WM_MOUSEWHEEL = 0x020A
    [<Literal>]
    let WM_XBUTTONDOWN = 0x020B
    [<Literal>]
    let WM_XBUTTONUP = 0x020C
    [<Literal>]
    let WM_XBUTTONDBLCLK = 0x020D
    [<Literal>]
    let WM_MOUSEHWHEEL = 0x020E

let WH_KEYBOARD_LL = 13
let WH_MOUSE_LL = 14
let WHEEL_DELTA = 120

// high-order
let XBUTTON1 = 0x0001
let XBUTTON2 = 0x0002

module Event =
    let MOUSEEVENTF_ABSOLUTE = 0x8000
    let MOUSEEVENTF_HWHEEL = 0x01000
    let MOUSEEVENTF_MOVE = 0x0001
    let MOUSEEVENTF_LEFTDOWN = 0x0002
    let MOUSEEVENTF_LEFTUP = 0x0004
    let MOUSEEVENTF_RIGHTDOWN = 0x0008
    let MOUSEEVENTF_RIGHTUP = 0x0010
    let MOUSEEVENTF_MIDDLEDOWN = 0x0020
    let MOUSEEVENTF_MIDDLEUP = 0x0040
    let MOUSEEVENTF_WHEEL = 0x0800
    let MOUSEEVENTF_XDOWN = 0x0080
    let MOUSEEVENTF_XUP = 0x0100

// https://msdn.microsoft.com/library/windows/desktop/ms644986.aspx
// http://www.pinvoke.net/default.aspx/Delegates/LowLevelMouseProc.html
type LowLevelMouseProc = delegate of nCode: int * wParam: nativeint * [<In>]lParam: MSLLHOOKSTRUCT -> nativeint

// https://msdn.microsoft.com/library/windows/desktop/ms644985.aspx
// http://pinvoke.net/default.aspx/Delegates/LowLevelKeyboardProc.html
type LowLevelKeyboardProc = delegate of nCode: int * wParam: nativeint * [<In>]lParam: KBDLLHOOKSTRUCT -> nativeint

[<DllImport("kernel32.dll")>]
extern uint32 GetCurrentThreadId()

// http://www.pinvoke.net/default.aspx/kernel32/GetModuleHandle.html
[<DllImport("kernel32.dll", SetLastError = true)>]
extern nativeint GetModuleHandle(string lpModuleName)

// http://www.pinvoke.net/default.aspx/user32/UnhookWindowsHookEx.html
// https://msdn.microsoft.com/library/windows/desktop/ms644993.aspx
[<DllImport("user32.dll", SetLastError = true)>]
extern bool UnhookWindowsHookEx(nativeint hhk)

// https://msdn.microsoft.com/library/windows/desktop/ms644974.aspx
// http://www.pinvoke.net/default.aspx/user32.callnexthookex
[<DllImport("user32.dll", EntryPoint = "CallNextHookEx", SetLastError = true)>]
extern nativeint CallNextHookExM(nativeint hhk, int32 nCode, nativeint wParam, [<In>]MSLLHOOKSTRUCT lParam)

[<DllImport("user32.dll", EntryPoint = "CallNextHookEx", SetLastError = true)>]
extern nativeint CallNextHookExK(nativeint hhk, int32 nCode, nativeint wParam, [<In>]KBDLLHOOKSTRUCT lParam)

// http://pinvoke.net/default.aspx/user32/SetWindowsHookEx.html
// https://msdn.microsoft.com/library/windows/desktop/ms644990.aspx
[<DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", SetLastError = true)>]
extern nativeint SetWindowsHookExM(int idHook, LowLevelMouseProc proc, nativeint hMod, uint32 dwThreadId)

[<DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", SetLastError = true)>]
extern nativeint SetWindowsHookExK(int idHook, LowLevelKeyboardProc proc, nativeint hMod, uint32 dwThreadId)


// https://msdn.microsoft.com/library/windows/desktop/ms646310.aspx
// http://pinvoke.net/default.aspx/user32/SendInput.html
[<DllImport("user32.dll", SetLastError = true)>]
extern uint32 SendInput(uint32 nInputs,
    [<MarshalAs(UnmanagedType.LPArray); In>]MINPUT[] pInputs, int cbSize)

[<DllImport("user32.dll", SetLastError = true)>]
extern void mouse_event(int32 dwFlags, int32 dx, int32 dy, int32 dwData, unativeint dwExtraInfo)

[<DllImport("user32.dll", SetLastError = false)>]
extern nativeint GetMessageExtraInfo()

module VKey =
    let VK_SHIFT = 0x10
    let VK_CONTROL = 0x11
    let VK_MENU = 0x12
    let VK_ESCAPE = 0x1B

// https://msdn.microsoft.com/ibrary/windows/desktop/ms646293.aspx
// http://www.pinvoke.net/default.aspx/user32.getasynckeystate
[<DllImport("user32.dll")>]
extern int16 GetAsyncKeyState(int32 vKey)

module CursorID =
    let OCR_NORMAL = 32512
    let OCR_IBEAM = 32513
    let OCR_HAND = 32649
    let OCR_SIZEALL = 32646
    let OCR_SIZENESW = 32643
    let OCR_SIZENS = 32645
    let OCR_SIZENWSE = 32642
    let OCR_SIZEWE = 32644
    let OCR_WAIT = 32514

// https://msdn.microsoft.com/library/windows/desktop/ms648391.aspx
// http://www.pinvoke.net/default.aspx/user32.loadcursor
[<DllImport("user32.dll")>]
extern nativeint LoadCursor(nativeint hInstance, nativeint lpCursorName)

let IMAGE_CURSOR = 2
let LR_DEFAULTSIZE = 0x00000040
let LR_SHARED = 0x00008000

// https://msdn.microsoft.com/library/windows/desktop/ms648045.aspx
// http://www.pinvoke.net/default.aspx/user32/LoadImage.html
[<DllImport("user32.dll", SetLastError = true)>]
extern nativeint LoadImage(nativeint hinst, nativeint lpszName, uint32 uType, int32 cxDesired, int32 cyDesired, uint32 fuLoad)

// https://msdn.microsoft.com/library/windows/desktop/ms648058.aspx
// http://www.pinvoke.net/default.aspx/user32.copyicon
[<DllImport("user32.dll")>]
extern nativeint CopyIcon(nativeint hIcon)

// https://msdn.microsoft.com/library/windows/desktop/ms648395.aspx
// http://www.pinvoke.net/default.aspx/user32/SetSystemCursor.html
[<DllImport("user32.dll")>]
extern bool SetSystemCursor(nativeint hCur, uint32 id)

let SPI_SETCURSORS = 0x0057

// https://msdn.microsoft.com/library/windows/desktop/ms724947.aspx
// http://www.pinvoke.net/default.aspx/user32.systemparametersinfo
[<DllImport("user32.dll", SetLastError = true)>]
extern bool SystemParametersInfo(uint32 uiAction, uint32 uiParam, nativeint pvParam, uint32 fWinIni)

module RawInput =
    let WM_INPUT = 0x00ff
    let HWND_MESSAGE = IntPtr(-3)
    let RID_INPUT = 0x10000003u
    let MOUSE_MOVE_RELATIVE = 0us
    let RIM_TYPEMOUSE = 0u
    let HID_USAGE_PAGE_GENERIC = 0x01us
    let HID_USAGE_GENERIC_MOUSE = 0x02us
    let RIDEV_INPUTSINK = 0x00000100u
    let RIDEV_REMOVE = 0x00000001u

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RAWINPUTDEVICE =
    val usUsagePage : uint16
    val usUsage     : uint16
    val dwFlags     : uint32
    val hwndTarget  : nativeint

    new(usup, usu, dwf, ht) = {usUsagePage = usup; usUsage = usu; dwFlags = dwf; hwndTarget = ht}

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RAWINPUTHEADER =
    val dwType  : uint32
    val dwSize  : uint32
    val hDevice : nativeint
    val wParam  : nativeint

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RAWMOUSE =
    val usFlags            : uint16
    val usButtonFlags      : uint16
    val usButtonData       : uint16
    val ulRawButtons       : uint32
    val lLastX             : int
    val lLastY             : int
    val ulExtraInformation : uint32

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RAWINPUT =
    val header : RAWINPUTHEADER
    val mouse  : RAWMOUSE

[<DllImport("user32.dll", SetLastError = true)>]
extern bool RegisterRawInputDevices([<MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0s); In>]RAWINPUTDEVICE[] pRawInputDevices, [<In>]uint32 uiNumDevices, [<In>]uint32 cbSize)

[<DllImport("user32.dll", SetLastError = true)>]
extern uint32 GetRawInputData([<In>]nativeint hRawInput, [<In>]uint32 uiCommand, [<Out>]nativeint pData, [<In; Out>]uint32& pcbSize, [<In>]uint32 cbSizeHeader)

[<DllImport("user32.dll", SetLastError = true)>]
extern nativeint WindowFromPoint(POINT point)

[<DllImport("user32.dll", SetLastError = true)>]
extern uint32 GetWindowThreadProcessId(nativeint hWnd, [<Out>]uint32& lpdwProcessId)

[<DllImport("kernel32.dll")>]
extern nativeint OpenProcess(uint32 dwDesiredAccess, bool bInheritHandle, uint32 dwProcessId)

[<DllImport("kernel32.dll")>]
extern bool CloseHandle(nativeint handle)

[<DllImport("psapi.dll", CharSet = CharSet.Ansi)>]
extern uint32 GetModuleBaseName(nativeint hWnd, nativeint hModule, [<MarshalAs(UnmanagedType.LPStr); Out>] System.Text.StringBuilder lpBaseName, uint32 nSize)

[<DllImport("psapi.dll")>]
extern uint32 GetModuleFileNameEx(nativeint hProcess, nativeint hModule, [<Out>] System.Text.StringBuilder lpBaseName, [<In>] [<MarshalAs(UnmanagedType.U4)>] int32 nSize)

[<DllImport("kernel32.dll", SetLastError=true)>]
extern bool QueryFullProcessImageName([<In>]nativeint hProcess, [<In>]int32 dwFlags, [<Out>]System.Text.StringBuilder lpExeName, int32& lpdwSize)

[<DllImport("user32.dll")>]
extern nativeint GetForegroundWindow()

[<DllImport("user32.dll", SetLastError = true)>]
extern [<MarshalAs(UnmanagedType.Bool)>] bool GetCursorPos(POINT& lpPoint);
