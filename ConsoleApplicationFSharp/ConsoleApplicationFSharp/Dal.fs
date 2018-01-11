namespace SuaveFSharp.Dal

module DataAccess =
    module Users = 
        open SuaveFSharp.Schema
        open System
        open AdoHelper.Connections

        let getUserId (_cn:Connector) _username = 
            0
        ()
        let updateAccessAttempts (_userId:int) (_cn:Connector) =
            Unchecked.defaultof<User>, DateTime.MinValue
        let loginSuccess cn userId =
            ()
    ()