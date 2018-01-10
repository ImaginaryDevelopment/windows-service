module SuaveFSharp.Schema
type ServiceDetails = {Start: unit -> unit; Stop: unit -> unit}
let tryFSwallow f =
    try
        f() |> Some
    with _ -> None

