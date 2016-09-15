namespace Suave.Swagger

module FunnyDsl =

  open System
  open Suave
  open Suave.Operators
  open Suave.Filters
  open Swagger

  let is (x:'a) = x
  
  let descriptionOf (route:DocBuildState) (f:string -> string) x =
    route.Documents(fun doc -> { doc with Description = (f x) })
  
  let Of (x:'a) = x
  
  let consumes (mimetype:string) (route:DocBuildState) =
    { route
        with 
          Current = 
            { route.Current 
                with 
                  Description =
                    { route.Current.Description
                        with
                          Consumes = mimetype :: route.Current.Description.Consumes
                    }
            }
    }

  let produces (mimetype:string) (route:DocBuildState) =
    { route
        with 
          Current = 
            { route.Current 
                with 
                  Description =
                    { route.Current.Description
                        with
                          Produces = mimetype :: route.Current.Description.Produces
                    }
            }
    }

//  let supportsJsonAndXml route =
//    route
//    |> produces "application/json" 
//    |> produces "application/xml"
//    |> consumes "application/json"
//    |> consumes "application/xml"

  let supportsJsonAndXml =
    produces "application/json" 
    >> produces "application/xml"
    >> consumes "application/json"
    >> consumes "application/xml"

  let addResponse (statusCode:int) (desc:string) (modelType:Type option) (route:DocBuildState) =
    let s,rs = 
      match modelType with
      | Some ty -> 
        let v1 = { route with Models=(ty :: route.Models) }
        let v2 = { Description=desc; Schema=Some(ty.Describes()) }
        v1,v2
      | None -> 
        route, { Description=desc; Schema=None }
    let rsd = 
      s.Current.Description.Responses
      |> Seq.map (fun kv -> kv.Key,kv.Value)
      |> Seq.toList
    { s 
        with 
          Current = 
            { s.Current 
                with 
                  Description =
                    { s.Current.Description
                        with
                          Responses = (statusCode, rs) :: rsd |> List.distinctBy(fun (k,_) -> k) |> dict
                    }
            }
    }
    
  let description _ (route:DocBuildState) (f:string -> string) x =
    route.Documents(fun doc -> { doc with Description = (f x) })
    
  let urlTemplate _ (route:DocBuildState) (f:string -> string) x =
    route.Documents(fun doc -> { doc with Template = (f x) })
    
  let private documentVerb w v = 
    let wv = 
      match v with
      | Get -> GET | Put -> PUT | Post -> POST 
      | Delete -> DELETE | Options -> OPTIONS 
      | Head -> HEAD | Patch -> PATCH
    (DocBuildState.Of (wv >=> w)).Documents(fun doc -> { doc with Verb=v })
  let getOf w = documentVerb w Get
  let putOf w = documentVerb w Put
  let postOf w = documentVerb w Post
  let deleteOf w = documentVerb w Delete
  let OptionsOf w = documentVerb w Options
  let headOf w = documentVerb w Head
  let patchOf w = documentVerb w Patch
    
  let parameter (name:string) _ (route:DocBuildState) (f:ParamDescriptor->ParamDescriptor) =
    let p = name |> ParamDescriptor.Named |> f
    route.Documents(fun doc -> { doc with Params = (p :: doc.Params) })

  let getting (d:DocBuildState) = 
    { d with Current = { d.Current with WebPart=(GET >=> d.Current.WebPart) } }
      .Documents(fun doc -> { doc with Verb=Get })
  let posting (d:DocBuildState) = 
    { d with Current = { d.Current with WebPart=(POST >=> d.Current.WebPart) } }
      .Documents(fun doc -> { doc with Verb=Post })
  let putting (d:DocBuildState) = 
    { d with Current = { d.Current with WebPart=(PUT >=> d.Current.WebPart) } }
      .Documents(fun doc -> { doc with Verb=Put })
  let deleting (d:DocBuildState) = 
    { d with Current = { d.Current with WebPart=(DELETE >=> d.Current.WebPart) } }
      .Documents(fun doc -> { doc with Verb=Delete })

  let simpleUrl (p:string) =
    (DocBuildState.Of <| path p).Documents(fun doc -> { doc with Template = p})

  let urlFormat (pf : PrintfFormat<_,_,_,_,'t>) f =
    let ty = typeof<'t>
    let parts = FormatParser.Parse pf.Value
    let _,tmpl =
      parts
      |> List.fold (
          fun ((i,acc):(int*string)) (p:FormatPart) ->
            match p with
            | Constant c -> i, acc + c
            | Parsed _ -> (i+1), sprintf "%s{param%d}" acc i
          ) (0,"")

    let doc = DocBuildState.Of <| pathScan pf f
    let pname (pt:Type) i =
      let name = sprintf "param%d" i
      let tn = match pt.FormatAndName with | Some (_,v) -> v | None -> "object"
      name,tn

    ( match ty with
      | _ when ty.IsPrimitive -> 
        let name,_ = pname ty 0
        let p = { (ParamDescriptor.Named name) with Type=(Some ty); In=Path }
        doc.Documents(fun d -> { d with Params = (p :: d.Params) })
      | _ ->
        ty.GetGenericArguments()
          |> Seq.mapi (
              fun i pt -> 
                let name,_ = pname pt i
                let t = Some pt
                { (ParamDescriptor.Named name) with Type=t; In=Path }
              ) 
          |> Seq.rev
          |> Seq.fold (
              fun (state:DocBuildState) p -> 
                state.Documents(fun d -> { d with Params = (p :: d.Params) })
            ) doc
    )
    |> fun d -> 
        d.Documents(fun doc -> { doc with Template = tmpl})
      

  let thenReturns w (d:DocBuildState) =
    { d with Current = { d.Current with WebPart=(d.Current.WebPart >=> w) } }
