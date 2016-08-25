namespace TestHook.AssemblyInfo

(*
 * Copyright (c) 2016 Yuki Ono
 * Licensed under the MIT License.
 *)

open System.Reflection
open System.Runtime.CompilerServices
open System.Runtime.InteropServices

// アセンブリに関する一般情報は、以下の属性セットによって
// 制御されます。アセンブリに関連付けられている情報を変更するには、
// これらの属性値を変更します。
[<assembly: AssemblyTitle("W10Wheel.NET")>]
[<assembly: AssemblyDescription("Mouse Wheel Simulator")>]
[<assembly: AssemblyConfiguration("")>]
[<assembly: AssemblyCompany("")>]
[<assembly: AssemblyProduct("W10Wheel.NET")>]
[<assembly: AssemblyCopyright("Copyright (c) 2016 Yuki Ono")>]
[<assembly: AssemblyTrademark("")>]
[<assembly: AssemblyCulture("")>]

// ComVisible を false に設定すると、COM コンポーネントがこのアセンブリ内のその型を認識
// できなくなります。COM からこのアセンブリ内の型にアクセスする必要がある場合は、
// その型の ComVisible 属性を true に設定します。
[<assembly: ComVisible(false)>]

// このプロジェクトが COM に公開される場合、次の GUID がタイプ ライブラリの ID になります
[<assembly: Guid("f7db64d0-fad4-4547-aa0a-09e24bb010ef")>]

// アセンブリのバージョン情報は、以下の 4 つの値で構成されます。:
// 
//       メジャー バージョン
//       マイナー バージョン 
//       ビルド番号
//       リビジョン
// 
// すべての値を指定するか、下に示すように '*' を使用してビルドおよびリビジョン番号を
// 既定値にすることができます。:
// [<assembly: AssemblyVersion("1.0.*")>]
[<assembly: AssemblyVersion("2.0.5.0")>]
[<assembly: AssemblyFileVersion("2.0.5.0")>]

do
    ()