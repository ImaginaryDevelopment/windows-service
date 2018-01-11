module SuaveFSharp.RicoSuave

open Suave
open Suave.Model.Binding
open Suave.Operators
open Suave.RequestErrors
open Suave.State.CookieStateStore
open System

let hoist f x =
    f()
    x

let debugMsg msg =
    printfn "%s" msg
//        printfn "printfn:%s" msg
//        System.Console.WriteLine(sprintf "console:%s" msg)
//        System.Diagnostics.Debugger.Log(1,"debugMsg", msg)
//        System.Diagnostics.Trace.WriteLine(sprintf "trace:%s" msg)

let bindToForm f handler =
    let f (r:HttpRequest) =
        r.formData
        |> f
    let withBad msg (ctx:HttpContext) =
        debugMsg(sprintf "bad form: %s, %A" msg ctx.request.form)
        BAD_REQUEST msg ctx
    bindReq f handler withBad
let session = statefulForSession

type Logon = {Username:string; Password: string }

let bindLogon handler (hc:HttpContext) : Async<HttpContext option> =
    let f f = 
        match f "username", f "password" with
        | Choice1Of2 u, Choice1Of2 pwd ->
            Choice1Of2 (u,pwd)
        | Choice2Of2 msg, _ ->
            Choice2Of2 msg
        | _, Choice2Of2 msg ->
            Choice2Of2 msg
    bindToForm f handler hc
//let bindPart handler : WebPart = bindLogon handler
let sessionStore setF = context (fun x ->
    match HttpContext.state x with
    | Some state -> setF state
    | None -> never)

module Path =
    // looks pretty unsafe, since there may already be a question mark and query values in the incoming path
    let withParam (key,value) path = sprintf "%s?%s=%s" path key value
    let home = "/"
    module Account =
        let logon = "/authentication/login"
        let logoff = "/authentication/logout"
module Files =
    module Views =
        let authentication = "Views/Authentication/Login.html"
        let layout = "Views/Shared/_Layout.html"
        let index = "Views/Home/Index.html"

[<RequireQualifiedAccess>]
module Recombinators =
    open System
    open System.IO
    open Suave.Files
    open Suave.Redirection
    open Suave.Utils
    open Suave.Writers

    // type WebPart<'a> = 'a -> Async<'a option>
    let resource key exists getLast getExtension
                (send : string -> bool -> WebPart)
                ctx =

        let sendIt name compression =
            setHeader "Last-Modified" ((getLast key : System.DateTime).ToString("R"))
            >=> setHeader "Vary" "Accept-Encoding"
            >=> setMimeType name
            >=> send key compression

        if exists key then
          let mimes = ctx.runtime.mimeTypesMap (getExtension key)
          match mimes with
          | Some value ->
            match ctx.request.header "if-modified-since" with
            | Choice1Of2 v ->
              match Parse.dateTime v with
              | Choice1Of2 date ->
                if getLast key > date then sendIt value.name value.compression ctx
                else NOT_MODIFIED ctx
              | Choice2Of2 _parse_error -> bad_request Array.empty ctx
            | Choice2Of2 _ ->
              sendIt value.name value.compression ctx
          | None ->
            let ext = getExtension key
            debugMsg (sprintf "failed to find matching mime for ext '%s'" ext)
            fail
        else
          debugMsg (sprintf "failed to find resource by key '%s'" key)
          fail
    let file fileName =
        resource
          fileName
          (File.Exists)
          (fun _name -> DateTime.Now) //FileInfo(name).LastAccessTime)
          (Path.GetExtension)
          sendFile

let userIdKey = "userid"
let getUserId ctx :int option=
    match ctx |> HttpContext.state with
    | Some state->
        printfn "Found state"
        state.get userIdKey
    | None ->
        printfn "no state in context"
        None

