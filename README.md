# MuSvc
A .NET library that demonstrates a microservice written in F#.

### Usage example
```
#r @"bin\Release\net5.0\MuSvc.dll"
open net.miloonline.MuSvc

// The function that processes each request received by the microservice.
let sleepSeconds seconds =
    match (System.Text.RegularExpressions.Regex.Match (seconds, "^[0-9.]+$")).Value with
    | "" -> Error "invalid input"
    | secondsStr ->
        let ms = double secondsStr |> (*) 1000. |> round
        let secs = ms / 1000.
        ms |> int |> System.Threading.Thread.Sleep
        sprintf "Slept for %.3f second%s." secs (if 1. = secs then "" else "s") |> Output

let m = MuSvc.create sleepSeconds 1234
```

`MuSvc.create` will send the IP address and port of the microservice to `stdout`.

For the above example, assuming an IP address of `10.9.8.7` and a port of `4242`, the microservice might be invoked via telnet at a command prompt:

```
> telnet 10.9.8.7 4242
Connected to microservice @ 10.9.8.72:4242

8
Slept for 8.000 seconds.
```

To kill the mcroservice:
```
MuSvc.shutdown m
```

### Credits
MuSvc is authored by [Milo D. Cooper](https://www.miloonline.net).
