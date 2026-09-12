using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using SimpleJson;

Console.WriteLine("=== 1. Basic serialization ===");
Console.WriteLine(JsonSerializer.Serialize("hello"));
Console.WriteLine(JsonSerializer.Serialize(42));
Console.WriteLine(JsonSerializer.Serialize(3.14));
Console.WriteLine(JsonSerializer.Serialize(true));
Console.WriteLine(JsonSerializer.Serialize((object?)null));

Console.WriteLine();
Console.WriteLine("=== 2. Object serialization ===");
var user = new User
{
    Id = 1,
    Name = "John",
    IsActive = true,
    CreatedAt = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
    Role = UserRole.Admin,
    Tags = new List<string> { "admin", "beta" },
    Manager = null
};
Console.WriteLine(JsonSerializer.Serialize(user));

Console.WriteLine();
Console.WriteLine("=== 3. Nested objects ===");
var company = new Company { Name = "Acme Corp", Owner = user };
Console.WriteLine(JsonSerializer.Serialize(company));

Console.WriteLine();
Console.WriteLine("=== 4/5. Collections and dictionaries ===");
Console.WriteLine(JsonSerializer.Serialize(new[] { 1, 2, 3 }));
Console.WriteLine(JsonSerializer.Serialize(new List<User> { user }));
var dict = new Dictionary<string, object> { { "count", 3 }, { "label", "widgets" }, { "active", true } };
Console.WriteLine(JsonSerializer.Serialize(dict));

Console.WriteLine();
Console.WriteLine("=== 6/7. Deserialization ===");
string json = JsonSerializer.Serialize(user);
var roundTripped = JsonSerializer.Deserialize<User>(json);
Console.WriteLine($"Round-tripped: Name={roundTripped!.Name}, Role={roundTripped.Role}, CreatedAt={roundTripped.CreatedAt:o}");

var weaklyTyped = JsonSerializer.Deserialize<Dictionary<string, object>>("{\"a\":1,\"b\":[1,2,3],\"c\":{\"nested\":true}}");
Console.WriteLine($"Weakly-typed dictionary has {weaklyTyped!.Count} top-level keys.");

Console.WriteLine();
Console.WriteLine("=== 8. Special types (DateTime, Guid, enum, nullable) ===");
var special = new SpecialTypes
{
    When = DateTime.UtcNow,
    UniqueId = Guid.NewGuid(),
    Role = UserRole.Member,
    OptionalCount = null
};
string specialJson = JsonSerializer.Serialize(special);
Console.WriteLine(specialJson);
var specialBack = JsonSerializer.Deserialize<SpecialTypes>(specialJson);
Console.WriteLine($"OptionalCount after round-trip (was null): {specialBack!.OptionalCount?.ToString() ?? "null"}");

Console.WriteLine();
Console.WriteLine("=== 9. Error handling ===");
try
{
    JsonSerializer.Deserialize<User>("{ \"Id\": 1, ");
}
catch (JsonParseException ex)
{
    Console.WriteLine($"JsonParseException: {ex.Message}");
}

try
{
    JsonSerializer.Deserialize<User>("{ \"Id\": \"not-a-number\" }");
}
catch (JsonDeserializationException ex)
{
    Console.WriteLine($"JsonDeserializationException: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== 10. Circular references ===");
var alice = new Person { Name = "Alice" };
var bob = new Person { Name = "Bob" };
alice.Friend = bob;
bob.Friend = alice; // cycle through a chain, not just a direct self-reference
try
{
    JsonSerializer.Serialize(alice);
}
catch (JsonSerializationException ex)
{
    Console.WriteLine($"JsonSerializationException: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("=== Performance benchmark ===");
RunBenchmark();

static void RunBenchmark()
{
    const int iterations = 200_000;

    var sample = new User
    {
        Id = 42,
        Name = "Benchmark User",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        Role = UserRole.Member,
        Tags = new List<string> { "x", "y", "z" }
    };

    // Warm up the JIT for both paths before timing anything.
    NaiveSerialize(sample);
    JsonSerializer.Serialize(sample);

    var swNaive = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++) NaiveSerialize(sample);
    swNaive.Stop();

    var swCached = Stopwatch.StartNew();
    for (int i = 0; i < iterations; i++) JsonSerializer.Serialize(sample);
    swCached.Stop();

    Console.WriteLine($"Iterations:                                     {iterations:N0}");
    Console.WriteLine($"Naive (Type.GetProperties() on every call):     {swNaive.ElapsedMilliseconds,6} ms");
    Console.WriteLine($"SimpleJson (TypeMetadataCache, this library):   {swCached.ElapsedMilliseconds,6} ms");
    double speedup = swNaive.Elapsed.TotalMilliseconds / Math.Max(swCached.Elapsed.TotalMilliseconds, 0.001);
    Console.WriteLine($"Speedup:                                        {speedup:F2}x");
    Console.WriteLine();
    Console.WriteLine("(See PERFORMANCE.md for methodology and how to reproduce this.)");
}

// A deliberately naive reflection-based serializer used only as the "before"
// side of the benchmark: it calls Type.GetProperties() from scratch on every
// invocation, which is exactly the cost TypeMetadataCache exists to remove.
static string NaiveSerialize(object value)
{
    var sb = new StringBuilder();
    sb.Append('{');
    var props = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
    for (int i = 0; i < props.Length; i++)
    {
        if (i > 0) sb.Append(',');
        sb.Append('"').Append(props[i].Name).Append("\":");
        var v = props[i].GetValue(value);
        sb.Append(v is null ? "null" : "\"" + v + "\"");
    }
    sb.Append('}');
    return sb.ToString();
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public UserRole Role { get; set; }
    public List<string> Tags { get; set; } = new();
    public User? Manager { get; set; }
}

public enum UserRole { Member, Admin }

public class Company
{
    public string Name { get; set; } = "";
    public User Owner { get; set; } = new();
}

public class SpecialTypes
{
    public DateTime When { get; set; }
    public Guid UniqueId { get; set; }
    public UserRole Role { get; set; }
    public int? OptionalCount { get; set; }
}

public class Person
{
    public string Name { get; set; } = "";
    public Person? Friend { get; set; }
}
