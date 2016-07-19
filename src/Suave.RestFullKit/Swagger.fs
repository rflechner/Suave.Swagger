namespace Suave.RestFullKit

module Swagger =

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
  open Newtonsoft.Json.Linq
  open ICSharpCode.SharpZipLib.Zip
  open Rest
  open Suave.Filters
  open Suave.RequestErrors
  open System.Xml.Serialization

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

  type RouteDescriptor = 
    { Template: Path
      Description: string
      Summary: string
      OperationId: string
      Produces: string list
      Params: ParamDescriptor list
      Verb:HttpVerb
      Responses:IDictionary<int, ResponseDoc> }
    static member Empty =
      { Template=""; Description=""; Params=[]; Verb=Get; Summary=""
        OperationId=""; Produces=[]; Responses=dict Seq.empty }
  and ResponseDoc = 
    { Description:string
      Schema:ObjectDefinition option }
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
      TypeName:string
      In:ParamContainer
      Required:bool }
    static member Named n =
      {Name=n; TypeName=""; In=Query; Required=true}
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
      { Name=""; Url=""; Email="" }
  and LicenseInfos =
    { Name:string; Url:string }
    static member Empty = 
      { Name=""; Url="" }
  and ObjectDefinition =
    { Id:string
      Properties:IDictionary<string, PropertyDefinition> }
//    member __.ToJObject() =
//      let token = JToken.FromObject
//      let o = JObject()
//      let props = JObject()
//      o.Add("type", token "object")
//      for p in __.Properties do
//        match p.Value with
//        | Primitive (t,f) ->
//            let v = JObject()
//            v.Add("type", token t)
//            v.Add("format", token f)
//            props.Add(p.Key, v)
//        | Ref ref ->
//            props.Add("$ref", token <| sprintf "#/definitions/%s" ref.Id)
//      o.Add("properties", props)
//      o

  and PathDefinition =
    { Summary:string
      Description:string
      OperationId:string
      Consumes:string list
      Parameters:ParamDefinition list
      Responses:IDictionary<int, ResponseDoc> }
  and ResponseDocConverter() =
    inherit JsonConverter()
        override __.WriteJson(writer:JsonWriter,value:obj,serializer:JsonSerializer) =
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
        override __.ReadJson(reader:JsonReader,objectType:Type,existingValue:obj,serializer:JsonSerializer) =
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
          
          writer.WritePropertyName "id"
          writer.WriteValue d.Id

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
      Type:string
      In:string
      Required:bool }
  and ApiDocumentation =
    { Swagger:string
      Info:ApiDescription
      BasePath:string
      Host:string
      Schemes:string list
      Paths:IDictionary<Path,IDictionary<HttpVerb,PathDefinition>>
      Definitions:IDictionary<string,ObjectDefinition> }
    member __.ToJson() =
      let settings = new JsonSerializerSettings()
      settings.ContractResolver <- new CamelCasePropertyNamesContractResolver()
      settings.Converters.Add(new ResponseDocConverter())
      settings.Converters.Add(new PropertyDefinitionConverter())
      settings.Converters.Add(new ObjectDefinitionConverter())
      settings.Converters.Add(new DefinitionsConverter())
      JsonConvert.SerializeObject(__, settings)
