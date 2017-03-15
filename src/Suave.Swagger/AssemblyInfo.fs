namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Suave.Swagger")>]
[<assembly: AssemblyProductAttribute("Suave.Swagger")>]
[<assembly: AssemblyDescriptionAttribute("This is an extension for Suave.io with some REST tools and Swagger documentation helpers")>]
[<assembly: AssemblyVersionAttribute("1.0.1")>]
[<assembly: AssemblyFileVersionAttribute("1.0.1")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.1"
    let [<Literal>] InformationalVersion = "1.0.1"
