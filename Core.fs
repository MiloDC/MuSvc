namespace net.miloonline.MuSvc

type MuSvcResult =
    | Bytes of byte array
    | Text of string
    | Error of string

type IMuSvcProcessor =
    abstract member ProcessInput : byte array -> MuSvcResult

[<AutoOpen>]
module Core =
    open System.Text
    open System.Runtime.InteropServices

    [<RequireQualifiedAccess>]
    module private Native =
        [<DllImport ("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)>]
        extern int memcmp (byte[] bytes1, byte[] bytes2, int64 count)

    let internal MsgTerminatorBytes = "\r\n" |> Encoding.UTF8.GetBytes

    let internal bytesMatch (bytes1 : byte array) (bytes2 : byte array) =
        (bytes1.LongLength = bytes2.LongLength)
        && (0 = Native.memcmp (bytes1, bytes2, bytes1.LongLength))

    [<RequireQualifiedAccess>]
    module internal Command =
        let ClientCount = "/cc" |> Encoding.UTF8.GetBytes
        let Quit = "/q" |> Encoding.UTF8.GetBytes
