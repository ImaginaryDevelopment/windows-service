[<AutoOpen>]
module SuaveFSharp.Schema

open System

type ServiceDetails = {Start: unit -> unit; Stop: unit -> unit}
let tryFSwallow f =
    try
        f() |> Some
    with _ -> None
let replace (d:string) (r:string) (x:string) =
    x.Replace(d,r)
let private failNullOrEmpty paramName x = if String.IsNullOrEmpty x then raise <| ArgumentOutOfRangeException paramName else x

[<AutoOpen>]
module FunctionalHelpersAuto =
    let cprintf c fmt = // https://blogs.msdn.microsoft.com/chrsmith/2008/10/01/f-zen-colored-printf/
        Printf.kprintf (fun s ->
            let old = System.Console.ForegroundColor
            try
                System.Console.ForegroundColor <- c
                System.Console.Write s
            finally
                System.Console.ForegroundColor <- old
            ) fmt
    let cprintfn c fmt =
        Printf.kprintf (fun s ->
            let old = System.Console.ForegroundColor
            try
                System.Console.ForegroundColor <- c
                System.Console.WriteLine s
            finally
                System.Console.ForegroundColor <- old
            ) fmt
    let teeTuple f x = x, f x
    /// take a dead-end function and curry the input
    let tee f x = f x; x
    // take a value and adjust it to fall within the range of vMin .. vMax
    let clamp vMin vMax v =
        max v vMin
        |> min vMax

    /// super handy with operators like (*) and (-)
    /// take a function that expects 2 arguments and flips them before applying to the function
    let inline flip f x y = f y x
    /// take a tuple and apply the 2 arguments one at a time (from haskell https://www.haskell.org/hoogle/?hoogle=uncurry)
    let uncurry f (x,y) = f x y
    /// does not work with null x
    let inline getType x = x.GetType()
    // based on http://stackoverflow.com/a/2362114/57883
    // mimic the C# as keyword
    let castAs<'t> (o:obj): 't option =
        match o with
        | :? 't as x -> Some x
        | _ -> None
    // long pipe chains don't allow breakpoints anywhere inside
    // does this need anything to prevent the method from being inlined/optimized away?
    let breakpoint x =
        let result = x
        result
    let breakpointf f x =
        let result = f x
        result

    // allows you to pattern match against non-nullables to check for null (in case c# calls)
    let (|NonNull|UnsafeNull|) x =
        match box x with
        | null -> UnsafeNull
        | _ -> NonNull

    // for statically typed parameters in an active pattern see: http://stackoverflow.com/questions/7292719/active-patterns-and-member-constraint
    //consider pulling in useful functions from https://gist.github.com/ruxo/a9244a6dfe5e73337261
    let cast<'T> (x:obj) = x :?> 'T

type System.String with
    // the default insensitive comparison
    static member defaultIComparison = StringComparison.InvariantCultureIgnoreCase
    static member substring i (x:string) = x.Substring i
    static member substring2 i e (x:string)= x.Substring(i,e)
    static member before (delimiter:string) s = s |> String.substring2 0 (s.IndexOf delimiter)
    static member equalsI (x:string) (x2:string) = not <| isNull x && not <| isNull x2 && x.Equals(x2, String.defaultIComparison)
let before delim x = System.String.before delim x
let after (delimiter:string) (x:string) =
    failNullOrEmpty "x" x
    |> tee (fun _ -> failNullOrEmpty "delimiter" delimiter |> ignore)
    |> fun x ->
        match x.IndexOf delimiter with
        | i when i < 0 -> failwithf "after called without matching substring in '%s'(%s)" x delimiter
        | i -> x |> String.substring (i + delimiter.Length)

let (|StringEqualsI|_|) s1 (toMatch:string) = if String.equalsI toMatch s1 then Some() else None

let hoist f x =
    f()
    x
