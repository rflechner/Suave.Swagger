module Suave.RestFullKit.Tests

open Suave.RestFullKit
open NUnit.Framework
open System
open Suave.RestFullKit.Rest
open Suave.RestFullKit.FunnyDsl
open Suave.RestFullKit.Swagger
open Suave.RestFullKit.Serialization

type Customer = {
  Name:string
  FirstName:string
  Birth:DateTime
}
and Brand = {
  Name:string
  CountryName:string
}
and Car = {
  ModelName:string
  Brand:Brand
}
and Rent = Car * Customer

[<Test>]
let ``When describing a very simple schema`` () =
  let desc = typeof<Customer>.Describes()
  let expected = 
    { Id="Customer"
      Properties = 
        [ ("Name", Primitive("string","string"))
          ("FirstName", Primitive("string","string"))
          ("Birth", Primitive("string","date-time")) ] |> dict
    }
  Assert.AreEqual(desc.Id, expected.Id)
  Assert.AreEqual(desc.Properties.Item "Name", expected.Properties.Item "Name")
  Assert.AreEqual(desc.Properties.Item "FirstName", expected.Properties.Item "FirstName")
  Assert.AreEqual(desc.Properties.Item "Birth", expected.Properties.Item "Birth")
  Assert.AreEqual(desc.Properties.Count, expected.Properties.Count)

[<Test>]
let ``When describing a more complex schema`` () =
  let desc = typeof<Car>.Describes()
  let expBrandDef = 
    { Id="Brand"
      Properties = 
        [ ("Name", Primitive("string","string"))
          ("CountryName", Primitive("string","string")) ] |> dict
    }
  let expected = 
    { Id="Car"
      Properties = 
        [ ("ModelName", Primitive("string","string"))
          ("Brand", Ref(expBrandDef)) ] |> dict
    }

  Assert.AreEqual(desc.Id, expected.Id)
  Assert.AreEqual(desc.Properties.Item "ModelName", expected.Properties.Item "ModelName")
  let brandDef = 
    desc.Properties.Item "Brand"
    |> fun (Ref d) -> d
  Assert.AreEqual(brandDef.Id, expBrandDef.Id)
  Assert.AreEqual(brandDef.Properties.Item "Name", expBrandDef.Properties.Item "Name")
  Assert.AreEqual(brandDef.Properties.Item "CountryName", expBrandDef.Properties.Item "CountryName")

[<Test>]
let ``When serializing a simple schema`` () =
  ()
