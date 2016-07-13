namespace Suave.RestFullKit

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
open Suave.RequestErrors
  open System.Xml.Serialization


module Serialization =
  let toJson o =
    let jsonSerializerSettings = new JsonSerializerSettings()
    jsonSerializerSettings.ContractResolver <- new CamelCasePropertyNamesContractResolver()
    JsonConvert.SerializeObject(o, jsonSerializerSettings)

module Rest =

  let JSON v =
    v |> Serialization.toJson |> OK
    >=> Writers.setMimeType "application/json; charset=utf-8"
    >=> Writers.addHeader "Access-Control-Allow-Origin" "*"
  let XML (v:'t) : WebPart =
    let w =
     fun (ctx : HttpContext) ->
        async {
          let serializer = new XmlSerializer(typeof<'t>)
          use memory = new MemoryStream()
          serializer.Serialize(memory, v)
          memory.Position <- 0L
          let bytes = (Bytes (memory.ToArray()))
          let context =
           { ctx 
              with 
                response = 
                  { ctx.response 
                      with 
                        status = Suave.Http.HTTP_200
                        content = bytes
                  }
           } |> succeed
          return! context
        }
    w >=> Writers.setMimeType "application/xml; charset=utf-8"

  let MODEL (m:'t) : WebPart =
    fun (x : HttpContext) ->
      async {
        let accept = 
          x.request.headers
          |> List.tryFind (
              fun (h,_) -> 
                h.Equals("Accept", StringComparison.InvariantCultureIgnoreCase))
        match accept with
        | Some (_,"application/xml") -> 
          return! XML m x
        | _ -> 
          return! JSON m x
      }
