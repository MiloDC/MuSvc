namespace net.miloonline.MuSvc

open System
open System.Net.Sockets

type MuSvcResult =
    | Output of string
    | Error of string

type MuSvc internal (port, fn) =
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
    [<DefaultValue>]val mutable internal Mailbox : MailboxProcessor<ClientMsg>
    member val internal Fn = fn with get
    member val internal Connections = ResizeArray<MuSvcClient> () with get
    member val internal CancelSrc = new Threading.CancellationTokenSource () with get

[<RequireQualifiedAccess>]
module MuSvc =
    let private sendResultAsync client result =
        async {
            let text = match result with Output o -> o | Error e -> $"? {e}"
            let bytes = sprintf "%s%s" text MsgTerminator |> Text.Encoding.UTF8.GetBytes
            try
                do! client.tcp.GetStream().AsyncWrite (bytes, 0, bytes.Length)
            with
            | _ -> ()
        }

    let rec private muSvcLoopAsync (m: MuSvc) : Async<unit> =
        async {
            try
                match! m.Mailbox.Receive 125 with
                | ProcessInput (client, bytes) ->
                    if bytes.Length > 0 then
                        do!
                            async { do! m.Fn bytes |> sendResultAsync client }
                            |> Async.StartChild |> Async.Ignore
                | ClientCount client ->
                    do!
                        async {
                            do!
                                lock m.Connections (fun () -> m.Connections.Count)
                                |> string
                                |> Output
                                |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | RemoveClient client ->
                    lock m.Connections (fun () -> m.Connections.Remove client) |> ignore
                    client.tcp.Close ()
            with
            | :? TimeoutException -> ()

            if m.Listener.Pending () then
                let! tcpClient =
                    Async.FromBeginEnd (
                        m.Listener.BeginAcceptTcpClient, m.Listener.EndAcceptTcpClient )
                let client =
                    {
                        tcp = tcpClient
                        buffer = Array.zeroCreate 8192
                    }
                lock m.Connections (fun () -> m.Connections.Add client)
                do!
                    async {
                        do!
                            $"{MsgTerminator}Connected to microservice @ \
                                {m.IpAddress}:{m.Port}{MsgTerminator}"
                            |> Output
                            |> sendResultAsync client
                        do!
                            Client.clientLoopAsync m.Mailbox (new IO.MemoryStream ()) client
                    }
                    |> Async.StartChild |> Async.Ignore

            return! muSvcLoopAsync m
        }

    let create ``function`` port =
        let m = MuSvc (port, ``function``)
        MailboxProcessor.Start (
            fun inBox ->
                m.Mailbox <- inBox
                (
                    muSvcLoopAsync m,
                    fun _ ->
                        // This sequence executes when muSvcLoopAsync is cancelled by
                        // calling the Cancel() method of the microservice's CancelSrc property.
                        m.Listener.Stop ()
                        try
                            m.Listener.Server.Shutdown SocketShutdown.Both
                        with
                        | :? SocketException -> ()

                        m.Listener.Server.Close ()
                        lock m.Connections (fun () ->
                            m.Connections |> Seq.iter (fun c -> c.tcp.Close ()))
                )
                |> Async.TryCancelled
            , m.CancelSrc.Token)
        |> ignore

        printfn $"Microservice started at {m.IpAddress}:{m.Port}"
        m

    let shutdown (m: MuSvc) =
        if not m.CancelSrc.IsCancellationRequested then
            m.CancelSrc.Cancel ()
            printfn $"Shutdown request sent to microservice at {m.IpAddress}:{m.Port}."
