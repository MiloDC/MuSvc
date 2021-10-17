namespace net.miloonline.MuSvc

open System
open System.Net.Sockets

type internal Client =
    {
        tcp : Net.Sockets.TcpClient
        buf : byte array
    }

type internal ClientMsg =
    | ProcessRequest of Client * byte array
    | ClientCount of Client
    | RemoveClient of Client

[<RequireQualifiedAccess>]
module internal Client =
    let private cmdMsgs =
        [
            Command.ClientCount, ClientCount
            Command.Quit, RemoveClient
        ]

    let private isConnected cl =
        let bytes = Array.zeroCreate<byte> 1
        try
            (not <| cl.tcp.Client.Poll (0, SelectMode.SelectRead))
            || (0 <> cl.tcp.Client.Receive (bytes, SocketFlags.Peek))
        with
        | _ -> false

    let private sendRequest cl (mb : MailboxProcessor<ClientMsg>) req =
        cmdMsgs
        |> Seq.tryFind (fun (cmdBytes, _) -> bytesMatch cmdBytes req)
        |> Option.bind (fun (_, msgFn) -> msgFn cl |> Some)
        |> Option.defaultValue (ProcessRequest (cl, req))
        |> mb.Post

    let rec loopAsync cl mb (stream : IO.MemoryStream) =
        async {
            try
                match! cl.tcp.GetStream().AsyncRead cl.buf with
                | readCount when readCount > 0 ->
                    do! stream.AsyncWrite (cl.buf, 0, readCount)
                    let span = ReadOnlySpan (stream.ToArray ())
                    match span.IndexOf (ReadOnlySpan MsgTerminatorBytes) with
                    | -1 -> return! loopAsync cl mb stream
                    | 0 ->
                        do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
                        return! new IO.MemoryStream () |> loopAsync cl mb
                    | i ->
                        let req = (span.Slice (0, i)).ToArray ()
                        sendRequest cl mb req
                        if not <| bytesMatch Command.Quit req then
                            let s =
                                match i + MsgTerminatorBytes.Length with
                                | trim when span.Length <= trim -> new IO.MemoryStream ()
                                | trim -> new IO.MemoryStream ((span.Slice trim).ToArray ())
                            do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
                            return! loopAsync cl mb s
                | _ ->
                    if isConnected cl then
                        return! loopAsync cl mb stream
                    else
                        sendRequest cl mb Command.Quit
            with
                _ -> ()

            do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
        }
