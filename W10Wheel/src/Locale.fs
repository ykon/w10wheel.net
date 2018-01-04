module Locale

open System.Diagnostics
open System.Globalization

let getLanguage () =
    match CultureInfo.CurrentUICulture.TwoLetterISOLanguageName with
    | "ja" -> "ja" // Japanese
    | _ -> "en" // Other

let convLang lang msg =
    match lang with
    | DataID.English -> msg
    | DataID.Japanese -> LocaleData.get_ja msg
    | _ ->
        Debug.WriteLine ("Not supported language: " + lang)
        msg