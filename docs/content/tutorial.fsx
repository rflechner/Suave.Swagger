(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"../../packages/Suave/lib/net40"
#I @"../../bin/Suave.Swagger"

#r "Suave.dll"
#r "Suave.Swagger.dll"


(**
Scenario 1
==========

We want to create an API returning current time.

Simple solution
---------------

*)

open System
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful

let now1 : WebPart =
  fun (x : HttpContext) ->
    async {
      return! OK (DateTime.Now.ToString()) x
    }
[<EntryPoint>]
let main argv = 
  let time1 = GET >=> path "/time1" >=> now1
  startWebServer defaultConfig time1
  0 // return an integer exit code

(**

Go to http://localhost:8083/time1


Documented solution
-------------------

*)
open Suave.Swagger
open Rest
open FunnyDsl
open Swagger
open Suave
open System

let now : WebPart =
  fun (x : HttpContext) ->
    async {
      // The MODEL helper checks the "Accept" header 
      // and switches between XML and JSON format
      return! MODEL DateTime.Now x
    }

let api1 = 
  swagger {
    // syntax 1
    for route in getting (simpleUrl "/time" |> thenReturns now) do
      yield description Of route is "What time is it ?"
  }

[<EntryPoint>]
let main argv = 
  startWebServer defaultConfig api1.App
  0 // return an integer exit codexit code

(**

Go to http://localhost:8083/swagger/v2/ui/index.html

![Swagger UI 1](images/screen1.gif)

***

Scenario 2
==========

Creating a service returning calculation results

*)

let substract(a,b) = OK ((a-b).ToString())

let api2 = 
  swagger {
    // For GET
    for route in getting <| urlFormat "/substract/%d/%d" substract do
      yield description Of route is "Substracts two numbers"
    // For POST
    for route in posting <| urlFormat "/substract/%d/%d" substract do
      yield description Of route is "Substracts two numbers"

    // the "add" function can be manually documented
    for route in getOf (pathScan "/add/%d/%d" (fun (a,b) -> OK((a+b).ToString()))) do
      yield description Of route is "Compute a simple addition"
      yield urlTemplate Of route is "/add/{number1}/{number2}"
      yield parameter "number1" Of route  
        (fun p -> { p with Type = (Some typeof<int>); In=Path })
      yield parameter "number2" Of route 
        (fun p -> { p with Type = (Some typeof<int>); In=Path })
}

(**

- urlFormat will extract parameters from the PrintfFormat, documente them and build the Suave's native "pathScan"
- we can use directly a pathScan and manually document the pathScan route.

![Swagger UI 2](images/screen2.gif)

Scenario 3
==========

Returning models

*)

[<CLIMutable>] //important for XML serialization
type Pet =
  { Id:int
    Name:string
    Category:PetCategory }
and [<CLIMutable>] PetCategory = 
  { Id:int
    Name:string }

(**

REST functions simulating a database access:

*)

let findPetById id = 
  MODEL
    { 
      Id=id; Name=(sprintf "pet_%d" id)
      Category = { Id=id*100; Name=(sprintf "cat_%d" id) }
    }

let findCategoryById id = 
  MODEL
    { 
      Id=id; Name=(sprintf "cat_%d" id)
    }

let createCategory =
  JsonBody<PetCategory>(fun model -> MODEL { model with Id=(Random().Next()) })

(**

API:

*)

let api3 = 
  swagger {
      for route in getting <| urlFormat "/pet/%d" findPetById do
        yield description Of route is "Search a pet by id"

        // adding responses informations for HttpCode 200
        yield route |> addResponse 200 "The found pet" (Some typeof<Pet>)
        // the type Pet and referenced PetCategory will be added to swagger definitions

        // supported requests and responses formats
        yield route |> produces "application/json"
        yield route |> produces "application/xml"
        yield route |> consumes "application/json"
        yield route |> consumes "application/xml"

        // can be written shortly
        yield route |> supportsJsonAndXml
      
      for route in getting <| urlFormat "/category/%d" findCategoryById do
        yield description Of route is "Search a category by id"
        yield route 
          |> addResponse 200 "The found category" (Some typeof<PetCategory>)

      for route in posting <| simpleUrl "/category" |> thenReturns createCategory do
        yield description Of route is "Create a category"
        yield route |> addResponse 200 "returns the create model with assigned Id" (Some typeof<PetCategory>)
        yield parameter "category model" Of route (fun p -> { p with Type = (Some typeof<PetCategory>); In=Body })
    }
  
[<EntryPoint>]
let main argv = 
  startWebServer defaultConfig api3.App
  0

(**

![Swagger UI 3](images/screen3_2.gif)

*)


