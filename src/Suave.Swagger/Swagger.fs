﻿namespace Suave.Swagger

module Swagger =

  open System
  open System.Collections.Generic
  open System.IO
  open System.IO.Compression

  open Suave
  open Suave.Operators
  open Suave.Filters
  open Suave.Writers
  open Suave.Sockets.Control
  open Suave.Successful
  open Suave.Logging
  open Suave.EventSource
  open Suave.Files
  open Suave.State.CookieStateStore
  open Suave.Sockets.AsyncSocket

  open Newtonsoft.Json
  open Newtonsoft.Json.Serialization
  open Newtonsoft.Json.Linq
  open Rest
  open Suave.Filters
  open Suave.RequestErrors
  open System.Xml.Serialization
  open Suave.Sockets

  type Path=string

  type FormatParsed =
    | StringPart | CharPart | BoolPart | IntPart
    | DecimalPart | HexaPart
  type FormatPart =
    | Constant  of string
    | Parsed    of FormatParsed
  type FormatParser =
    { Parts:FormatPart list ref
      Buffer:char list ref
      Format:string
      Position:int ref }
    static member Parse f =
        { Parts = ref List.empty
          Buffer = ref List.empty
          Format = f
          Position = ref 0 }.Parse()
    member x.Acc (s:string) =
        x.Buffer := !x.Buffer @ (s.ToCharArray() |> Seq.toList)
    member x.Acc (c:char) =
        x.Buffer := !x.Buffer @ [c]
    member private x.Finished () =
        !x.Position >= x.Format.Length
    member x.Next() =
        if x.Finished() |> not then
            x.Format.Chars !x.Position |> x.Acc
            x.Position := !x.Position + 1
    member x.PreviewNext() =
        if !x.Position >= x.Format.Length - 1
        then None
        else Some (x.Format.Chars (!x.Position))
    member x.Push t =
        x.Parts := !x.Parts @ t
        x.Buffer := List.empty
    member x.StringBuffer skip =
        let c = !x.Buffer |> Seq.skip skip |> Seq.toArray
        new String(c)
    member x.Parse () =
        while x.Finished() |> not do
            x.Next()
            match !x.Buffer with
            | '%' :: '%' :: _ -> x.Push [Constant (x.StringBuffer 1)]
            | '%' :: 'b' :: _ -> x.Push [Parsed BoolPart]
            | '%' :: 'i' :: _
            | '%' :: 'u' :: _
            | '%' :: 'd' :: _ -> x.Push [Parsed IntPart]
            | '%' :: 'c' :: _ -> x.Push [Parsed StringPart]
            | '%' :: 's' :: _ -> x.Push [Parsed StringPart]
            | '%' :: 'e' :: _
            | '%' :: 'E' :: _
            | '%' :: 'f' :: _
            | '%' :: 'F' :: _
            | '%' :: 'g' :: _
            | '%' :: 'G' :: _ -> x.Push [Parsed DecimalPart]
            | '%' :: 'x' :: _
            | '%' :: 'X' :: _ -> x.Push [Parsed HexaPart]
            | _ :: _ ->
                let n = x.PreviewNext()
                match n with
                | Some '%' -> x.Push [Constant (x.StringBuffer 0)]
                | _ -> ()
            | _ -> ()
        if !x.Buffer |> Seq.isEmpty |> not then x.Push [Constant (x.StringBuffer 0)]
        !x.Parts

  type JsonWriter with 
    member __.WriteProperty name (value:obj) =
      __.WritePropertyName name
      __.WriteValue value

  type RouteDescriptor =
    { Template: Path
      Description: string
      Summary: string
      OperationId: string
      Produces: string list
      Consumes: string list
      Tags : string list
      Params: ParamDescriptor list
      Verb:HttpVerb
      Responses:IDictionary<int, ResponseDoc> }
    static member Empty =
      //let defaultResponses = dict [ (200, ResponseDoc.Default) ]
      { Template=""; Description=""; Params=[]; Verb=Get; Summary=""
        OperationId=""; Produces=[]; Responses=dict[]; Consumes=[]; Tags = [] }
  and ResponseDoc =
    { Description:string
      Schema:ObjectDefinition option }
    static member Default = {Description="Not documented"; Schema=None}
    member __.IsDefault() = __ = ResponseDoc.Default
  and HttpVerb =
    | Get | Put | Post | Delete | Options | Head | Patch
    override __.ToString() =
      match __ with
      | Get -> "get" | Put -> "put"
      | Post -> "post" | Delete -> "delete"
      | Options -> "options" | Head -> "head"
      | Patch -> "patch"
  and ParamDescriptor =
    { Name:string
      Type:Type option
      In:ParamContainer
      Required:bool }
    static member Named n =
      {Name=n; Type=None; In=Query; Required=true}
  // http://swagger.io/specification/#parameterObject
  and ParamContainer =
    | Query | Header | Path | FormData | Body
    override __.ToString() =
      match __ with
      | Query -> "query" | Header -> "header"
      | Path -> "path" | FormData -> "formData"
      | Body -> "body"

  and WebPartDocumentation =
    { WebPart:WebPart
      Description:RouteDescriptor }
    static member Of w =
      { WebPart=(w >=> Writers.addHeader "Access-Control-Allow-Origin" "*" )
        Description=RouteDescriptor.Empty }

  and ApiDescription =
    { Title:string
      Description:string
      TermsOfService:string
      Version:string
      Contact:Contact
      License:LicenseInfos }
    static member Empty =
      { Title=""; Description=""; TermsOfService=""; Version="";
        Contact=Contact.Empty; License=LicenseInfos.Empty }
  and Contact =
    { Name:string; Url:string; Email:string }
    static member Empty = 
      { Name=""; Url=""; Email=null }
  and LicenseInfos =
    { Name:string; Url:string }
    static member Empty =
      { Name=""; Url="" }
  and ObjectDefinition =
    { Id:string
      Properties:IDictionary<string, PropertyDefinition> }
  and PathDefinition =
    { Summary:string
      Description:string
      OperationId:string
      Consumes:string list
      Produces:string list
      Tags:string list
      Parameters:ParamDefinition list
      Responses:IDictionary<int, ResponseDoc> }
    member __.ShouldSerializeParameters() =
      __.Parameters.Length > 0
  and ApiDescriptionConverter() =
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,_:JsonSerializer) =
          let d = unbox<ApiDescription>(value)

          writer.WriteStartObject()
          
          writer.WriteProperty "title" d.Title
          writer.WriteProperty "description" d.Description
          writer.WriteProperty "termsOfService" d.TermsOfService
          writer.WriteProperty "version" d.Version
          
          if not (d.Contact = Contact.Empty)
          then writer.WriteProperty "contact" d.Contact

          if not (d.License = LicenseInfos.Empty)
          then writer.WriteProperty "license" d.License

          writer.WriteEndObject()
          writer.Flush()
        override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
          unbox ""
        override __.CanConvert(objectType:Type) =
          objectType = typeof<ApiDescription>
  and ResponseDocConverter() =
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,_:JsonSerializer) =
          let rs = unbox<ResponseDoc>(value)

          writer.WriteStartObject()
          writer.WritePropertyName "description"
          writer.WriteValue rs.Description

          writer.WritePropertyName "schema"
          writer.WriteStartObject()
          match rs.Schema with
          | Some sch ->
            writer.WritePropertyName "$ref"
            writer.WriteValue (sprintf "#/definitions/%s" sch.Id)
          | None ->()
          writer.WriteEndObject()

          writer.WriteEndObject()
          writer.Flush()
        override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
          unbox ""
        override __.CanConvert(objectType:Type) =
          objectType = typeof<ResponseDoc>
  and PropertyDefinitionConverter()=
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,_:JsonSerializer) =
          let p = unbox<PropertyDefinition>(value)
          writer.WriteStartObject()
          writer.WriteRawValue (p.ToJson())
          writer.WriteEndObject()
          writer.Flush()
        override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
          unbox ""
        override __.CanConvert(objectType:Type) =
          objectType = typeof<PropertyDefinition>
  and ParamDefinitionConverter()=
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,_:JsonSerializer) =
          let p = unbox<ParamDefinition>(value)
          writer.WriteRawValue (p.ToJson())
          writer.Flush()
        override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
          unbox ""
        override __.CanConvert(objectType:Type) =
          objectType = typeof<ParamDefinition>
  and DefinitionsConverter() =
    inherit JsonConverter()
      override __.WriteJson(writer:JsonWriter,value:obj,serializer:JsonSerializer) =
          let d = unbox<IDictionary<string,ObjectDefinition>>(value)
          writer.WriteStartObject()
          let c = ObjectDefinitionConverter()
          for k in d.Keys do
            writer.WritePropertyName k
            let v = d.Item k
            c.WriteJson(writer, v, serializer)
          writer.WriteEndObject()
          writer.Flush()
      override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
            unbox ""
      override __.CanConvert(objectType:Type) =
        typeof<IDictionary<string,ObjectDefinition>>.IsAssignableFrom objectType
  and ObjectDefinitionConverter() =
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,_:JsonSerializer) =
          let d = unbox<ObjectDefinition>(value)

          writer.WriteStartObject()
          writer.WritePropertyName "type"
          writer.WriteValue "object"
          writer.WritePropertyName "properties"
          
          writer.WriteStartObject()
          for p in d.Properties do
            writer.WritePropertyName p.Key
            writer.WriteRawValue (p.Value.ToJson())
          writer.WriteEndObject()

          writer.WriteEndObject()
          writer.Flush()
        override __.ReadJson(_:JsonReader,_:Type,_:obj,_:JsonSerializer) =
          unbox ""
        override __.CanConvert(objectType:Type) =
          objectType = typeof<ObjectDefinition>

  and PropertyDefinition =
    | Primitive of Type:string*Format:string
    | Ref of ObjectDefinition
    member __.ToJObject() : JObject =
      let v = JObject()
      match __ with
      | Primitive (t,f) ->
          v.Add("type", JToken.FromObject t)
          v.Add("format", JToken.FromObject f)
      | Ref ref ->
          v.Add("$ref", JToken.FromObject <| sprintf "#/definitions/%s" ref.Id)
      v
    member __.ToJson() : string =
      __.ToJObject().ToString()
  and ParamDefinition =
    { Name:string
      Type:PropertyDefinition option
      In:string
      Required:bool }
    member __.ToJObject() : JObject =
      let v = JObject()
      v.Add("name", JToken.FromObject __.Name)
      v.Add("in", JToken.FromObject __.In)
      v.Add("required", JToken.FromObject __.Required)
      match __.Type with
      | Some t ->
          match t with
          | Primitive (t,_) ->
              v.Add("type", JToken.FromObject t)
          | Ref _ ->
              v.Add("schema", t.ToJObject())
      | None -> ()
      v
    member __.ToJson() : string =
      __.ToJObject().ToString()
  and ApiDocumentation =
    { Swagger:string
      Info:ApiDescription
      BasePath:string
      Host:string
      Schemes:string list
      Paths:IDictionary<Path,IDictionary<HttpVerb,PathDefinition>>
      Definitions:IDictionary<string,ObjectDefinition> }
    member __.ToJson() =
      let settings = new JsonSerializerSettings(NullValueHandling = NullValueHandling.Ignore)
      settings.ContractResolver <- new CamelCasePropertyNamesContractResolver()
      settings.Converters.Add(new ApiDescriptionConverter())
      settings.Converters.Add(new ResponseDocConverter())
      settings.Converters.Add(new PropertyDefinitionConverter())
      //settings.Converters.Add(new PropertyDefinitionOptionConverter())
      settings.Converters.Add(new ObjectDefinitionConverter())
      settings.Converters.Add(new DefinitionsConverter())
      settings.Converters.Add(new ParamDefinitionConverter())
      //settings.Converters.Add(new ParamDescriptorConverter())
      JsonConvert.SerializeObject(__, settings)

   module TypeHelpers =
        // https://swagger.io/specification/#dataTypes -> Data Types
        let typeFormatsNames =
            [
              typeof<string>, ("string", "string")
              typeof<int8>, ("integer", "int8")
              typeof<int16>, ("integer", "int16")
              typeof<int32>, ("integer", "int32")
              typeof<int64>, ("integer", "int64")
              typeof<bool>, ("boolean", "")
              typeof<float>, ("number", "double")
              typeof<float32>, ("number", "float")
              typeof<uint8>, ("integer", "int8")
              typeof<uint16>, ("integer", "int16")
              typeof<uint32>, ("integer", "int32")
              typeof<uint64>, ("integer", "int64")
              typeof<DateTime>, ("string", "date-time")
              typeof<byte array>, ("string", "binary")
              typeof<byte list>, ("string", "binary")
              typeof<byte seq>, ("string", "binary")
              typeof<byte>, ("string", "byte")
              typeof<Guid>, ("string", "string")
            ] |> dict

    type Type with
      member this.IsSwaggerPrimitive
        with get () =
          TypeHelpers.typeFormatsNames.ContainsKey this
      member this.FormatAndName
        with get () =
          match this with
          | _ when TypeHelpers.typeFormatsNames.ContainsKey this ->
            Some (TypeHelpers.typeFormatsNames.Item this)
          | _ when this.IsPrimitive ->
            Some (TypeHelpers.typeFormatsNames.Item (typeof<string>))
          | _ -> None

      member this.Describes() : ObjectDefinition =

        let optionalType (t:Type) = 
          if (not t.IsGenericType) || t.GetGenericTypeDefinition() <> typedefof<Option<_>>
          then None
          else
            let arg = t.GenericTypeArguments |> Seq.exactlyOne
            Some arg

        let rec describe (t:Type) = 
          let descProp (tp:Type) name = 
            match tp.FormatAndName with
            | Some (ty,na) ->
                Some (name, Primitive(ty,na))
            | None ->
                let t' = tp
                if t = t'
                then
                  None
                else
                  let d = Ref(describe t')
                  Some (name, d)
          let props =
            t.GetProperties()
            |> Seq.choose (
                fun p ->
                  match optionalType p.PropertyType with
                  | Some t' -> descProp t' p.Name
                  | None -> descProp p.PropertyType p.Name
            ) |> Map
          {Id=t.Name; Properties=props}

        describe this

  let combineUrls (u1:string) (u2:string) =
    let sp = if u2.StartsWith "/" then u2.Substring 1 else u2
    u1 + sp

  let streamWp (stream:Stream) : WebPart =
    fun ctx ->
      let write (conn, _:HttpResult) : SocketOp<Connection> = socket {
            let header = sprintf "Content-Length: %d\r\n" stream.Length
            let! (_, conn) = asyncWriteLn header conn
            do! transferStream conn stream
            return conn
          }
      { ctx with
          response =
            { ctx.response with
                status = {ctx.response.status with code = 200 }
                content = SocketTask write } }
      |> succeed

  let locker = obj()
  let swaggerUiWebPart (swPath:string) (swJsonPath:string) =
    let wp : WebPart =
      fun ctx ->
        let p =
          match ctx.request.url.AbsolutePath.Substring(swPath.Length) with
          | v when String.IsNullOrWhiteSpace v -> "index.html"
          | v -> v

        let streamZipContent () =
          let assembly = System.Reflection.Assembly.GetExecutingAssembly()
          let fs = assembly.GetManifestResourceStream "Suave.Swagger.swagger-ui.zip"
          let zip = new ZipArchive(fs)
          match zip.Entries |> Seq.tryFind (fun e -> e.FullName = p) with
          | Some ze ->
            let headers =
              match defaultMimeTypesMap (System.IO.Path.GetExtension p) with
              | Some mimetype -> ("Content-Type", mimetype.name) :: ctx.response.headers
              | None -> ctx.response.headers
            let write (conn, _) =
              socket {
                  let! (_, conn) = asyncWriteLn (sprintf "Content-Length: %d\r\n" ze.Length) conn 
                  let! conn = flush conn
                  do! transferStream conn (ze.Open())
                  return conn
              }
            if p = "index.html"
            then
              use r = new StreamReader(ze.Open())
              let bytes =
                r.ReadToEnd()
                  .Replace("http://petstore.swagger.io/v2/swagger.json", (combineUrls "/" swJsonPath))
              |> r.CurrentEncoding.GetBytes
              { ctx
                  with
                    response =
                      { ctx.response
                          with
                            status = { ctx.response.status with code = 200 }
                            content = Bytes bytes
                            headers = headers
                      }
              } |> succeed
            else
              { ctx with
                  response =
                    { ctx.response with
                        status = { ctx.response.status with code = 200 }
                        content = SocketTask write
                        headers = headers
                    }
              }
              |> succeed
          | None ->
              ctx |> NOT_FOUND "Ressource not found"
        streamZipContent()
    pathStarts swPath >=> wp
  
  let removeDefaultResponseDoc = 
    Seq.filter (fun (code:int,r:ResponseDoc) -> code <> 200 && not (r.IsDefault()))
  
  let inline toTuple (kv:KeyValuePair<'u,'t>) = kv.Key,kv.Value
  let inline toTuples (kvs:KeyValuePair<'u,'t> seq) = kvs |> Seq.map toTuple

  type DocBuildState =
    { SwaggerJsonPath:string
      SwaggerUiPath:string
      BasePath:string
      Host:string
      Schemes:string list
      Description:ApiDescription
      Routes:WebPartDocumentation list
      Current:WebPartDocumentation
      Models:Type list
      Id:Guid }
    static member Of w =
      { Routes=[]
        Models=[]
        BasePath=null
        Host=null
        Schemes=["http"]
        Current=WebPartDocumentation.Of w
        Id=Guid.NewGuid()
        Description=ApiDescription.Empty
        SwaggerJsonPath="/swagger/v3/swagger.json"
        SwaggerUiPath="/swagger/v3/ui/" }
    member this.Documents (f:RouteDescriptor->RouteDescriptor) : DocBuildState =
      let d = f this.Current.Description
      { this with Current = { this.Current with Description=d } }
    member this.Combine (other:DocBuildState) =
      if this.Id <> other.Id
      then
        { this with
            Routes=((this.Routes @ other.Routes) @ [other.Current] |> List.rev)
            Models=(other.Models @ this.Models |> List.distinct) }
      else
        let o = other.Current.Description
        let state = this.Current.Description
        let d = if o.Description = "" then state.Description else o.Description
        let t = if o.Template = "" then state.Template else o.Template
        let p = o.Params @ state.Params |> List.distinctBy(fun arg -> arg.Name)
        let rs =
          (List.ofSeq other.Current.Description.Responses) @ (List.ofSeq state.Responses)
          |> List.distinctBy(fun kv -> kv.Key)
          |> List.map (fun kv -> kv.Key, kv.Value)
          |> dict
        let m = other.Models @ this.Models |> List.distinct
        { this with
            Routes = (this.Routes @ other.Routes)
            Current =
              {
                this.Current with
                    Description =
                      {
                        this.Current.Description with
                          Description = d
                          Template = t
                          Params = p
                          Responses = rs
                          Consumes = o.Consumes @ state.Consumes |> List.distinct
                          Produces = o.Produces @ state.Produces |> List.distinct
                          Tags = o.Tags @ state.Tags |> List.distinct
                      }
              }
            Models=m
          }
    member this.Describes (f:ApiDescription->ApiDescription) =
      { this with Description=(f this.Description) }
    member __.Documentation
      with get () =
        let docs = __.Current.Description :: (__.Routes |> List.map (fun r -> r.Description))
        let definitions = Dictionary<string,ObjectDefinition>()

        for m in __.Models do
          let d = m.Describes()
          definitions.Add(d.Id, d)

        let paths =
          docs
          |> Seq.groupBy (fun d -> d.Template)
          |> Seq.map (
              fun (k,g) ->
                let vs =
                  seq {
                    for p in g do
                      let par =
                        p.Params
                        |> List.map(
                            fun a ->
                              let d =
                                match a.Type with
                                | Some t when t.IsSwaggerPrimitive ->
                                    match t.FormatAndName with
                                    | Some (ty,na) -> Some(Primitive(ty,na))
                                    | None -> Some(Ref(t.Describes()))
                                | Some t -> Some((Ref <| t.Describes()))
                                | None -> None

                              { Name=a.Name
                                Type=d
                                In=a.In.ToString()
                                Required=a.Required })
                      let rs = 
                        p.Responses
                        |> fun responses ->
                            if responses.Count > 1 && responses |> toTuples |> Seq.exists(function | (200, d) when d.IsDefault() -> true | _ -> false)
                            then responses |> toTuples |> removeDefaultResponseDoc |> Seq.toList |> dict
                            elif responses.Count <= 0
                            then dict [(200, ResponseDoc.Default)]
                            else responses
                      let pa =
                        { Summary=p.Summary
                          Description=p.Description
                          OperationId=p.OperationId
                          Consumes=p.Consumes
                          Produces=p.Produces
                          Parameters=par
                          Responses=rs
                          Tags=p.Tags }
                      yield p.Verb, pa
                  } |> dict
                (k,vs)
            ) |> Seq.toList |> dict

        for d in List.ofSeq definitions.Values do
          for p in d.Properties do
            match p.Value with
            | Ref r ->
              if not <| definitions.ContainsKey r.Id
              then definitions.Add(r.Id, r)
            | _ -> ()

        { Definitions=definitions
          BasePath=__.BasePath; Host=__.Host; Schemes=__.Schemes
          Paths=paths
          Info=__.Description
          Swagger="2.0" }
    member __.App
      with get () =
        let swaggerWebPart =
          path __.SwaggerJsonPath
            >=> OK (__.Documentation.ToJson())
            >=> Writers.setMimeType "application/json; charset=utf-8"
            >=> Writers.addHeader "Access-Control-Allow-Origin" "*"
        let oldSwaggerUiPart = 
          pathStarts "/swagger/v2/ui"
           >=> Redirection.redirect __.SwaggerUiPath
        let uiWebpart = swaggerUiWebPart __.SwaggerUiPath __.SwaggerJsonPath
        choose ((__.Routes |> List.map (fun r -> r.WebPart)) @ [oldSwaggerUiPart;swaggerWebPart;uiWebpart; __.Current.WebPart])

  type SwaggerBuilder () =
    member __.Yield (w:DocBuildState) : DocBuildState =
      w
    member __.For (w:WebPart, func:DocBuildState->DocBuildState) : DocBuildState =
      let b = DocBuildState.Of w
      func b
    member __.For (b:DocBuildState, func:DocBuildState->DocBuildState) : DocBuildState =
      func b
    member __.Combine ((b1,b2):(DocBuildState*DocBuildState)) : DocBuildState =
      b1.Combine b2
    member __.Delay (func:unit->DocBuildState) : DocBuildState =
      func()

  let swagger = new SwaggerBuilder()
