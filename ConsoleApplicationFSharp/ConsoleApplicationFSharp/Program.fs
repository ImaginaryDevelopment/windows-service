// Learn more about F# at http://fsharp.org

open System
open System.IO
open Topshelf
open SuaveFSharp.Schema
open System.Diagnostics

type Timer = System.Timers.Timer
let mutable getNow = fun () -> DateTime.Now

let serviceName = "SuaveFSharp"
let pidOpt = SuaveFSharp.BReusable.tryFSwallow (fun () -> Process.GetCurrentProcess().Id)
let logAction =
    let rec logger msg =
        let path = Environment.CurrentDirectory
        let msg = sprintf "%s:%A:%A:%s" serviceName pidOpt (getNow()) msg
        printfn "%s" msg
        let fullPath = Path.Combine(path,"svc.log")
        File.AppendAllText(fullPath, sprintf "%s%s" msg Environment.NewLine)
    logger

module Seq =
    let iterTry f =
        Seq.iter(fun x ->
            try
                f x
            with _ -> () // Dispose should never throw, but in case it does, swallow so we can do the other disposals
        )

let timerService () =
    let timer = new Timer(1000., AutoReset = true)
    let disp =
        let mutable recorded = false
        fun () ->
            [
                timer.Elapsed.Subscribe(fun _ ->
                    if not recorded then
                        recorded <- true
                        sprintf "Engaged starting at %A" (getNow())
                        |> logAction
                    printfn "It is %A and all is well" (getNow())
                )
                timer :> IDisposable
            ]
            |> Seq.iterTry(fun d -> d.Dispose())

    {Start=timer.Start;Stop = timer.Stop}, disp

let recordConstruction () =
    sprintf "Wrapper created with context '%s','%A'" Environment.CommandLine (Environment.GetCommandLineArgs())
    |> logAction
type ServiceWrapper(x:ServiceDetails, disposalOpt) =
    let stop () = x.Stop()
    let mutable t = null
    let disposal =
        let task = (new System.Threading.Tasks.Task(Action recordConstruction))
        task.Start()
        [   yield task :> IDisposable
            match disposalOpt with
            | Some d -> yield d
            | None -> ()
        ]

    member __.Start () =
        let inline f () =
            x.Start()
        t <- Threading.Thread(Threading.ThreadStart f)
        t.Start()
    member __.Stop () =
        t
        |> Option.ofObj
        |> Option.iter(fun t ->
            logAction "Asking service to stop"
            stop()
            logAction "Service stop returned control"
            System.Threading.Thread.Sleep 200
            t.Abort()
            logAction "Thread aborted"
        )
    interface IDisposable with
        member __.Dispose() = 
            disposal
            |> Seq.iterTry(fun x -> x.Dispose())


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
let main _argv =
    let rc = HostFactory.Run(fun x ->
        x.BeforeInstall(fun () ->
            logAction "Service about to install"
        ) |> ignore
        x.AfterInstall(fun () ->
            logAction "Service installed"
        ) |> ignore
        x.AfterUninstall(fun () ->
            logAction "Service uninstalled"
        ) |> ignore
        x.StartAutomaticallyDelayed() |> ignore
        service x (fun s ->
            let factory = fun _ ->
                let sd,d = SuaveFSharp.SuaveService.makeSuaveService()
                new ServiceWrapper(sd, Some d)
            s
            |> constructUsing factory
            |> whenStarted (fun tc -> tc.Start())
            |> whenStopped (fun tc -> tc.Stop())
            |> ignore
            ()
        ) |> ignore
        x.RunAsLocalSystem() |> ignore
        x.SetDescription serviceName
        x.SetDisplayName serviceName
        x.SetServiceName serviceName
        ()
    )
    let exitCode = Convert.ChangeType(rc, rc.GetTypeCode()) :?> int
    exitCode