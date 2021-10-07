[<AutoOpen>]
module net.miloonline.MuSvc.Global

let [<Literal>] MsgTerminator = "\r\n"

[<RequireQualifiedAccess>]
module Command =
    let [<Literal>] ClientCount = "/cc"
    let [<Literal>] Quit = "/q"
