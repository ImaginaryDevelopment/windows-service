module SuaveFSharp.Domain
open System
module Users =
    type ValidLoginResult =
        | Success
        | MustChangePassword
        | Failure
        | Abort

    let maxLoginAttempts = 5
    let pwdChangeBound (today:DateTime) = today.AddDays(-90.)

    let validateLogin loginAttempts isLockedOut (hashedPassword,userPassword) (lastPwdChange,today) =
        let isPwdExpired = lastPwdChange < pwdChangeBound today
        match loginAttempts > maxLoginAttempts || isLockedOut, isPwdExpired, hashedPassword = userPassword with
        | false, true, true -> MustChangePassword
        | false,false,true ->
                    Success
        | false, _,false -> Failure
        | true, _, _ -> Abort
