namespace net.miloonline.MuSvc

open System
open System.Net.Sockets

type MuSvcResult =
    | Bytes of byte array
    | Text of string
    | Error of string

type MuSvc internal (processInput, port) =
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
    member val internal ProcessInput = processInput with get
    member val internal Listener = listener with get
    [<DefaultValue>]val mutable internal Mailbox : MailboxProcessor<ClientMsg>
    member val internal Clients = ResizeArray<Client> () with get
    member val internal CancelSrc = new Threading.CancellationTokenSource () with get

[<RequireQualifiedAccess>]
module MuSvc =
    let private sendResultAsync client result =
        async {
            try
                do!
                    MsgTerminatorBytes
                    |> Array.append (
                        match result with
                        | Bytes b -> b
                        | Text t -> Text.Encoding.UTF8.GetBytes t
                        | Error e -> $"? {e} ?" |> Text.Encoding.UTF8.GetBytes)
                    |> client.tcp.GetStream().AsyncWrite
            with
            | _ -> ()
        }

    let rec internal loopAsync (m : MuSvc) : Async<unit> =
        async {
            try
                match! m.Mailbox.Receive 50 with
                | Input (client, bytes) ->
                    do!
                        async {
                            do! m.ProcessInput bytes |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | ClientCount client ->
                    do!
                        async {
                            do!
                                lock m.Clients (fun () -> m.Clients.Count)
                                |> string |> Text
                                |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | RemoveClient client ->
                    client.tcp.Close ()
                    lock m.Clients (fun () -> m.Clients.Remove client) |> ignore
            with
            | :? TimeoutException -> ()

            if m.Listener.Pending () then
                let! tcpClient =
                    Async.FromBeginEnd (
                        m.Listener.BeginAcceptTcpClient, m.Listener.EndAcceptTcpClient )
                let client = { tcp = tcpClient; buf = Array.zeroCreate 8192 }
                lock m.Clients (fun () -> m.Clients.Add client)
                do!
                    async {
                        do!
                            $"Connected to microservice @ {m.IpAddress}:{m.Port}"
                            |> Text
                            |> sendResultAsync client
                        do! Client.loopAsync m.Mailbox (new IO.MemoryStream ()) client
                    }
                    |> Async.StartChild |> Async.Ignore

            return! loopAsync m
        }

    let create (processInput : byte array -> MuSvcResult) port =
        let m = MuSvc (processInput, port)
        MailboxProcessor.Start (
            fun inBox ->
                m.Mailbox <- inBox
                (
                    loopAsync m,
                    fun _ ->
                        // This sequence executes when muSvcLoopAsync is cancelled by
                        // calling the Cancel() method of the microservice's CancelSrc property.
                        m.Listener.Stop ()
                        try
                            m.Listener.Server.Shutdown SocketShutdown.Both
                        with
                        | :? SocketException -> ()
                        m.Listener.Server.Close ()

                        lock m.Clients (fun () ->
                            m.Clients |> Seq.iter (fun cl -> cl.tcp.Close ())
                            m.Clients.Clear ())
                )
                |> Async.TryCancelled
            , m.CancelSrc.Token)
        |> ignore

        printfn $"Microservice started at {m.IpAddress}:{port}"
        m

    let shutdown (m : MuSvc) =
        if not m.CancelSrc.IsCancellationRequested then
            m.CancelSrc.Cancel ()
            printfn $"Shutdown request sent to microservice at {m.IpAddress}:{m.Port}."
