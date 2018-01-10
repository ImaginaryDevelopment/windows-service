module SuaveFSharp.SuaveService
open Suave

let makeSuaveService () =
    let cts = new System.Threading.CancellationTokenSource()
    let start () =
        let (startedOptions,server) = startWebServerAsync defaultConfig (Successful.OK "Hello World!")
        Async.Start(server, cts.Token)
    let stop () = cts.Cancel()
    {Start=start; Stop=stop}, cts.Dispose
