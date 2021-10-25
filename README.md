# MuSvc
A .NET library that demonstrates a microservice written in F#.

### Usage example
```
#r @"bin\Release\net5.0\MuSvc.dll"
open System
open net.miloonline.MuSvc

// The type that processes each request received by the microservice.
type Sleeper () =
    interface IMuSvcProcessor with
        member this.ProcessInput input =
            match Text.Encoding.UTF8.GetString input |> Double.TryParse with
            | false, _ -> Error "invalid input"
            | true, secs when secs < 0. -> Error "invalid input"
            | true, secs ->
                let s = Math.Round (secs, 3) |> Math.Abs    // In case -0
                s * 1000. |> int |> Threading.Thread.Sleep
                sprintf "Slept for %.3f second%s." s (if 1. = s then "" else "s") |> Text

let m = Sleeper () |> MuSvc.create 1234
```

`MuSvc.create` will send the IP address and port of the microservice to `stdout`:
```
Microservice started at 10.9.8.7:6969
```

For the above example, the microservice might be invoked via `telnet` at a command prompt:
```
C:\> telnet 10.9.8.7 6969
Connected to microservice @ 10.9.8.7:6969
3
Slept for 3.000 seconds.
```

To kill the microservice:
```
MuSvc.shutdown m
```

### Credits
MuSvc is authored by [Milo D. Cooper](https://www.miloonline.net).
