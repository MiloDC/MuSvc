namespace miloonline.net.JobActor

open System
open System.Net.Sockets

type ActorResult =
    | Output of string
    | Error of string

type Actor internal (port, fn) =
    let ipAddr =
        use socket = new Socket (AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        socket.Connect ("8.8.8.8", 65530)
        (socket.LocalEndPoint :?> System.Net.IPEndPoint).Address
    let listener = TcpListener (ipAddr, port)

    do
        listener.Server.NoDelay <- true
        listener.Start ()

    member val IpAddress = string ipAddr with get
    member val Port = port with get
    member val internal Listener = listener with get
    [<DefaultValue>]val mutable internal Mailbox : MailboxProcessor<ActorMsg>
    member val internal Fn = fn with get
    member val internal Clients = ResizeArray<Client> () with get
    member val internal CancelSrc = new Threading.CancellationTokenSource () with get

[<RequireQualifiedAccess>]
module Actor =
    let private sendResultAsync client result =
        async {
            let text = match result with Output o -> o | Error e -> $"? {e}"
            let bytes = sprintf "%s%s" text Terminator |> Text.Encoding.UTF8.GetBytes
            try
                do! client.tcp.GetStream().AsyncWrite (bytes, 0, bytes.Length)
            with
            | _ -> ()
        }

    let rec private actorLoopAsync (actor: Actor) : Async<unit> =
        async {
            try
                match! actor.Mailbox.Receive 125 with
                | ProcessInput (client, item) ->
                    do!
                        async { do! actor.Fn item |> sendResultAsync client }
                        |> Async.StartChild |> Async.Ignore
                | ClientCount client ->
                    do!
                        async {
                            do!
                                lock actor.Clients (fun _ -> actor.Clients.Count)
                                |> string |> Output |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | RemoveClient client ->
                    lock actor.Clients (fun _ -> actor.Clients.Remove client) |> ignore
                    client.tcp.Close ()
            with
            | :? TimeoutException -> ()

            if actor.Listener.Pending () then
                let! tcpClient =
                    Async.FromBeginEnd (
                        actor.Listener.BeginAcceptTcpClient, actor.Listener.EndAcceptTcpClient )
                let client =
                    {
                        tcp = tcpClient
                        buffer = Array.zeroCreate 8192
                    }
                lock actor.Clients (fun () -> actor.Clients.Add client)
                do!
                    async {
                        do!
                            $"{Terminator}Connected to actor @ \
                                {actor.IpAddress}:{actor.Port}{Terminator}"
                            |> Output |> sendResultAsync client
                        do!
                            client
                            |> Client.clientLoopAsync actor.Mailbox (System.Text.StringBuilder ())
                    }
                    |> Async.StartChild |> Async.Ignore

            return! actorLoopAsync actor
        }

    let create ``function`` port =
        let actor = Actor (port, ``function``)

        MailboxProcessor.Start (
            fun inBox ->
                actor.Mailbox <- inBox
                (
                    actorLoopAsync actor,
                    fun _ ->
                        // This sequence executes when actorLoopAsync is cancelled by calling
                        // the Cancel() method of the actor's CancelSrc property.
                        actor.Listener.Stop ()
                        try
                            actor.Listener.Server.Shutdown SocketShutdown.Both
                        with
                        | :? SocketException -> ()

                        actor.Listener.Server.Close ()
                        lock actor.Clients (fun () ->
                            actor.Clients |> Seq.iter (fun c -> c.tcp.Close ()))
                )
                |> Async.TryCancelled
            , actor.CancelSrc.Token)
        |> ignore

        printfn $"Actor started at {actor.IpAddress}:{actor.Port}"
        actor

    let shutdown (actor: Actor) =
        actor.CancelSrc.Cancel ()
        printfn ""
        printfn $"Actor at {actor.IpAddress}:{actor.Port} shut down."
