namespace SuaveFSharp

module SuaveService =
    open System

    open Suave
    open Suave.Logging
    open Suave.Filters
    open Suave.Logging.Message
    open System.Reflection
    open Suave.Operators
    open Suave.RequestErrors
    open Suave.Successful
    open RicoSuave
    open Domain.Users
    open Helpers
    open Encryption
    open SuaveFSharp.Dal.AdoHelper.Connections
    open Newtonsoft.Json

    type Passwordy =
        | Raw of string
        | Hashy of string

    let cryptPwd = "multipass"
    let debugMsg msg =
        printfn "%s" msg
        //printfn "printfn:%s" msg
        //System.Console.WriteLine(sprintf "console:%s" msg)
        //System.Diagnostics.Debugger.Log(1,"debugMsg", msg)
        //System.Diagnostics.Trace.WriteLine(sprintf "trace:%s" msg)

    let makeHelloWorldSuaveService() =
        let cts = new System.Threading.CancellationTokenSource()
        let start () =
            let (_startedOptions,server) = startWebServerAsync defaultConfig (Successful.OK "Hello World!")
            Async.Start(server, cts.Token)
        let stop () = cts.Cancel()
        {Start=start; Stop=stop}, cts :> IDisposable

    let makeSuaveService () =
        let cts = new System.Threading.CancellationTokenSource()

        let mimeMap (config:SuaveConfig) (key:string) : MimeType option =
            // do not remap this to use the current config, we need to use the base mimeTypesMap
            match defaultConfig.mimeTypesMap key with
            | Some mt -> Some mt
            | None ->

                config.logger.warn (eventX  "looking for mime key {key}" >> setFieldValue "key" key)
                |> fun x -> x
                [
                    ".jsx", {
                        MimeType.compression =
                #if DEBUG // no compression in debug, because compression also caches in Suave-world
                            false
                #else
                            true
                #endif
                        MimeType.name="text/javascript"
                        }
                    ".woff", { MimeType.compression = true; MimeType.name="application/x-font-woff"}
                ]
                |> Map.ofSeq
                |> fun m -> if m.ContainsKey key then Some m.[key] else None
        let config =
            let port:Sockets.Port = 8080us // Sockets.Port() //8080us
            let homeFolder =
                    BReusable.Reflection.Assemblies.getAssemblyFullPath typeof<Schema.ServiceDetails>.Assembly
                    |> IO.Path.GetDirectoryName

            {defaultConfig with
                            bindings = [HttpBinding.create HTTP (System.Net.IPAddress.Parse "0.0.0.0") port]
                            homeFolder = Some homeFolder
                            //cancellationToken = cancellationOpt |> Option.getOrDefault defaultConfig.cancellationToken
    //                        errorHandler = errorHandler
                            //mimeTypesMap = mimeMap
            } |> fun c -> { c with mimeTypesMap = mimeMap c}
        let homeFolder = // homefolder should have a views folder in it, adapt if we are in debug to check parents
            let rec checkForViews checkAbove dp =
                if isNull dp then
                    null
                else
                    let viewsPath = IO.Path.Combine(dp,"Views") |> IO.Path.GetFullPath

                    if IO.Directory.Exists viewsPath then
                        dp
                    elif checkAbove then IO.Path.GetDirectoryName dp |> checkForViews true
                    else null
                |> function
                    | null -> failwithf "No views folder found"
                    | x -> x
            match config.homeFolder with
            | None ->
                let p = ConnectionStringReader.getConfigPath()
                checkForViews true p
            | Some p ->
                checkForViews true p
        let mutable printedCompressionFolder = false
        let onFirstRequest  (ctx:HttpContext) =
            if not printedCompressionFolder then
                debugMsg (sprintf "CompressionFolder:%s" ctx.runtime.compressionFolder)
                printedCompressionFolder <- true
            ctx
        let returnPathOrHome =
            request (fun x ->
                let path =
                    match (x.formData "returnPath") with
                    | Choice1Of2 path -> path
                    | _ -> Path.home
                Redirection.FOUND path)

        let loginParts homeFolder cn username pwd : WebPart =
            let logonView = logonView homeFolder
            debugMsg "attempting login"
            match username with
            | null
            | "" -> logonView "No username provided"
            | _ ->
                try
                    Dal.DataAccess.Users.getUserId cn username
                    |> Some
                with ex ->
                    debugMsg (sprintf "username exception:%A" ex)
                    None
                |> function
                    | None ->
                        // insecure, should not specify what is invalid.
                        logonView "Login failure"
                    | Some userId ->
                        let (user,updateDate) = Dal.DataAccess.Users.updateAccessAttempts userId cn
                        if user.IsLockedOut then
                            logonView "Unable to login at this time"
                        else
                            let hash =
                                match pwd with
                                | Passwordy.Raw x ->
                                    debugMsg (sprintf "password:%s" x)
                                    x |> Helpers.ReferenceData.hashCredential
                                | Hashy x -> x

                            match Domain.Users.validateLogin user.LoginAttempts user.IsLockedOut (hash, user.PasswordHash) (updateDate,DateTime.Today) with
                            | ValidLoginResult.Success ->
                                // reset access attempts
                                Dal.DataAccess.Users.loginSuccess cn user
                                Auth.authenticated false
                                >=> session
                                >=> sessionStore (fun store ->
                                    debugMsg "setting up sessionStore"
                                    store.set "username" username
                                    >=> store.set userIdKey userId
                                    )
                                >=> returnPathOrHome
                            | _ -> logonView "Username or password is invalid."
            // desired feature, if there is already a return path, and there's not going to be a new one here, keep it
        let logon homeFolder cn : WebPart =
            (printPart "Hello logon" >>
                choose [
                    GET >=> logonView homeFolder null
                    POST >=> bindToForm (fun f ->
                        match f "username", f "password" with
                        | Choice1Of2 un, Choice1Of2 pwd -> Choice1Of2(un,Helpers.ReferenceData.hashCredential pwd |> Passwordy.Hashy)
                        | Choice2Of2 msg , _ -> Choice2Of2 msg
                        | _, Choice2Of2 msg -> Choice2Of2 msg

                    ) (fun (un,pwd) -> loginParts homeFolder cn un pwd)
                ])
        (
            let configPath = ConnectionStringReader.getTargetConfigFullPath()
            if not <| IO.File.Exists configPath then
                {Settings.ConnectionString = encrypt cryptPwd "Server=." }
                |> JsonConvert.SerializeObject
                |> Tuple2.curry IO.File.WriteAllText configPath
                ()
        )
        let getCn csProviderOpt : SuaveFSharp.Dal.AdoHelper.Connections.Connector =
            let failIt fpOpt exOpt =
                let failText =
                    match fpOpt with
                    | Some fp ->
                        sprintf "Failed to read connection string file at '%s'" fp
                    | None ->
                        sprintf "Failed to read connection string file"
                match exOpt with
                | Some ex ->
                    raise <| InvalidOperationException(failText,ex)
                | None -> raise <| InvalidOperationException(failText)

            let useFilePath (fpOpt:string option) =
                    try
                        ConnectionStringReader.getCn (decrypt cryptPwd) fpOpt
                    with ex ->
                        failIt fpOpt (Some ex)
                    |> function
                        | Happy cn -> cn
                        | Unhappy path -> failIt (Some path) None

            match csProviderOpt with
            | Some (Cs cs) -> Connector.CreateCString cs
            | Some (CsFilePath fp) -> useFilePath (fp |> String.emptyToNull |> Option.ofObj)
            | None -> useFilePath None

        let indexView homeFolder ctx =
            debugMsg "attempting to serve index"
            ServeItDammit.serveView homeFolder id Files.Views.index ctx

        let cn = getCn None
        let app:WebPart<_> =
                choose [
                    GET >=>
                        choose [
                            pathStarts "/Content" >=> Files.browseHome
                            pathStarts "/Content" >=> (fun ctx -> NOT_FOUND ctx.request.url.AbsolutePath ctx)
                            pathStarts "/Scripts" >=> Files.browseHome
                            pathStarts "/fonts" >=> Files.browseHome
                            path "/favicon.ico" >=> Files.browseHome
                            path "/hello" >=> OK "world"
                        ]
                    printUrl
                    pathStarts Path.Account.logon >=> (logon homeFolder cn)
                    pathStarts Path.Account.logoff >=> Auth.reset
                    path "/" >=> indexView homeFolder
                ]

        let start () =
            let (_startedOptions,server) = startWebServerAsync config (onFirstRequest>>app)
            Async.Start(server, cts.Token)
        let stop () = cts.Cancel()
        {Start=start; Stop=stop}, cts :> IDisposable
