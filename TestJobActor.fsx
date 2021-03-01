// -------- BEGIN Send to F# Interactive first --------
#r @"..\_External\DotNET\netstandard2.0\netstandard.dll"
#r @"bin\Debug\netstandard2.0\JobActor.dll"

open miloonline.net.JobActor

let sleepSeconds seconds =
    match (System.Text.RegularExpressions.Regex.Match (seconds, "^[0-9.]+$")).Value with
    | "" -> Error "invalid input"
    | secondsStr ->
        let ms = double secondsStr |> (*) 1000. |> round
        let secs = ms / 1000.
        ms |> int |> System.Threading.Thread.Sleep
        sprintf "Slept for %.3f second%s." secs (if 1. = secs then "" else "s") |> Output
// -------- END Send to F# Interactive first --------

// To get all open TCP ports (at a command prompt):
// netstat -a -p TCP

// To connect to a server:
// telnet HOST PORT

// Start a new actor on port 1234:
let actor = Actor.create sleepSeconds 1234

// Shut down an acotr:
Actor.shutdown actor
