namespace Suave.Swagger

open System
open System.Collections.Generic
open System.IO
open Suave
open Suave.Operators
open Suave.Filters
open Suave.Writers
open Suave.Successful
open Microsoft.FSharpLu.Json

open Suave.RequestErrors
open System.Xml.Serialization


module Rest =

  let JSON v =
    v |> Compact.serialize |> OK
    >=> Writers.setMimeType "application/json; charset=utf-8"
    //>=> Writers.addHeader "Access-Control-Allow-Origin" "*"
    >=> Writers.addHeader "Origin" "*"
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
                        status = { ctx.response.status with code = 200 }
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

  let inline JsonBody< ^T > (f: ^T -> WebPart) : WebPart =
    fun (x : HttpContext) ->
      async {
        let json = System.Text.Encoding.UTF8.GetString x.request.rawForm
        let model = Microsoft.FSharpLu.Json.Compact.deserialize< ^T > json
      
        return! (f model) x
      }
