namespace net.miloonline.MuSvc

open System
open System.Text.RegularExpressions

type internal MuSvcClient =
    {
        tcp     : Net.Sockets.TcpClient
        buffer  : byte []
    }

type internal ClientMsg =
    | ProcessInput of MuSvcClient * string
    | ClientCount of MuSvcClient
    | RemoveClient of MuSvcClient

module internal Client =
    let private isConnected client =
        let bytes = Array.zeroCreate<byte> 1
        try
            (not <| client.tcp.Client.Poll (0, Net.Sockets.SelectMode.SelectRead))
            || (0 <> client.tcp.Client.Receive (bytes, Net.Sockets.SocketFlags.Peek))
        with
        | :? ObjectDisposedException -> false

    let private sendRequest (mb: MailboxProcessor<ClientMsg>) client (request: string) =
        match request with
        | r when r.Equals (Command.ClientCount, StringComparison.OrdinalIgnoreCase) ->
            ClientCount client
        | r when r.Equals (Command.Quit, StringComparison.OrdinalIgnoreCase) ->
            RemoveClient client
        | r -> ProcessInput (client, r)
        |> mb.Post

    let rec clientLoopAsync (mb: MailboxProcessor<ClientMsg>) sb cl =
        async {
            try
                match! cl.tcp.GetStream().AsyncRead (cl.buffer, 0, cl.buffer.Length) with
                | byteCount when byteCount > 0 ->
                    Text.Encoding.UTF8.GetString (cl.buffer, 0, byteCount)
                    |> Printf.bprintf sb "%s"

                    let m = Regex.Match (string sb, $"^.*{Regex.Escape MsgTerminator}")
                    let quit =
                        m.Success
                        && (
                            sb.Remove (0, m.Value.Length) |> ignore
                            m.Value.Split
                                ([| MsgTerminator |], StringSplitOptions.RemoveEmptyEntries)
                            |> Array.exists (fun t ->
                                sendRequest mb cl t
                                t.Equals (Command.Quit, StringComparison.OrdinalIgnoreCase)))

                    if not quit then return! clientLoopAsync mb sb cl
                | _ ->
                    if isConnected cl then
                        return! clientLoopAsync mb sb cl
                    else
                        sendRequest mb cl Command.Quit
            with
                _ -> ()
        }
