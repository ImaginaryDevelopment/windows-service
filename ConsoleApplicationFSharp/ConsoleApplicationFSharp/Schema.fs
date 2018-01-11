
[<AutoOpen>]
module SuaveFSharp.Schema

type ServiceDetails = {Start: unit -> unit; Stop: unit -> unit}
type Settings = {ConnectionString : string}
type User = {IsLockedOut:bool; LoginAttempts:int; PasswordHash:string}