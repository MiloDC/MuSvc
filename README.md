# MuSvc
A .NET library that demonstrates a microservice written in F#.

### Usage example
```
#r @"bin\Release\net6.0\MuSvc.dll"
open System
open net.miloonline.MuSvc

let countSheep (input : byte array) =
    match Text.Encoding.UTF8.GetString input |> Double.TryParse with
    | false, _ -> Error "invalid input"
    | true, secs when secs < 0. -> Error "invalid input"
    | true, secs ->
        let s = Math.Round (secs, 3) |> Math.Abs    // In case -0
        s * 1000. |> int |> Threading.Thread.Sleep
        sprintf "Slept for %.3f seconds." s |> Text

// On port 6969, start a microservice that sleeps for the requested number of seconds:
let m = MuSvc.create countSheep 6969
Microservice started at 10.9.8.7:6969
```

For the above example, the microservice might be engaged via `telnet` at a command prompt:
```
C:\> telnet 10.9.8.7 6969
Connected to microservice @ 10.9.8.7:6969
3
Slept for 3.000 seconds.
```

Reserved microservice commands are `/cc` to get the current count of connections to a microservice, and `/q` to disconnect from it (although simply closing the socket or otherwise dropping the connection is fine).

To kill the microservice:
```
MuSvc.shutdown m
```

### Credits
MuSvc is authored by [Milo D. Cooper](https://www.miloonline.net).