// replace all instances of "@"
module ServeItDammit =
    open System
    open System.IO
    open Suave.Sockets
    open Suave.Sockets.Control
    open Suave.Utils

    module ContentRange =
        let parseContentRange (input:string) =
          let contentUnit = input.Split([|' '; '='|], 2)
          let rangeArray = contentUnit.[1].Split([|'-'|])
          let start = int64 rangeArray.[0]
          let finish = if Int64.TryParse (rangeArray.[1], ref 0L) then Some <| int64 rangeArray.[1] else None
          start, finish
          
        let (|ContentRange|_|) (context:HttpContext) =
          match context.request.header "range" with
          | Choice1Of2 rangeValue -> Some <| parseContentRange rangeValue
          | Choice2Of2 _ -> None
      
        let getStream (ctx:HttpContext) fs =
            match ctx with
            | ContentRange (start, finish) ->
              let length = finish |> Option.bind (fun finish -> Some (finish - start))
              new RangedStream(fs, start, length, true) :> Stream, start, fs.Length, HTTP_206.status
            | _ -> fs, 0L, fs.Length, HTTP_200.status

    let serveView homeFolder fView path ctx =
        try
            let sendCleaned uncompressedStream compression ctx =
                let writeFile key =
                    let fs, start, total, status = ContentRange.getStream ctx uncompressedStream
                    (fun (conn, _) ->
                        socket {
                            let getLm = fun path -> FileInfo(path).LastWriteTime
                            let! (encoding,fs) = Compression.transformStream key fs getLm compression ctx.runtime.compressionFolder ctx
                            let finish = start + fs.Length - 1L
                            try
                                match encoding with
                                | Some n ->
                                    let! (_,conn) = asyncWriteLn (sprintf "Content-Range: bytes %d-%d/*" start finish) conn
                                    let! (_,conn) = asyncWriteLn (String.Concat [| "Content-Encoding: "; n.ToString() |]) conn
                                    let! (_,conn) = asyncWriteLn (sprintf "Content-Length: %d\r\n" (fs : Stream).Length) conn
                                    let! conn = flush conn
                                    if ctx.request.``method`` <> HttpMethod.HEAD && fs.Length > 0L then
                                      do! transferStream conn fs
                                    return conn
                                | None ->
                                    let! (_,conn) = asyncWriteLn (sprintf "Content-Range: bytes %d-%d/%d" start finish total) conn
                                    let! (_,conn) = asyncWriteLn (sprintf "Content-Length: %d\r\n" (fs : Stream).Length) conn
                                    let! conn = flush conn
                                    if ctx.request.``method`` <> HttpMethod.HEAD && fs.Length > 0L then
                                      do! transferStream conn fs
                                    return conn
                            finally fs.Dispose()
                        }), status
                let (task,status:HttpStatus) = writeFile path
                { ctx with
                    response =
                      { ctx.response with
                          status = status
                          content = SocketTask task } }
                |> succeed
            let stringToStream (s:string) =
                let stream = new MemoryStream()
                let writer = new StreamWriter(stream)
                writer.Write s
                writer.Flush()
                stream.Position <- 0L
                stream :> Stream
            let filePath = Path.GetFullPath(Path.Combine(homeFolder, path))
            let uncompressed =
                debugMsg (sprintf "getting Fs for %s" filePath)
                let text = File.ReadAllText filePath
                let cleaned:string = text |> fView
                let layout =
                    File.ReadAllText (Path.Combine(homeFolder,Files.Views.layout))
                    |> StringHelpers.replace "@logout" """<a href="/Authentication/Logout">
                                    <i class="fa fa-fw fa-power-off"></i> Log Out
                                </a>"""

                if cleaned.Contains "@scripts" then
                    layout
                    |> StringHelpers.replace "@RenderBody()" (cleaned |> StringHelpers.before "@scripts")
                    |> StringHelpers.replace "@RenderScripts()" (cleaned |> StringHelpers.after "@scripts")
                else
                    layout
                    |> StringHelpers.replace "@RenderBody()" cleaned
                    |> StringHelpers.replace "@RenderScripts()" String.Empty
                |> stringToStream
            Recombinators.resource filePath File.Exists (fun _ -> DateTime.Now) (Path.GetExtension) (fun _fn _compression -> sendCleaned uncompressed (* do not set compression to true for views, as they should not get cached, which compression appears to do *) false) ctx
        with _ex ->
            BAD_REQUEST (sprintf "Unable to locate view (home:%s)" homeFolder) ctx

let redirectWithReturnPath redirection =
    request (fun x ->
        let uri = x.url
        let returnPath = uri.PathAndQuery
        debugMsg (sprintf "%s -> %s -> %s" (x.url.ToString()) redirection returnPath)
        match uri.OriginalString with
        | StringEqualsI redirection ->
            cprintfn ConsoleColor.DarkRed "Redirection when current path is already redirect? %s" redirection
            Redirection.FOUND redirection
        | _ ->
            let target = redirection |> Path.withParam ("returnPath", returnPath)
            cprintfn ConsoleColor.DarkYellow "Redirection proceeding %s -> %s -> %s" returnPath target returnPath
            Redirection.FOUND target
        )
module Auth =
    open Suave.Cookie
    open Suave.Authentication

    let reset =
        unsetPair SessionAuthCookie
        >=> unsetPair StateCookie
        >=> Redirection.FOUND Path.home
    let authenticated secure ctx = authenticated Cookie.CookieLife.Session secure ctx
    let loggedOn f_success =
        statefulForSession >=>
        authenticate
            Cookie.CookieLife.Session
            true
            (fun () ->
                let target = Path.Account.logon
                cprintfn ConsoleColor.DarkYellow "missing Cookie, redirecting to %s" target
                Choice2Of2(redirectWithReturnPath target))
            (fun _ -> Choice2Of2 reset)
            f_success

let printPart msg x = hoist (fun () -> debugMsg msg) x
let printUrl : WebPart = (fun ctx ->
    printfn "Request for %s" ctx.request.url.OriginalString
    async{return None})
let logonView homeFolder msg: WebPart =
    fun ctx ->
        let fServeView text =
            text
            |> StringHelpers.replace "@msg" msg
            |> StringHelpers.replace "@model.requestVerificationToken" (Guid.NewGuid() |> string)
            |> StringHelpers.replace "@returnPath" (match ctx.request.queryParam "returnPath" with Choice1Of2 rp -> rp | _ -> String.Empty)

        ServeItDammit.serveView homeFolder fServeView Files.Views.authentication ctx


