module SuaveFSharp.Dal.AdoHelper

// desired features
// "do we have a connectionstring or connection?" ignorance-capable coding
// "are we in a transaction?" ignorance-capable coding
// massive reduction in using blocks necessary
// remove any reliance/requirement to refer directly to System.Data in layers that need not

// conventions:
//  pascal cased functions if they are specially crafted to be easier for C# to consume
//  F# method params are in an order that makes more sense for partial application
//  C# targeted method params are in an order that makes more sense for C# consumption

// possible future features:
// automatic sql exception catching to add ex.data, then rethrow
// adding nice extension methods to help IDbConnection act as fully featured as SqlConnection (there are some features like cmd.Params.AddWithValue missing in IDbCommand)

open System
open System.Collections.Generic
open System.Data
open System.Data.SqlClient
open SuaveFSharp

let nullToDbNull x = if not <| isNull x then x :> obj else upcast System.DBNull.Value

let dbNullToOption (x:obj) : obj option=
    if System.DBNull.Value.Equals x then
        None
    else Some x
let eitherNullToOption (x:obj): obj option=
    nullToDbNull x
    |> dbNullToOption

let dbNullToObjNull (x:obj) : obj =
    match dbNullToOption x with
    |Some x-> x
    |None -> null
let identityOptToValueOrDbNull (x:ValidIdentity<'t> option) = 
    match x with
    | Some vi -> vi.Value |> box
    | None -> System.DBNull.Value |> box
let maybeValidSqlDate (x:DateTime) = 
    if x.Year > 1753 then // https://stackoverflow.com/questions/15157328/datetime-minvalue-and-sqldatetime-overflow
        Some x
    else None

type System.DBNull with
    static member OfObj (x:obj) = if not <| isNull x then x else upcast System.DBNull.Value
    static member ToObj (x:obj) = dbNullToObjNull x
    static member OfOption (x:obj option) = match x with Some x -> System.DBNull.OfObj x | None -> upcast System.DBNull.Value
    static member ToOption (x:obj) = dbNullToOption x

module Option = 
    let ofDbNullable (x:obj) = dbNullToOption x

// don't mate the next 2 functions as the one doesn't need the dependency on System.Data
/// Get a query that can run directly in SSMS or LINQPad
let getRunnableQuery query (parameters:Map<string,obj> option) = 
    match parameters with
    | None -> query
    | Some parameters ->
        parameters
        |> Map.toSeq
        // this needs work, DateTimes aren't working, who knows what else
        |> Seq.map (fun (p,o) ->
            // we are assuming that null/dbNull would still get passed down to the query
            let valueOpt = o |> eitherNullToOption
            let surround delims x = sprintf"%s%s%s" delims x delims
            let quote = surround "'"
            let quoteSafe = 
                // should we handle DbNull here too?
                function
                | null -> "null"
                | x -> x |> StringHelpers.replace "'" "''" |> quote
            let clampDt = clamp (DateTime(1753,1,1)) (DateTime(9999,12,31))
            match valueOpt with
            | None -> "int -- type guessed. value was null or dbNull", null
            | Some(:? int as i) -> "int", i |> string
            | Some(:? string as s) -> "VARCHAR(255) -- assumes the arg will fit", s |> quoteSafe
            | Some(:? DateTime as dt) -> "DateTime", dt |> clampDt |> fun x -> x.ToString("yyyy.MM.dd hh:mm:ss") |> quote
            | Some(:? Boolean as b) -> "bit", (if b then 1 else 0) |> string
            // this case doesn't work very well, but not sure it could be improved much
            | Some x -> 
                printfn "guessing for %A" x
                "int; -- type guessed. value was unmapped: " + (x.GetType().Name), x |> string |> quoteSafe
            |> fun (dataType, value) ->
                let declaration = sprintf "DECLARE %s %s;\r\n" p dataType
                sprintf "%sset %s=%s;\r\n" declaration p value
            )
        |> StringHelpers.delimit "\r\n"
        |> ((+) "-- These are the params passed into the query:\r\n")
    |> flip (+) query

let ( |? ) (x:'t) (f) = if not <| isNull x then x else f()
let ( |?? ) (v:'t option) x = match v with |Some x -> x  |None -> x

let columnNameErrorKey = "ColumnName"
let inline private getColumn<'T> (r:System.Data.IDataRecord) (name:string) (f:obj option -> 'T option) =
    try
        match r.[name] with
        | x when x = box System.DBNull.Value -> None
        | x -> Some x
        |> f
    with ex ->
        ex.Data.Add(columnNameErrorKey, name)
        reraise()

let getRecordOpt (r:System.Data.IDataRecord) name =
    getColumn r name id

let getRecordOptT<'T> r name =
    getColumn r name (Option.map(fun x -> x :?> 'T))

let getRecordT<'T> r name =
    getRecordOptT<'T> r name
    |> function
        | Some x -> x
        | None -> failwithf "Expected a value found none for column %s" name

// http://stackoverflow.com/questions/2983087/f-working-with-datareader/
let getRecordBytesData i (r : IDataRecord) =
    let len = r.GetBytes(i, int64 0, null, 0, 0)
    // Create a buffer to hold the bytes, and then
    // read the bytes from the DataTableReader.
    let buffer : byte array = Array.zeroCreate (int32 len)
    r.GetBytes(i, int64 0, buffer, 0, int32 len) |> ignore
    buffer

let readInt r name = getRecordOptT<int> r name
let readStringOrNull r name = getRecordOptT<String> r name |> function | Some s -> s | None -> null

// this doesn't work in C# land, it's an extension class inside a module
[<System.Runtime.CompilerServices.Extension>]
module DataRecordExtensions =
    [<System.Runtime.CompilerServices.Extension>]
    // uses default instead of opt
    let ReadOrDefault(r:System.Data.IDataRecord) name (fOpt:Func<_,_>) =
        getRecordOpt r name
        |> Option.bind (fun x -> (if not <| isNull fOpt then Some (fOpt.Invoke x) else x :?> _ ))
        |> function
            | Some x -> x
            | None -> Unchecked.defaultof<_>

    [<System.Runtime.CompilerServices.Extension>]
    let ReadNullable(r:System.Data.IDataRecord) name =
        ReadOrDefault r name (Func<_,_>(fun o -> Nullable (o :?> 't)))


module Connections =
    open StringHelpers
    // The heart of the code-ignorance possibilities
    [<NoComparison>][<NoEquality>]
    type Connector =
        private // suggested by http://stackoverflow.com/q/24212865/57883
        | CString of string
        | ICon of IDbConnection
        with
            static member CreateICon x = Connector.ICon(x)
            static member CreateCString s =
                if System.String.IsNullOrEmpty(s) then
                    failwithf "Invalid connection string:%s" s
                Connector.CString(s)

    let (|CString|ICon|) x =
        match x with
        |Connector.CString cs -> CString cs
        |Connector.ICon con -> ICon con

    let inline openConnection (conn: #IDbConnection) =
        if conn.State = ConnectionState.Closed then
            printfn "Opening connection to unknown via hash %i" (conn.ConnectionString.GetHashCode())
            conn.Open()

    let validateDelegateIsNotPartial (_ : _ -> 't) =
        let tType = typeof<'t>

        if tType = typeof<Delegate> || tType.IsSubclassOf typeof<Delegate> || tType.FullName.StartsWith "Microsoft.FSharp.Core.FSharpFunc" then
            invalidArg "f" (sprintf "Bad delegate passed %A" tType)

    let cleanConnectionString x =
        //"Data Source=;Initial Catalog=;App=;User Id=;Password=;"
        x
        |> String.splitO [";"] StringSplitOptions.RemoveEmptyEntries
        |> Seq.map(String.split ["="] >> List.ofSeq)
        |> Seq.map(function | [name;value] -> Rail.Happy(name,value) | _ -> Rail.Unhappy "could not read connection string to clean")
        |> Seq.choose Railway.toHappyOption
        |> Seq.filter(fst >> String.equalsI "password" >> not )
        |> Seq.filter(fst >> String.equalsI "user id" >> not)
        |> Seq.map (fun (name,value) -> sprintf "%s=%s" name value)
        |> delimit ";"

    /// Expectations:
    ///     Connector.ICon expects an open connection
    /// as long as you aren't returning
    ///  an IEnumerable that depends on the connection staying open
    ///  a partial function
    let inline runWithConnection connector f =
        // hoping this is the correct central place that ALL logic comes through for connections, so we can catch errors/etc.

        validateDelegateIsNotPartial f
        let mutable cstring = null
        let withCon f =
            match connector with
            | ICon con ->
                cstring <- con.ConnectionString
                f con
            | CString cs ->
                cstring <- cs
                use conn = new SqlConnection(cs)
                openConnection conn
                f conn
        try
            withCon f
        with ex ->
            if not <| ex.Data.Contains "cstring" then
                ex.Data.Add("cstring", cleanConnectionString cstring)
            let getAddSprocText name =
                withCon (fun con ->
                    let con = con:?> SqlConnection
                    use cmd = new SqlCommand(sprintf "sp_helptext '%s'" name, con)
                    let text = cmd.ExecuteScalar()
                    ex.Data.Add("sp_helptext", text)
                )
            if not <| isNull ex.Message then
                if ex.Message.StartsWith "Procedure or function '" && ex.Message.EndsWith "' has too many arguments specified." then
                    try
                        let name = System.Text.RegularExpressions.Regex.Match(ex.Message, "Procedure or function '(\w+)' has too many arguments specified.").Groups.[1].Value
                        getAddSprocText name
                        ()
                    with ex -> ()
                elif ex.Message.StartsWith "Procedure or function '" && ex.Message.Contains "' expects parameter '" && ex.Message.EndsWith "', which was not supplied." then
                    try
                        let name = System.Text.RegularExpressions.Regex.Match(ex.Message, "Procedure or function '(\w+)' expects parameter '(@\w+)', which was not supplied.").Groups.[1].Value
                        getAddSprocText name
                    with ex -> ()
                    ()
            reraise()

    let runWithCn cn f = runWithConnection cn (Connector.ICon >> f)

    let inline getItems connector f = runWithConnection connector (f >> Array.ofSeq)

    /// get a sequence of items, which is automatically pulled into an array so that the disposal of the connection is safe
    let GetItems<'tResult> (runWithConnectionFunc:System.Func<IDbConnection, IEnumerable<'tResult>>) cn = getItems cn (runWithConnectionFunc.Invoke >> Array.ofSeq)
    let ActWithConnection connector (f:System.Action<_>) = runWithConnection connector f.Invoke
    let RunWithConnection connector (f:System.Func<_,_>) = runWithConnection connector f.Invoke
    let RunWithCn connector (f:System.Func<_,_>) = runWithConnection connector (Connector.ICon >> f.Invoke)
    let ActWithCn connector (f:System.Action<_>) = runWithConnection connector (Connector.ICon >> f.Invoke)

type Connector = Connections.Connector


/// all SqlClient specific code should live in this module
module SqlConnections =
    type SqlConnector = Connections.Connector

    /// Expectations:
    ///     Connector.ICon expects an open connection
    /// as long as you aren't returning an IEnumerable that depends on the connection staying open, this method is safe for anything
    let inline runWithConnection (connector:SqlConnector) f = Connections.runWithConnection connector (fun con -> con :?> SqlConnection |> f)

    let RunWithConnection connector (f:System.Action<_>)=  runWithConnection connector f.Invoke
    /// as long as you aren't returning an IEnumerable that depends on the connection staying open, this method is safe for anything
    let GetFromConnection connector (f:System.Func<_,_>)= runWithConnection connector f.Invoke
    /// get a sequence of items, which is automatically pulled into an array so that the disposal of the connection is safe
    let GetItems connector (f:System.Func<_,_ seq>) = runWithConnection connector (f.Invoke >> Array.ofSeq)

module Commands =
    /// replace ' with '' and any other sanitize/cleaning of a string
    open BReusable.StringPatterns
    open BReusable.Diagnostics
    open System.Diagnostics

    let encodeStringParam =
        function
        |ValueString as s -> s.Replace("'", "''")
        | x -> x

    [<NoComparison;NoEquality>]
    type Input = {CommandText:string; CommandType:CommandType; OptParameters:IDictionary<string,obj> option}

    [<NoComparison;NoEquality>]
    type InputC = {CommandTextC:string; CommandTypeOpt: CommandType; ParametersOpt:IDictionary<string,obj>; }
        with
            member x.ToSqlCommandInput =
                {       CommandText = x.CommandTextC
                        CommandType = x.CommandTypeOpt
                        OptParameters = if isNull x.ParametersOpt then None else Some x.ParametersOpt
                        //OptExtraPrep = if isNull x.ExtraPrepOpt then None else Some x.ExtraPrepOpt.Invoke
                        }

    // works with null just as well as `None`
    let loadParameters (cmd: #IDbCommand) (parameters: IDictionary<string,obj> option) =
        let 
#if !DEBUG
            inline
#endif
            loadParam (KeyValue(k,v)) =
            let param = cmd.CreateParameter ()
            param.Value <- System.DBNull.OfObj v
            param.ParameterName <- k
            cmd.Parameters.Add param |> ignore
        match parameters with
        | None -> ()
        | Some x when isNull x -> ()
        | Some items -> items |> Seq.iter loadParam

    let 

#if !DEBUG
            inline
#endif
        prepareCommand sci (cmd:'a when 'a :> IDbCommand) =
        cmd.CommandText <- sci.CommandText
        cmd.CommandType <- sci.CommandType
        loadParameters cmd sci.OptParameters

    // sci is solely for diagnostic output on failure
    let inline runWithSqlDiag sci f =
        printfn "sql: %s params: %A" sci.CommandText sci.OptParameters
        let sw = System.Diagnostics.Stopwatch.StartNew ()
        try
            let result = f ()
            sw.Stop ()
            if sw.ElapsedMilliseconds > 700L then
                    System.Diagnostics.Debug.WriteLine(sprintf "runWithSqlDiag took %A" sw.ElapsedMilliseconds)
            result
        with ex ->
            ex.Data.Add("CommandText", sci.CommandText)
            ex.Data.Add("CommandType", sci.CommandType)
            ex.Data.Add("Parameters", sprintf "%A" sci.OptParameters)
            reraise ()

    // breakpoints inside inline functions may not be hit.
    let 
#if !DEBUG
        inline
#endif
        useCmd (con: #IDbConnection) sci f =
        try
            use cmd = con.CreateCommand()
            prepareCommand sci cmd
            runWithSqlDiag sci (fun () -> f cmd)
        with | :? SqlException as ex when 
                    not <| isNull ex.Message && 
                    ex.Message.Contains("Procedure or function") 
                    && sci.CommandType = CommandType.Text ->
            logExS (Some "SqlException") (sprintf "Wrong commandType for %s" sci.CommandText |> Some) ex
            let sci = {sci with CommandType = CommandType.StoredProcedure}
            use cmd = con.CreateCommand()
            prepareCommand sci cmd
            runWithSqlDiag sci (fun () -> f cmd)


    let inline executeNonQuery (cmd: #IDbCommand) = cmd.ExecuteNonQuery ()
    let inline executeScalar (cmd: #IDbCommand) = cmd.ExecuteScalar ()
    let inline executeReader (cmd: #IDbCommand) = cmd.ExecuteReader ()
    let inline executeTable (fDataAdapter: _ -> #System.Data.Common.DbDataAdapter) (cmd: #IDbCommand) =
        let dt = new DataTable ()
        use da = fDataAdapter cmd
        let result = da.Fill dt
        result |> ignore
        dt

    let inline executeDataset (fDataAdapter: _ -> #System.Data.Common.DbDataAdapter) (cmd: #IDbCommand) =
        let ds = new DataSet()
        use da = fDataAdapter cmd
        da.Fill ds |> ignore
        ds

    let inline executeDatasetName (fDataAdapter: _ -> #System.Data.Common.DbDataAdapter) tableName (cmd: #IDbCommand) =
        let ds = new DataSet()
        use da = fDataAdapter cmd
        da.Fill(ds,tableName) |> ignore
        ds

    let inline executeReaderArray f fSeqOpt (cmd: #IDbCommand) =
        use reader = cmd.ExecuteReader ()
        reader
        |> Seq.unfold(fun r -> if r.Read () then Some (r :> IDataRecord |> f,r) else None)
        |> fun items -> match fSeqOpt with |Some f -> f items | None -> items
        |> List<_>
        |> System.Collections.ObjectModel.ReadOnlyCollection

    /// Works with non-nullable return values (an int column that allows nulls for instance, would fail in the case of null)
    let inline getOptScalar' cmd =
        let raw = executeScalar cmd
        if isNull raw then
            None
        else
            raw
            |> System.DBNull.ToOption

    let inline getScalarT<'t> cmd =
        let result = executeScalar cmd
        let onBadResult (ex:#Exception) =
            if System.Diagnostics.Debugger.IsAttached then
                System.Diagnostics.Debugger.Break()
            raise ex
        // if 't is an option type or nullable, then the null exceptions are inappropriate
        if box System.DBNull.Value = result then
            let ex = NullReferenceException("getScalarT result was dbNull")
            onBadResult ex

        if isNull result then
            let ex = NullReferenceException("getScalarT result was null")
            onBadResult ex

        result :?> 't
    let inline getNonQueryFromCon con sci= useCmd con sci executeNonQuery

    let inline getScalarFromCon con sci= useCmd con sci executeScalar
    let inline getOptScalarFromCon con sci = useCmd con sci getOptScalar'
    let inline getScalarIntFromCon con sci = useCmd con sci getScalarT<int>

    // a single runReader doesn't make sense unless reading a single row of data
    let inline getReaderArrayFromCon con sci f = useCmd con sci (executeReaderArray f None)
    let inline getReaderArrayLimitFromCon limit con sci f =
        useCmd con sci (executeReaderArray f (Some (Seq.take limit)))

    // unless you cast the identity to int (within your sql statements) it will be a decimal (http://dba.stackexchange.com/questions/4696/why-is-select-identity-returning-a-decimal)
    /// select @@identity or SCOPE_IDENTITY() both return Numeric(38,0)
    /// see also http://dba.stackexchange.com/questions/4696/why-is-select-identity-returning-a-decimal
    let inline getScalarIdentityFromCon con sci = 
        useCmd con sci 
                ( fun x -> 
                    match x |> getScalarT<obj> with
                    | :? decimal as x -> int x
                    | :? int as x -> x
                    | x -> raise <| InvalidCastException(if isNull x || x = box System.DBNull.Value then sprintf "null returned for identity in cmd %s" sci.CommandText else sprintf "invalid type returned from identity command %s" (x.GetType().Name))
                )

    let UseCmd connector (scic:InputC) (f:System.Func<_,_>) = useCmd connector scic.ToSqlCommandInput f.Invoke
    let RunReaderArray cmd (f:System.Func<_,_>) = executeReaderArray f.Invoke None cmd
    let ExecuteReaderArray con (scic:InputC) (f:System.Func<_,_>) = getReaderArrayFromCon con scic.ToSqlCommandInput f.Invoke

let inline private runComplete f cn (sci:Commands.Input) = Connections.runWithConnection cn (flip f sci)

let getNonQuery cn= runComplete Commands.getNonQueryFromCon cn
let getScalar cn= runComplete Commands.getScalarFromCon cn
let getOptScalar cn = runComplete Commands.getOptScalarFromCon cn
let getOptScalarInt cn = runComplete Commands.getOptScalarFromCon cn >> Option.map (fun o -> o :?> int)
let getScalarInt cn= runComplete Commands.getScalarIntFromCon cn
let getScalarIdentity cn= runComplete Commands.getScalarIdentityFromCon cn
let getReaderArray cn sci f= Connections.runWithConnection cn (fun con -> Commands.getReaderArrayFromCon con sci f)
let getReaderArrayLimit limit cn sci f = Connections.runWithConnection cn (fun con -> Commands.getReaderArrayLimitFromCon limit con sci f)
let inline private createScicFromParts cmdText cmdType parameters =
    let scic : Commands.InputC = {Commands.InputC.CommandTextC = cmdText; CommandTypeOpt = cmdType; ParametersOpt = parameters}
    scic
let inline private createSciFromParts cmdText cmdType parameters =
    let scic = createScicFromParts cmdText cmdType parameters
    scic.ToSqlCommandInput

let GetReaderArray cn (scic:Commands.InputC) (f:System.Func<_,_>) = getReaderArray cn scic.ToSqlCommandInput f.Invoke

let ExecuteScalar cmdText cmdType cn parameters =           createSciFromParts cmdText cmdType parameters       |> getScalar cn
let ExecuteScalarInt cmdText cmdType cn parameters =        createSciFromParts cmdText cmdType parameters       |> getScalarInt cn
let ExecuteNullableInt cmdText cmdType cn parameters =      createSciFromParts cmdText cmdType parameters       |> getOptScalarInt cn |> Option.toNullable
let ExecuteScalarIdentity cmdText cmdType cn parameters =   createSciFromParts cmdText cmdType parameters       |> getScalarIdentity cn
let ExecuteNonQuery cmdText cmdType cn parameters =         createSciFromParts cmdText cmdType parameters       |> getNonQuery cn

let ExecuteReaderArray cmdText cmdType cn parameters f =    createScicFromParts cmdText cmdType parameters      |> GetReaderArray cn <| f

/// If you are using Microsoft's Sql Server specifically and need that functionality, or just find it easier to work with fewer generic params
module SqlCommands =
    let inline getSqlCommandInput (scic:Commands.InputC) = scic.ToSqlCommandInput
    let inline createAdapter (cmd:SqlCommand) = new SqlDataAdapter(cmd)
    let inline useSqlCmd (con:SqlConnection) sci f=
        use cmd = con.CreateCommand()
        Commands.prepareCommand sci cmd
        Commands.runWithSqlDiag sci (fun () -> f cmd)

    let getDs cmd tableName =
        let ds = new DataSet()
        use adapter = createAdapter(cmd)
        adapter.Fill( ds, tableName) |> ignore
        ds

    let inline private runComplete f (cn:Connections.Connector) (sci:Commands.Input) = runComplete f cn sci

    // begin ease of use (generic parameters getting to be unwiedly) helpers for C#
    let ExecuteNonQuery cn scic = getSqlCommandInput scic |> runComplete Commands.getNonQueryFromCon cn
    let ExecuteScalar cn scic = getSqlCommandInput scic |> runComplete Commands.getScalarFromCon cn
    let ExecuteScalarInt cn scic = getSqlCommandInput scic |> runComplete Commands.getScalarIntFromCon cn
    let ExecuteScalarIdentity cn scic = getSqlCommandInput scic |> runComplete Commands.getScalarIdentityFromCon cn

    let ExecuteReaderArray scic (f:System.Func<_,_>) cn = getReaderArray cn (getSqlCommandInput scic) f.Invoke
    let ExecuteReaderArraySci commandText commandType cn parametersOpt f = ExecuteReaderArray {CommandTextC = commandText; CommandTypeOpt = commandType; ParametersOpt = parametersOpt} f cn
    let ExecuteTableCon scic sqlCon= useSqlCmd sqlCon (getSqlCommandInput scic) (Commands.executeTable createAdapter)

    let ExecuteDatasetCon scic sqlCon = useSqlCmd sqlCon (getSqlCommandInput scic) (Commands.executeDataset createAdapter)
    let ExecuteDatasetNameCon scic tableName sqlCon = useSqlCmd sqlCon (getSqlCommandInput scic) (Commands.executeDatasetName createAdapter tableName)
    let ExecuteTable scic sqlCn = SqlConnections.runWithConnection sqlCn (fun con -> ExecuteTableCon scic con)
    let ExecuteDataset scic sqlCn = SqlConnections.runWithConnection sqlCn (fun con -> ExecuteDatasetCon scic con)
    let ExecuteTableM cmdText cmdType cn parameters = createScicFromParts cmdText cmdType parameters |> (fun scic -> ExecuteTable scic cn)
    let ExecuteDatasetNameM cmdText cmdType parameters tableName sqlCn =
        let scic = createScicFromParts cmdText cmdType parameters
        SqlConnections.runWithConnection sqlCn (ExecuteDatasetNameCon scic tableName)

module ConnectionTests =
    let openCon timeoutInSecondsOpt cs =
        let sscsb = SqlConnectionStringBuilder(cs)
        printfn "Connection test meta: %i retries, %i timeout" sscsb.ConnectRetryCount sscsb.ConnectTimeout
        timeoutInSecondsOpt 
        |> Option.iter(fun timeoutSeconds ->
            sscsb.ConnectTimeout <- timeoutSeconds 
        )
        let targetCs = sscsb.ToString()
        printfn "Connection test to %s/%s" sscsb.DataSource sscsb.InitialCatalog
        use con = new SqlConnection(targetCs)
        //con.ConnectionTimeout <- 200
        try
            con.Open ()
        with _ ->
            printfn "Connection failed"
            reraise()
        printfn "Connection tested successfully!"

    [<NoComparison>]
    type GetDbNamesResult = 
        | Success of string seq
        | ConFailed of exn
    let getSuccess = 
        function
        | Success x -> Some x
        | _ -> None
    
    let getDbNames cn = 
        //type Input = {CommandText=; CommandType=; OptParameters=}
        let cmdText = "SELECT name FROM master.dbo.sysdatabases where name not in('msdb', 'tempdb', 'master', 'model')"
        try
            getReaderArray cn {CommandText=cmdText; CommandType=CommandType.Text; OptParameters=None} (fun r -> getRecordT<string> r "name")
            |> fun x -> GetDbNamesResult.Success x
        with ex ->
            ConFailed ex

