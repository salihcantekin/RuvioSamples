using System.Text.Json.Serialization;

namespace CacheStampede.Api;

/// <summary>The value that is cached. <c>BuiltBy</c> shows which API instance paid for the database call.</summary>
public sealed record Product(int Id, string Name, decimal Price, string BuiltBy);

[JsonSerializable(typeof(Product))]
public sealed partial class ProductJson : JsonSerializerContext;
