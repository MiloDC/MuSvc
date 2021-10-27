namespace net.miloonline.MuSvc

open System
open System.Net.Sockets

type MuSvc internal (port) =
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
    [<DefaultValue>]val mutable internal Processor : IMuSvcProcessor
    member val internal Clients = ResizeArray<Client> () with get
    member val internal CancelSrc = new Threading.CancellationTokenSource () with get

[<RequireQualifiedAccess>]
module MuSvc =
    let rec internal loopAsync (m : MuSvc) : Async<unit> =
        async {
            try
                match! m.Mailbox.Receive 50 with
                | Input (client, bytes) ->
                    do!
                        async {
                            do! m.Processor.ProcessInput bytes |> Client.sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | ClientCount client ->
                    do!
                        async {
                            do!
                                lock m.Clients (fun () -> m.Clients.Count)
                                |> string |> Text
                                |> Client.sendResultAsync client
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
                            |> Client.sendResultAsync client
                        do!
                            new IO.MemoryStream ()
                            |> Client.loopAsync client m.Mailbox
                    }
                    |> Async.StartChild |> Async.Ignore

            return! loopAsync m
        }

    let shutdown (m : MuSvc) =
        if not m.CancelSrc.IsCancellationRequested then
            m.CancelSrc.Cancel ()
            printfn $"Shutdown request sent to microservice at {m.IpAddress}:{m.Port}."

type MuSvc<'P when 'P :> IMuSvcProcessor and 'P : (new : unit -> 'P)> (port) as this =
    inherit MuSvc (port)

    do
        MailboxProcessor.Start (
            fun inBox ->
                this.Mailbox <- inBox
                this.Processor <- new 'P ()
                (
                    MuSvc.loopAsync this,
                    fun _ ->
                        // This sequence executes when muSvcLoopAsync is cancelled by
                        // calling the Cancel() method of the microservice's CancelSrc property.
                        this.Listener.Stop ()
                        try
                            this.Listener.Server.Shutdown SocketShutdown.Both
                        with
                        | :? SocketException -> ()
                        this.Listener.Server.Close ()

                        lock this.Clients (fun () ->
                            this.Clients |> Seq.iter (fun cl -> cl.tcp.Close ())
                            this.Clients.Clear ())
                )
                |> Async.TryCancelled
            , this.CancelSrc.Token)
        |> ignore

        printfn $"Microservice started at {this.IpAddress}:{port}"
