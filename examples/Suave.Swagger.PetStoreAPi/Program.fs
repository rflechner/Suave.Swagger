open System
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful

open Suave.Swagger
open Rest
open FunnyDsl
open Swagger

let now1 : WebPart =
  fun (x : HttpContext) ->
    async {
      return! OK (DateTime.Now.ToString()) x
    }

let now : WebPart =
  fun (x : HttpContext) ->
    async {
      // The MODEL helper checks the "Accept" header 
      // and switches between XML and JSON format
      return! MODEL DateTime.Now x
    }

[<CLIMutable>]
type Pet =
  { Id:int
    Uuid:Guid
    Name:string
    Category:PetCategory }
and [<CLIMutable>] PetCategory = 
  { Id:int
    Name:string }

[<CLIMutable>]
type SubtractionRequest =
  { First:int
    Second:int
  }

[<CLIMutable>]
type SubtractionResult = { Result:int }

let createCategory =
  JsonBody<PetCategory>(fun model -> MODEL { model with Id=(Random().Next()) })



let subtract(a,b) = OK ((a-b).ToString())

let subtractObj =
  JsonBody<SubtractionRequest>(fun {First=a;Second=b} -> MODEL {Result=(a-b)})

let findPetById id = 
  MODEL
    { 
      Id=id; Name=(sprintf "pet_%d" id); Uuid=(Guid.NewGuid())
      Category = { Id=id*100; Name=(sprintf "cat_%d" id) }
    }
let findPetByUuid (id:string) = 
  MODEL
    { 
      Id=1; Name="pet_1"; Uuid=(Guid.Parse(id))
      Category = { Id=100; Name="cat_0" }
    }

let findCategoryById id = 
  MODEL
    { 
      Id=id; Name=(sprintf "cat_%d" id)
    }

let time1 = GET >=> path "/time1" >=> now
let bye = GET >=> path "/bye" >=> OK "bye. @++"

let bye2 = GET >=> path "/bye2" >=> JSON "bye. @++"
let bye3 = GET >=> path "/bye3" >=> XML "bye. @++"

let api = 
  swagger {
      //// syntax 1
      for route in getting (simpleUrl "/time" |> thenReturns now) do
        yield description Of route is "What time is it ?"
        yield route |> tag "time"

      for route in getting (urlFormat "/bonjour/%s" (fun x -> OK (sprintf "Bonjour %s" x))) do
        yield description Of route is "Say hello in french"

      // another syntax
      for route in getOf (path "/time2" >=> now) do
        yield description Of route is "What time is it 2?"
        yield urlTemplate Of route is "/time2"
        yield route |> tag "time"

      for route in getting <| urlFormat "/subtract/%d/%d" subtract do
        yield description Of route is "Subtracts two numbers"
        yield route |> tag "maths"

      for route in posting <| simpleUrl "/subtract" |> thenReturns subtractObj do
        yield description Of route is "Subtracts two numbers"
        yield route |> addResponse 200 "Subtraction result" (Some typeof<SubtractionResult>)
        yield parameter "subtraction request" Of route (fun p -> { p with Type = (Some typeof<SubtractionRequest>); In=Body })
        yield route |> tag "maths"

      for route in posting <| urlFormat "/subtract/%d/%d" subtract do
        yield description Of route is "Subtracts two numbers"
        yield route |> tag "maths"

      for route in getting <| urlFormat "/pet/%d" findPetById do
        yield description Of route is "Search a pet by id"
        yield route |> addResponse 200 "The found pet" (Some typeof<Pet>)
        yield route |> supportsJsonAndXml
        yield route |> tag "pets"
      
      for route in getOf (pathScan "/pet/byuuid/%s" findPetByUuid) do
        yield description Of route is "Search a pet by uuid"
        yield urlTemplate Of route is "/pet/byuuid/{uuid}"
        yield parameter "UUID" Of route (fun p -> { p with Type = (Some typeof<Guid>); In=Path })
        yield route |> addResponse 200 "The found pet" (Some typeof<Pet>)
        yield route |> supportsJsonAndXml
        yield route |> tag "pets"
      
      for route in getting <| urlFormat "/category/%d" findCategoryById do
        yield description Of route is "Search a category by id"
        yield route |> addResponse 200 "The found category" (Some typeof<PetCategory>)
        yield route |> tag "pets"
      
      for route in posting <| simpleUrl "/category" |> thenReturns createCategory do
        yield description Of route is "Create a category"
        yield route |> addResponse 200 "returns the create model with assigned Id" (Some typeof<PetCategory>)
        yield parameter "category model" Of route (fun p -> { p with Type = (Some typeof<PetCategory>); In=Body })
        yield route |> tag "pets"

//       Classic routes with manual documentation

      for route in bye do
        yield route.Documents(fun doc -> { doc with Description = "Say good bye." })
        yield route.Documents(fun doc -> { doc with Template = "/bye"; Verb=Get })

      for route in getOf (pathScan "/add/%d/%d" (fun (a,b) -> OK((a + b).ToString()))) do
        yield description Of route is "Compute a simple addition"
        yield urlTemplate Of route is "/add/{number1}/{number2}"
        yield parameter "number1" Of route (fun p -> { p with Type = (Some typeof<int>); In=Path })
        yield parameter "number2" Of route (fun p -> { p with Type = (Some typeof<int>); In=Path })

      for route in getOf (path "/hello" >=> OK "coucou") do
        yield description Of route is "Say hello"
        yield urlTemplate Of route is "/hello"
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
    do! Async.Sleep 2000
    System.Diagnostics.Process.Start "http://localhost:8082/swagger/v2/ui/index.html" |> ignore
  } |> Async.Start
  
  startWebServer { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" 8082 ] } api.App
  0 // return an integer exit code

