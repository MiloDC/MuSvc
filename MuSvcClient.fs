namespace net.miloonline.MuSvc

open System

type internal MuSvcClient =
    {
        tcp     : Net.Sockets.TcpClient
        buffer  : byte array
    }

type internal ClientMsg =
    | ProcessRequest of MuSvcClient * byte array
    | ClientCount of MuSvcClient
    | RemoveClient of MuSvcClient

module internal Client =
    let private cmdMsgs =
        [
            Command.ClientCount, ClientCount
            Command.Quit, RemoveClient
        ]
    let private msgTerminatorBytes = Text.Encoding.UTF8.GetBytes MsgTerminator

    let private isConnected client =
        let bytes = Array.zeroCreate<byte> 1
        try
            (not <| client.tcp.Client.Poll (0, Net.Sockets.SelectMode.SelectRead))
            || (0 <> client.tcp.Client.Receive (bytes, Net.Sockets.SocketFlags.Peek))
        with
        | _ -> false

    let private sendRequest (mb : MailboxProcessor<ClientMsg>) req cl =
        cmdMsgs
        |> Seq.tryFind (fun (cmdBytes, _) -> 0 = memcmp (cmdBytes, req, req.LongLength))
        |> Option.bind (fun (_, msgFn) -> msgFn cl |> Some)
        |> Option.defaultValue (ProcessRequest (cl, req))
        |> mb.Post

    let rec clientLoopAsync mb (stream : IO.MemoryStream) cl =
        async {
            try
                match! cl.tcp.GetStream().AsyncRead (cl.buffer, 0, cl.buffer.Length) with
                | byteCount when byteCount > 0 ->
                    do! stream.AsyncWrite (cl.buffer, 0, byteCount)
                    let span = ReadOnlySpan (stream.ToArray ())
                    let mtSpan = ReadOnlySpan msgTerminatorBytes
                    match span.IndexOf mtSpan with
                    | -1 -> return! clientLoopAsync mb stream cl
                    | 0 ->
                        stream.Dispose ()
                        return! clientLoopAsync mb (new IO.MemoryStream ()) cl
                    | i ->
                        stream.Dispose ()
                        let req = (span.Slice (0, i)).ToArray ()
                        sendRequest mb req cl
                        if memcmp (req, Command.Quit, req.LongLength) <> 0 then
                            let s =
                                match i + mtSpan.Length with
                                | trim when trim >= span.Length -> new IO.MemoryStream ()
                                | trim -> new IO.MemoryStream ((span.Slice trim).ToArray ())
                            return! clientLoopAsync mb s cl
                | _ ->
                    if isConnected cl then
                        return! clientLoopAsync mb stream cl
                    else
                        sendRequest mb Command.Quit cl
            with
                _ -> ()
        }
