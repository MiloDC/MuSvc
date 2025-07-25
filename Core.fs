namespace net.miloonline.MuSvc

open System
open System.Reflection
open System.Runtime.InteropServices

[<Sealed>]
type Native () =
    static let dllImportResolver
            libraryName (assm: Assembly) (searchPath: Nullable<DllImportSearchPath>) =
        let libName =
            match RuntimeInformation.IsOSPlatform OSPlatform.Windows, libraryName with
            | false, "msvcrt" -> "libc"
            | _ -> libraryName

        let mutable nativeLib = IntPtr.Zero
        if NativeLibrary.TryLoad (libName, &nativeLib) then nativeLib else IntPtr.Zero

    [<DllImport ("msvcrt", EntryPoint = "memcmp", CallingConvention = CallingConvention.Cdecl)>]
    static extern int memcmp' (byte[] bytes1, byte[] bytes2, unativeint count)

    static do
        NativeLibrary.SetDllImportResolver (Assembly.GetExecutingAssembly (), dllImportResolver)

    static member memcmp (lhs: byte array, rhs: byte array, count: uint64) =
        memcmp' (lhs, rhs, unativeint count) |> sign

    static member memcmp (lhs: byte array, rhs: byte array) =
        memcmp' (lhs, rhs, unativeint (min lhs.LongLength rhs.LongLength)) |> sign

[<AutoOpen>]
module Core =
    let internal MsgTerminatorBytes = "\r\n" |> Text.Encoding.UTF8.GetBytes

    let internal bytesMatch (bytes1 : byte array) (bytes2 : byte array) =
        (bytes1.LongLength = bytes2.LongLength)
        && (Native.memcmp (bytes1, bytes2, uint64 bytes1.LongLength) = 0)
