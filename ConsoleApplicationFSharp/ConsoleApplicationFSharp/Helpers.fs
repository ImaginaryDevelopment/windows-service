module SuaveFSharp.Helpers

open Newtonsoft.Json

let toJson (o:obj) = JsonConvert.SerializeObject( o, JsonSerializerSettings(NullValueHandling = NullValueHandling.Ignore))
let hoist f x =
    f()
    x
module Option =
    let getOrDefault y = function | Some x -> x | None -> y

type ConnectionStringProvider =
    | Cs of string
    | CsFilePath of string

module ReferenceData =
    open System.Text

    let hashCredential (s:string) =
        //var hashed = PasswordHash.CalculateMD5Hash(pwdBox.Password);
        let md5 = System.Security.Cryptography.MD5.Create()
        let inputBytes = System.Text.Encoding.ASCII.GetBytes(s)
        let hash = md5.ComputeHash inputBytes
        let sb = StringBuilder()
        for i in [0..hash.Length - 1] do
            sb.Append(hash.[i].ToString("x2")) |> ignore<StringBuilder>
        sb.ToString()

module ConnectionStringReader =
    open System.IO
    open System




    let getConfigPath() =
        let asm = System.Reflection.Assembly.GetExecutingAssembly()
        let asmPath = BReusable.Reflection.Assemblies.getAssemblyFullPath(asm)
        Path.GetDirectoryName asmPath
    let getTargetConfigFullPath() =
        let appFolder = getConfigPath()
        let configFile = Path.Combine(appFolder, "Service.Config.json")
        configFile

    let getConnectionStringJson filePathOpt =
        let configFile =
            match filePathOpt with
            | Some (ValueString as fp) -> fp
            | None
            | _ -> getTargetConfigFullPath()
        let result =
            try
                match File.Exists configFile with
                | false -> None
                | true ->
                    match File.ReadAllText configFile with
                    | ValueString as text ->
                        Some text
                    | _ -> None
            with ex ->
                let nextEx = InvalidOperationException("getConnectionStringXml", ex)
                nextEx.Data.Add("Fullpath", configFile)
                raise nextEx
        configFile,result

    let getRawConnectionString(json:string) =
        let settings = JsonConvert.DeserializeObject<_>(json)
        // can't access AppCn as it would point to the still encrypted string
        settings.ConnectionString

    let private decryptCs fDecrypt x =
        match x with
        | ValueString ->
            try
                fDecrypt x
                |> Some
            with _ -> None
        | _ -> None

    let getCn fDecrypt filePathOpt : Rail<Dal.AdoHelper.Connector,string> =
        // if we are going to fail, need the path it used
        let path, xdoc = getConnectionStringJson filePathOpt
        match xdoc with
        | Some xdoc ->
            xdoc
            |> getRawConnectionString
            |> decryptCs fDecrypt
            |> Option.map (Dal.AdoHelper.Connector.CreateCString)
            |> function
                | Some cn -> Happy cn
                | None -> Unhappy path
        | None ->
            Unhappy path

module CliHelpers =
    // if -shortform --longform or /longform are present then match
    let (|COptionPresent|_|) short long args =
        match args with
        | x when x = sprintf "-%s" short -> Some ()
        | x when x = sprintf "--%s" long -> Some ()
        | x when x = sprintf "/%s" long -> Some()
        | _ -> None
