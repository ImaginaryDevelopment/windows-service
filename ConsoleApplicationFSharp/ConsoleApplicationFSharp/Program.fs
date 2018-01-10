// Learn more about F# at http://fsharp.org

open System
open System.IO
open Topshelf

type Timer = System.Timers.Timer
let mutable getNow = fun () -> DateTime.Now

let logAction filename msg =
    let path = Environment.CurrentDirectory // try this in case a hard coded path was unnecessary
    printfn "%s?!??" msg
    let fullPath = Path.Combine(path,filename)
    File.AppendAllText(fullPath, msg)

type TownCrier () =
    let timer = new Timer(1000., AutoReset = true)
    let disp = 
        let mutable recorded = false
        [
            timer.Elapsed.Subscribe(fun _ ->
                if not recorded then
                    recorded <- true
                    sprintf "Engaged starting at %A" (getNow())
                    |> logAction "running.log"
                printfn "It is %A and all is well" (getNow())
            )
            timer :> IDisposable
        ]
    let start () = timer.Start()
    let stop () = timer.Stop()
    member __.Start = start
    member __.Stop = stop
module TopshelfAdapter =
    let inline service<'T when 'T : not struct> (hc:HostConfigurators.HostConfigurator) f = 
        hc.Service<'T>(Action<_>(fun (s:ServiceConfigurators.ServiceConfigurator<'T>) ->
            f s
            ()
        ))
    let inline constructUsing<'T when 'T : not struct> f (sc:ServiceConfigurators.ServiceConfigurator<'T>) =
        sc.ConstructUsing<'T>(factory=Func<_>(f))
    let inline whenStarted<'T when 'T : not struct> f (sc:ServiceConfigurators.ServiceConfigurator<'T>) =
        sc.WhenStarted(Action<_>(f))
    let inline whenStopped<'T when 'T : not struct> f (sc:ServiceConfigurators.ServiceConfigurator<'T>) =
        sc.WhenStopped(Action<_>(f))
    ()
open TopshelfAdapter
[<EntryPoint>]
let main argv =
    let desc = "ConsoleAppFSharpCore"
    let rc = HostFactory.Run(fun x ->
        x.BeforeInstall(fun () ->
            sprintf "Service about to install at %A" (getNow())
            |> logAction "beforeInstall.log"
        ) |> ignore
        x.AfterInstall(fun () ->
            getNow()
            |> sprintf "Service installed at %A"
            |> logAction "afterInstall.log"
        ) |> ignore
        x.AfterUninstall(fun () ->
            getNow()
            |> sprintf "Service uninstalled at %A"
            |> logAction "afterUninstall.log"
        ) |> ignore
        x.StartAutomaticallyDelayed() |> ignore
        service x (fun s ->
            let factory = fun _ -> TownCrier()
            s
            |> constructUsing factory 
            |> whenStarted (fun tc -> tc.Start())
            |> whenStopped (fun tc -> tc.Stop())
            |> ignore
            ()
        ) |> ignore
        x.RunAsLocalSystem() |> ignore
        x.SetDescription desc
        x.SetDisplayName desc
        x.SetServiceName desc
        ()
    )
    let exitCode = Convert.ChangeType(rc, rc.GetTypeCode()) :?> int
    exitCode