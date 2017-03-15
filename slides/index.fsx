(**
- title : Suave.Swagger
- description : A solution to use Swagger with Suave
- author : Romain Flechner
- theme : night
- transition : default

*)

(*** hide ***)

#I @"../packages/Suave/lib/net40"
#I @"../bin/Suave.Swagger"

#r "Suave.dll"
#r "Suave.Swagger.dll"

(**

# Documenting Suave REST APIs

Using Swagger with Suave

***

## What are Suave and Swagger?

- [Suave](https://suave.io/) is a F# lightweight web server principally used to develop REST APIs
- [Swagger](http://swagger.io/) is a tool for creating interactive documentation, client SDK generation and discoverability.

***

## Why use Swagger?

- Generate beautiful API documentation, including a UI to explore and test operations.
- Improve productivity during development.
- Make it possible for API users to generate clients.

***

## Scenario 1

We want to create an API returning current time.

---

### Simple solution

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
let main1 argv = 
  let time1 = GET >=> path "/time1" >=> now1
  startWebServer defaultConfig time1
  0 // return an integer exit code

(**

Go to http://localhost:8083/time1

---

### Documented solution

*)
open Suave.Swagger
open Rest
open FunnyDsl
open Swagger

let now : WebPart =
  fun (x : HttpContext) ->
    async {
      // The MODEL helper checks the "Accept" header 
      // and switches between XML and JSON format
      return! MODEL DateTime.Now x
    }

(**
---
#### Problem

Suave's WebParts are combined functions and it's not possible to get route format and parameters with reflection.

---
#### Solution

Creating a DSL with a tiny computation expression

*)

let api1 = 
  swagger {
    // syntax 1
    for route in getting (simpleUrl "/time" |> thenReturns now) do
      yield description Of route is "What time is it?"
  }

[<EntryPoint>]
let main argv = 
  startWebServer defaultConfig api1.App
  0 // return an integer exit code

(**
---
Go to http://localhost:8083/swagger/v2/ui/index.html

![Swagger UI 1](images/screen1.gif)

***

## Scenario 2

Creating a service returning calculation results

---

#### Code

*)


let subtract(a,b) = OK ((a-b).ToString())

let api2 = 
  swagger {
    // For GET
    for route in getting <| urlFormat "/subtract/%d/%d" subtract do
      yield description Of route is "Subtracts two numbers"
    // For POST
    for route in posting <| urlFormat "/subtract/%d/%d" subtract do
      yield description Of route is "Subtracts two numbers"

    // the "add" function can be manually documented
    for route in getOf (pathScan "/add/%d/%d" (fun (a,b) -> OK((a+b).ToString()))) do
      yield description Of route is "Compute a simple addition"
      yield urlTemplate Of route is "/add/{number1}/{number2}"
      yield parameter "number1" Of route  
        (fun p -> { p with TypeName = "integer"; In=Path })
      yield parameter "number2" Of route 
        (fun p -> { p with TypeName = "integer"; In=Path })
}

(**

---

#### Some comments

- urlFormat will extract parameters from the PrintfFormat, documente them and build the Suave's native "pathScan"
- we can use directly a pathScan and manually document the pathScan route.

---

![Swagger UI 2](images/screen2.gif)

*)

(**

***

## Scenario 3

Returning models

---

#### Models will be:
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
---

#### REST functions simulating a database access:

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

(**
---

#### API

*)

let api3 = 
  swagger {
      for route in getting <| urlFormat "/pet/%d" findPetById do
        yield description Of route is "Search a pet by id"
        yield route |> addResponse 200 "The found pet" (Some typeof<Pet>)
        yield route |> produces "application/json"
        yield route |> produces "application/xml"
        yield route |> consumes "application/json"
        yield route |> consumes "application/xml"
      
      for route in getting <| urlFormat "/category/%d" findCategoryById do
        yield description Of route is "Search a category by id"
        yield route 
          |> addResponse 200 "The found category" (Some typeof<PetCategory>)
    }
  
[<EntryPoint>]
let main argv = 
  startWebServer defaultConfig api3.App
  0

(**
---

![Swagger UI 3](images/screen3.gif)

*)