//      let tmp = JsonConvert.SerializeObject(__, settings)
//      let json = JObject.Parse tmp
//      json.Remove "definitions" |> ignore
//      let definitions = JObject()
//      for def in __.Definitions do
//        let token = def.Value.ToJObject()
//        definitions.Add(def.Key, token)
//      json.Add("definitions", definitions)
//      json.ToString()

   module TypeHelpers =
        //http://swagger.io/specification/ -> Data Types
        let typeFormatsNames = 
            [
              typeof<string>, ("string", "string")
              typeof<int8>, ("integer", "int8")
              typeof<int16>, ("integer", "int16")
              typeof<int32>, ("integer", "int32")
              typeof<int64>, ("integer", "int64")
              typeof<bool>, ("boolean", "")
              typeof<float>, ("float", "float32")
              typeof<float32>, ("float", "float32")
              typeof<uint8>, ("integer", "int8")
              typeof<uint16>, ("integer", "int16")
              typeof<uint32>, ("integer", "int32")
              typeof<uint64>, ("integer", "int64")
              typeof<DateTime>, ("string", "date-time")
              typeof<byte array>, ("string", "binary")
              typeof<byte list>, ("string", "binary")
              typeof<byte seq>, ("string", "binary")
              typeof<byte>, ("string", "byte")
            ] |> dict

    type Type with
      member this.FormatAndName
        with get () = 
          match this with
          | _ when TypeHelpers.typeFormatsNames.ContainsKey this -> 
            Some (TypeHelpers.typeFormatsNames.Item this)
          | _ when this.IsPrimitive ->
            Some (TypeHelpers.typeFormatsNames.Item (typeof<string>))
          | _ -> None

      member this.Describes() =
        let props = 
          this.GetProperties()
          |> Seq.map (
              fun p -> 
                match p.PropertyType.FormatAndName with
                | Some (ty,na) -> p.Name, Primitive(ty,na)
                | None -> 
                    p.Name, Ref(p.PropertyType.Describes())
          ) |> dict
        {Id=this.Name; Properties=props}


  let findInZip (zip:ZipInputStream) (f:ZipEntry -> bool) =
    let rec loop (ze:ZipEntry) =
      if isNull ze
      then None
      elif f ze
      then Some ze
      else loop (zip.GetNextEntry())
    loop (zip.GetNextEntry())
  let combineUrls (u1:string) (u2:string) =
    let sp = if u2.StartsWith "/" then u2.Substring 1 else u2
    u1 + sp

  let swaggerUiWebPart (swPath:string) (swJsonPath:string) = 
    let wp : WebPart = 
      fun ctx -> 
        let p = 
          match ctx.request.url.AbsolutePath.Substring(swPath.Length) with
          | v when String.IsNullOrWhiteSpace v -> "index.html"
          | v -> v
        let assembly = System.Reflection.Assembly.GetExecutingAssembly()
        use fs = assembly.GetManifestResourceStream "swagger-ui.zip"
        use zip = new ZipInputStream(fs)
        match findInZip zip (fun e -> e.Name = p) with
        | Some _ -> 
          let headers = 
            match defaultMimeTypesMap (System.IO.Path.GetExtension p) with
            | Some mimetype -> ("Content-Type", mimetype.name) :: ctx.response.headers
            | None -> ctx.response.headers
          let bytes() = 
            use mem = new MemoryStream()
            zip.CopyTo mem
            mem.Position <- 0L
            if p = "index.html" then
              use r = new StreamReader(mem)
              r.ReadToEnd()
                .Replace("http://petstore.swagger.io/v2/swagger.json", (combineUrls "http://localhost:8083/" swJsonPath))
              |> Text.Encoding.UTF8.GetBytes
            else
              mem.ToArray()
          { ctx 
              with 
                response = 
                  { ctx.response 
                      with 
                        status = Suave.Http.HTTP_200
                        content = Bytes (bytes())
                        headers = headers
                  }
          } |> succeed
        | None -> 
            ctx |> NOT_FOUND "Ressource not found"
    pathStarts swPath >=> wp

  type DocBuildState =
    { SwaggerJsonPath:string
      SwaggerUiPath:string
      Description:ApiDescription
      Routes:WebPartDocumentation list
      Current:WebPartDocumentation
      Models:Type list
      Id:Guid }
    static member Of w =
      { Routes=[]
        Models=[]
        Current=WebPartDocumentation.Of w
        Id=Guid.NewGuid()
        Description=ApiDescription.Empty
        SwaggerJsonPath="/swagger/v2/swagger.json"
        SwaggerUiPath="/swagger/v2/ui/" }
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
                              { Name=a.Name
                                Type=a.TypeName
                                In=a.In.ToString()
                                Required=a.Required })
                      let pa = 
                        { Summary=p.Summary
                          Description=p.Description
                          OperationId=p.OperationId
                          Consumes=[]
                          Parameters=par
                          Responses=p.Responses }
                      yield p.Verb, pa
                  } |> dict
                (k,vs)
            ) |> Seq.toList |> dict

        { Definitions=definitions
          BasePath=""; Host=""; Schemes=["http"]
          Paths=paths
          Info=__.Description
          Swagger="2.0" }
    member __.App 
      with get () =
        let swaggerWebPart = 
          path __.SwaggerJsonPath
            >=> OK (__.Documentation.ToJson()) // JSON __.Documentation
            >=> Writers.setMimeType "application/json; charset=utf-8"
            >=> Writers.addHeader "Access-Control-Allow-Origin" "*"
        let uiWebpart = swaggerUiWebPart __.SwaggerUiPath __.SwaggerJsonPath
        choose ((__.Routes |> List.map (fun r -> r.WebPart)) @ [swaggerWebPart;uiWebpart; __.Current.WebPart])

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

