open System
open System.Collections.Generic
open System.IO
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful
open Newtonsoft.Json
open Newtonsoft.Json.Serialization
open ICSharpCode.SharpZipLib.Zip

open Suave.RestFullKit
open Rest
open FunnyDsl
open Swagger

let now : WebPart =
  fun (x : HttpContext) ->
    async {
      return! MODEL DateTime.Now x
    }

type Pet =
  { Id:int
    Name:string
    Category:PetCategory }
and PetCategory = 
  { Id:int
    Name:string }

let substract(a,b) = OK ((a-b).ToString())

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

let bye = GET >=> path "/bye" >=> OK "bye. @++"

let api = 
  swagger {
      
      // syntax 1
      for route in getting (simpleUrl "/time" |> thenReturns now) do
        yield description Of route is "What time is it ?"

      // another syntax
//      for route in getOf (path "/time2" >=> now) do
//        yield description Of route is "What time is it 2 ?"
//        yield urlTemplate Of route is "/time2"

//      for route in getting <| urlFormat "/substract/%d/%d" substract do
//        yield description Of route is "Substracts two numbers"
//
//      for route in posting <| urlFormat "/substract/%d/%d" substract do
//        yield description Of route is "Substracts two numbers"

      for route in getting <| urlFormat "/pet/%d" findPetById do
        yield description Of route is "Search a pet by id"
        yield route |> addResponse 200 "The found pet" (Some typeof<Pet>)
      
      for route in getting <| urlFormat "/category/%d" findCategoryById do
        yield description Of route is "Search a category by id"
        yield route |> addResponse 200 "The found category" (Some typeof<PetCategory>)
      
//       Classic routes with manual documentation

//      for route in bye do
//        yield route.Documents(fun doc -> { doc with Description = "Say good bye." })
//        yield route.Documents(fun doc -> { doc with Template = "/bye"; Verb=Get })
//
//      for route in getOf (pathScan "/add/%d/%d" (fun (a,b) -> OK((a + b).ToString()))) do
//        yield description Of route is "Compute a simple addition"
//        yield urlTemplate Of route is "/add/{number1}/{number2}"
//        yield parameter "number1" Of route (fun p -> { p with TypeName = "integer"; In=Path })
//        yield parameter "number2" Of route (fun p -> { p with TypeName = "integer"; In=Path })
//
//      for route in getOf (path "/hello" >=> OK "coucou") do
//        yield description Of route is "Say hello"
//        yield urlTemplate Of route is "/hello"
    }
  |> fun a ->
      a.Describes(
        fun d -> 
          { 
            d with 
                Title = "Swagger and Suave.io"
                Description = "A simple swagger with Suave.io example"
          })


[<EntryPoint>]
let main argv = 
  async {
    do! Async.Sleep 1000
    System.Diagnostics.Process.Start "http://localhost:8083/swagger/v2/ui/index.html" |> ignore
  } |> Async.Start
  startWebServer defaultConfig api.App
  0 // return an integer exit code
