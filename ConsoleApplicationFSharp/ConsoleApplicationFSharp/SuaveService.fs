module SuaveFSharp.SuaveService
open Suave


let start,stop, dispose =
    let cts = new System.Threading.CancellationTokenSource()
    let start () = 
        let address = defaultConfig.bindings |> Seq.head |> fun x -> x |> string // |> Dump |> ignore
        let (startedOptions,server) = startWebServerAsync defaultConfig (Successful.OK "Hello World!")
        Async.Start(server, cts.Token)
    //startedOptions |> Async.RunSynchronously |> printfn "started: %A"
    let stop () =
        cts.Cancel()
    start,stop, cts.Dispose
