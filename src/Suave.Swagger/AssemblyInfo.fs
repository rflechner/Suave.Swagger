namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Suave.RestFullKit")>]
[<assembly: AssemblyProductAttribute("Suave.RestFullKit")>]
[<assembly: AssemblyDescriptionAttribute("This is an extension for Suave.io with some REST tools and Swagger documentation helpers")>]
[<assembly: AssemblyVersionAttribute("1.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0"
    let [<Literal>] InformationalVersion = "1.0"
