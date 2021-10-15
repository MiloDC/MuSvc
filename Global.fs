[<AutoOpen>]
module net.miloonline.MuSvc.Global

open System.Runtime.InteropServices

let [<Literal>] MsgTerminator = "\r\n"

[<DllImport ("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)>]
extern int memcmp (byte[] bytes1, byte[] bytes2, int64 count)

[<RequireQualifiedAccess>]
module Command =
    let ClientCount = "/cc" |> System.Text.Encoding.UTF8.GetBytes
    let Quit = "/q" |> System.Text.Encoding.UTF8.GetBytes
