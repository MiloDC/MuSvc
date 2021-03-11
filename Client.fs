﻿namespace miloonline.net.JobActor

open System
open System.Text.RegularExpressions

type internal Client =
    {
        tcp     : Net.Sockets.TcpClient
        buffer  : byte []
    }

type internal ActorMsg =
    | ProcessInput of Client * string
    | ClientCount of Client
    | RemoveClient of Client

module internal Client =
    let private isConnected client =
        let bytes = Array.zeroCreate<byte> 1

        try
            (not <| client.tcp.Client.Poll (0, Net.Sockets.SelectMode.SelectRead))
            || (0 <> client.tcp.Client.Receive (bytes, Net.Sockets.SocketFlags.Peek))
        with
        | :? ObjectDisposedException -> false

    let private sendRequest (actor: MailboxProcessor<ActorMsg>) client (request: string) =
        match request with
        | r when r.Equals (Command.ClientCount, StringComparison.OrdinalIgnoreCase) ->
            ClientCount client
        | r when r.Equals (Command.Quit, StringComparison.OrdinalIgnoreCase) ->
            RemoveClient client
        | r -> ProcessInput (client, r)
        |> actor.Post

    let rec clientLoopAsync (actor: MailboxProcessor<ActorMsg>) sb cl =
        async {
            try
                match! cl.tcp.GetStream().AsyncRead (cl.buffer, 0, cl.buffer.Length) with
                | byteCount when byteCount > 0 ->
                    Text.Encoding.UTF8.GetString (cl.buffer, 0, byteCount)
                    |> Printf.bprintf sb "%s"

                    let m = Regex.Match (string sb, $"^.*{Regex.Escape Terminator}")
                    let quit =
                        m.Success
                        && (
                            sb.Remove (0, m.Value.Length) |> ignore

                            m.Value.Split ([| Terminator |], StringSplitOptions.RemoveEmptyEntries)
                            |> Array.exists (fun t ->
                                sendRequest actor cl t
                                t.Equals (Command.Quit, StringComparison.OrdinalIgnoreCase)))

                    if not quit then return! clientLoopAsync actor sb cl
                | _ ->
                    if isConnected cl then
                        return! clientLoopAsync actor sb cl
                    else
                        sendRequest actor cl Command.Quit
            with
                _ -> ()
        }