(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"../../packages/Suave/lib/net40"
#I @"../../bin/Suave.Swagger"

#r "Suave.dll"
#r "Suave.Swagger.dll"

open System
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful


(**
Rest module
==========

This small module contains helpers for JSON and XML serialization.

*)

open Suave.Swagger
open Rest
open FunnyDsl
open Swagger

[<CLIMutable>]
type PetCategory = 
  { Id:int
    Name:string }

(**
Route returning a PetCategory as JSON:
*)

let r1 = GET >=> path "/cat" >=> JSON { Id=45; Name="category 45" }

(**
Route returning a PetCategory as XML:
*)

let r2 = GET >=> path "/cat" >=> XML { Id=45; Name="category 45" }

(**

Allmost REST APIs response's format depends on "Accept" header sent in the request

The MODEL helper will do it.

*)

let r2 = GET >=> path "/cat" >=> MODEL { Id=45; Name="category 45" }


(**

We can also simply read the body stream as JSON serialized object

*)

let r2 =  
  POST 
    >=> path "/cat" 
    >=> JsonBody<PetCategory>(fun model -> MODEL { model with Id=(Random().Next()) })







