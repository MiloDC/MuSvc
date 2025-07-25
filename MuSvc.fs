namespace net.miloonline.MuSvc

open System
open System.Net.Sockets

type MuSvcResponse =
    | Bytes of byte array
    | Text of string
    | Error of string

type MuSvc internal (processInput, port) =
    let ipAddr =
        use socket = new Socket (AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        socket.Connect ("8.8.8.8", 65530)
        (socket.LocalEndPoint :?> System.Net.IPEndPoint).Address
    let listener = new TcpListener (ipAddr, port)

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

    interface IDisposable with
        member this.Dispose () =
            this.Listener.Stop ()
            try
                this.Listener.Server.Shutdown SocketShutdown.Both
            with
            | :? SocketException -> ()
            this.Listener.Server.Close ()

            lock this.Clients (fun () ->
                this.Clients |> Seq.iter (fun cl -> cl.tcp.Close ())
                this.Clients.Clear () )

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

    let rec internal loopAsync (svc : MuSvc) : Async<unit> =
        async {
            try
                match! svc.Mailbox.Receive 50 with
                | Input (client, bytes) ->
                    do!
                        async {
                            do! svc.ProcessInput bytes |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | ClientCount client ->
                    do!
                        async {
                            do!
                                lock svc.Clients (fun () -> svc.Clients.Count)
                                |> string |> Text
                                |> sendResultAsync client
                        }
                        |> Async.StartChild |> Async.Ignore
                | RemoveClient client ->
                    client.tcp.Close ()
                    lock svc.Clients (fun () -> svc.Clients.Remove client) |> ignore
            with
            | :? TimeoutException -> ()

            if svc.Listener.Pending () then
                let! tcpClient =
                    Async.FromBeginEnd (
                        svc.Listener.BeginAcceptTcpClient, svc.Listener.EndAcceptTcpClient )
                let client = { tcp = tcpClient; buf = Array.zeroCreate 8192 }
                lock svc.Clients (fun () -> svc.Clients.Add client)
                do!
                    async {
                        do!
                            $"Connected to microservice @ {svc.IpAddress}:{svc.Port}"
                            |> Text
                            |> sendResultAsync client
                        do! Client.loopAsync svc.Mailbox (new IO.MemoryStream ()) client
                    }
                    |> Async.StartChild
                    |> Async.Ignore

            return! loopAsync svc
        }

    let create port (processInput : byte array -> MuSvcResponse) =
        let svc = new MuSvc (processInput, port)

        MailboxProcessor.Start (
            (fun inBox ->
                svc.Mailbox <- inBox
                (
                    loopAsync svc,
                    (fun _ -> (svc :> IDisposable).Dispose ())
                )
                |> Async.TryCancelled ),
            svc.CancelSrc.Token )
        |> ignore

        svc

    let shutdown (svc : MuSvc) =
        if not svc.CancelSrc.IsCancellationRequested then
            svc.CancelSrc.Cancel ()
