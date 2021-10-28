namespace net.miloonline.MuSvc

open System

type internal Client =
    {
        tcp : Net.Sockets.TcpClient
        buf : byte array
    }

type internal ClientMsg =
    | Input of Client * byte array
    | ClientCount of Client
    | RemoveClient of Client

[<RequireQualifiedAccess>]
module internal Client =
    let private cmds =
        [|
            "/q", RemoveClient
            "/cc", ClientCount
        |]
        |> Array.map (fun (str, cm) -> Text.Encoding.UTF8.GetBytes str, cm)

    let private isConnected cl =
        let bytes = Array.zeroCreate<byte> 1
        try
            (not <| cl.tcp.Client.Poll (0, Net.Sockets.SelectMode.SelectRead))
            || (0 <> cl.tcp.Client.Receive (bytes, Net.Sockets.SocketFlags.Peek))
        with
        | _ -> false

    let private submitRequest (mb : MailboxProcessor<ClientMsg>) req cl =
        cmds
        |> Seq.tryFind (fun (cmdBytes, _) -> bytesMatch cmdBytes req)
        |> Option.bind (fun (_, msgFn) -> msgFn cl |> Some)
        |> Option.defaultValue (Input (cl, req))
        |> mb.Post

    let rec loopAsync mb (stream : IO.MemoryStream) cl =
        async {
            try
                match! cl.tcp.GetStream().AsyncRead cl.buf with
                | readCount when readCount > 0 ->
                    do! stream.AsyncWrite (cl.buf, 0, readCount)
                    let span = ReadOnlySpan (stream.ToArray ())
                    match span.IndexOf (ReadOnlySpan MsgTerminatorBytes) with
                    | -1 -> return! loopAsync mb stream cl
                    | 0 ->
                        do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
                        return! loopAsync mb (new IO.MemoryStream ()) cl
                    | i ->
                        let req = (span.Slice (0, i)).ToArray ()
                        submitRequest mb req cl
                        if not <| bytesMatch (fst cmds.[0]) req then
                            let s =
                                match i + MsgTerminatorBytes.Length with
                                | trim when span.Length <= trim -> new IO.MemoryStream ()
                                | trim -> new IO.MemoryStream ((span.Slice trim).ToArray ())
                            do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
                            return! loopAsync mb s cl
                | _ ->
                    if isConnected cl then
                        return! loopAsync mb stream cl
                    else
                        submitRequest mb (fst cmds.[0]) cl
            with
                _ -> ()

            do! stream.DisposeAsync().AsTask () |> Async.AwaitTask
        }
