# MuSvc
A .NET library that demonstrates a microservice written in F#.

### Usage example
```
#r @"bin\Release\net5.0\MuSvc.dll"
open net.miloonline.MuSvc

// The function that processes each request received by the microservice.
let sleepSeconds (seconds : byte array) =
    match Text.Encoding.UTF8.GetString seconds |> Double.TryParse with
    | false, _ -> Error "invalid input"
    | true, s ->
        let secs = Math.Round (s, 3)
        secs * 1000. |> int |> Threading.Thread.Sleep
        sprintf "Slept for %.3f second%s." secs (if 1. = secs then "" else "s") |> Output

let m = MuSvc.create sleepSeconds 4242
```

`MuSvc.create` will send the IP address and port of the microservice to `stdout`.

For the above example, assuming an IP address of `10.9.8.7` and a port of `4242`, the microservice might be invoked via telnet at a command prompt:

```
> telnet 10.9.8.7 4242
Connected to microservice @ 10.9.8.72:4242

8
Slept for 8.000 seconds.
```

To kill the microservice:
```
MuSvc.shutdown m
```

### Credits
MuSvc is authored by [Milo D. Cooper](https://www.miloonline.net).
