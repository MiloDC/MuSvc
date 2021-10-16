[<RequireQualifiedAccess>]
module internal net.miloonline.MuSvc.Client

open System
open System.Net.Sockets

type Msg =
    | ProcessRequest of TcpClient * byte array
    | ClientCount of TcpClient
    | RemoveClient of TcpClient

let private cmdMsgs =
    [
        Command.ClientCount, ClientCount
        Command.Quit, RemoveClient
    ]
let private msgTerminatorBytes = Text.Encoding.UTF8.GetBytes MsgTerminator

let private isConnected (client : TcpClient) =
    let bytes = Array.zeroCreate<byte> 1
    try
        (not <| client.Client.Poll (0, SelectMode.SelectRead))
        || (0 <> client.Client.Receive (bytes, SocketFlags.Peek))
    with
    | _ -> false

let private sendRequest cl (mb : MailboxProcessor<Msg>) (req : byte array) =
    cmdMsgs
    |> Seq.tryFind (fun (cmdBytes, _) -> bytesMatch cmdBytes req)
    |> Option.bind (fun (_, msgFn) -> msgFn cl |> Some)
    |> Option.defaultValue (ProcessRequest (cl, req))
    |> mb.Post

let rec loopAsync (cl : TcpClient) buf mb (stream : IO.MemoryStream) =
    async {
        try
            match! cl.GetStream().AsyncRead (buf, 0, buf.Length) with
            | readCount when readCount > 0 ->
                do! stream.AsyncWrite (buf, 0, readCount)
                let span = ReadOnlySpan (stream.ToArray ())
                match span.IndexOf (ReadOnlySpan msgTerminatorBytes) with
                | -1 -> return! loopAsync cl buf mb stream
                | 0 ->
                    do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
                    return! new IO.MemoryStream () |> loopAsync cl buf mb
                | i ->
                    stream.Dispose ()   // Cannot be done asynchronously before Span operations.
                    let req = (span.Slice (0, i)).ToArray ()
                    sendRequest cl mb req
                    if not <| bytesMatch Command.Quit req then
                        return!
                            match i + msgTerminatorBytes.Length with
                            | trim when span.Length <= trim -> new IO.MemoryStream ()
                            | trim -> new IO.MemoryStream ((span.Slice trim).ToArray ())
                            |> loopAsync cl buf mb
            | _ ->
                if isConnected cl then
                    return! loopAsync cl buf mb stream
                else
                    sendRequest cl mb Command.Quit
        with
            _ -> ()
    }
