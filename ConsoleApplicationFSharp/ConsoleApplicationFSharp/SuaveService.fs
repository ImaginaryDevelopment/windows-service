module SuaveFSharp.SuaveService
open System

open Suave

let makeSuaveService () =
    let cts = new System.Threading.CancellationTokenSource()
    let start () =
        let (_startedOptions,server) = startWebServerAsync defaultConfig (Successful.OK "Hello World!")
        Async.Start(server, cts.Token)
    let stop () = cts.Cancel()
    {Start=start; Stop=stop}, cts :> IDisposable
