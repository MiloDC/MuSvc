namespace miloonline.net.JobActor

[<AutoOpen>]
module Global =
    let [<Literal>] Terminator = "\r\n"

    [<RequireQualifiedAccess>]
    module Command =
        let [<Literal>] ClientCount = "/cc"
        let [<Literal>] Quit = "/q"
