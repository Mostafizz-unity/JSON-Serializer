using System;
using System.Collections.Generic;
using SimpleJson;

// A tiny hand-rolled test runner. No xunit/NUnit dependency on purpose: this
// sandbox has no NuGet access, and the assignment itself only calls for
// "no third-party JSON library" - but avoiding any external package keeps
// the whole thing buildable offline with nothing but the .NET SDK.

int passed = 0;
int failed = 0;

void Check(string name, bool condition)
{
    if (condition) { passed++; Console.WriteLine($"[PASS] {name}"); }
    else { failed++; Console.WriteLine($"[FAIL] {name}"); }
}

void CheckThrows<TException>(string name, Action action) where TException : Exception
{
    try
    {
        action();
        failed++;
        Console.WriteLine($"[FAIL] {name} (expected {typeof(TException).Name}, but no exception was thrown)");
    }
    catch (TException)
    {
        passed++;
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"[FAIL] {name} (expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message})");
    }
}

Console.WriteLine("--- 1. Basic serialization ---");
Check("string", JsonSerializer.Serialize("hi") == "\"hi\"");
Check("int", JsonSerializer.Serialize(42) == "42");
Check("long", JsonSerializer.Serialize(9000000000L) == "9000000000");
Check("double", JsonSerializer.Serialize(3.5) == "3.5");
Check("decimal", JsonSerializer.Serialize(3.50m) == "3.50");
Check("bool true", JsonSerializer.Serialize(true) == "true");
Check("bool false", JsonSerializer.Serialize(false) == "false");
Check("null", JsonSerializer.Serialize((object?)null) == "null");
Check("string with escapes", JsonSerializer.Serialize("a\"b\\c\nd") == "\"a\\\"b\\\\c\\nd\"");

Console.WriteLine();
Console.WriteLine("--- 2. Object serialization ---");
var user = new TestUser { Id = 1, Name = "John", IsActive = true };
string userJson = JsonSerializer.Serialize(user);
Check("object contains Id", userJson.Contains("\"Id\":1"));
Check("object contains Name", userJson.Contains("\"Name\":\"John\""));
Check("object contains IsActive", userJson.Contains("\"IsActive\":true"));

Console.WriteLine();
Console.WriteLine("--- 3. Nested objects ---");
var company = new TestCompany { Name = "Acme", Owner = user };
string companyJson = JsonSerializer.Serialize(company);
Check("nested object embeds owner", companyJson.Contains("\"Owner\":{\"Id\":1"));
var companyBack = JsonSerializer.Deserialize<TestCompany>(companyJson);
Check("nested object round-trips", companyBack!.Owner.Name == "John");

Console.WriteLine();
Console.WriteLine("--- 4. Collections ---");
Check("List<int>", JsonSerializer.Serialize(new List<int> { 1, 2, 3 }) == "[1,2,3]");
Check("int[]", JsonSerializer.Serialize(new[] { 1, 2, 3 }) == "[1,2,3]");
Check("List<List<int>>", JsonSerializer.Serialize(new List<List<int>> { new() { 1 }, new() { 2, 3 } }) == "[[1],[2,3]]");
Check("List<TestUser>", JsonSerializer.Serialize(new List<TestUser> { user }).StartsWith("[{\"Id\":1"));

var listBack = JsonSerializer.Deserialize<List<int>>("[1,2,3]");
Check("deserialize List<int>", listBack!.Count == 3 && listBack[2] == 3);
var arrayBack = JsonSerializer.Deserialize<int[]>("[4,5]");
Check("deserialize int[]", arrayBack!.Length == 2 && arrayBack[1] == 5);

Console.WriteLine();
Console.WriteLine("--- 5. Dictionaries ---");
var dict = new Dictionary<string, object> { { "a", 1 }, { "b", "two" } };
Check("serialize Dictionary<string,object>", JsonSerializer.Serialize(dict) == "{\"a\":1,\"b\":\"two\"}");
var dictBack = JsonSerializer.Deserialize<Dictionary<string, object>>("{\"a\":1,\"b\":\"two\"}");
Check("deserialize dictionary value a", dictBack!["a"].ToString() == "1");
Check("deserialize dictionary value b", dictBack["b"].ToString() == "two");

Console.WriteLine();
Console.WriteLine("--- 6/7. Deserialization & reading JSON ---");
var deserializedUser = JsonSerializer.Deserialize<TestUser>(userJson);
Check("deserialize object Name", deserializedUser!.Name == "John");
Check("deserialize object Id", deserializedUser.Id == 1);
Check("deserialize object IsActive", deserializedUser.IsActive);
Check("unknown JSON fields are ignored", JsonSerializer.Deserialize<TestUser>("{\"Id\":9,\"Extra\":123}")!.Id == 9);
Check("case-insensitive property match", JsonSerializer.Deserialize<TestUser>("{\"id\":7,\"NAME\":\"x\"}")!.Id == 7);
var natural = JsonSerializer.Deserialize<object>("{\"a\":[1,2,{\"b\":true}]}");
Check("deserialize into object gives Dictionary", natural is Dictionary<string, object?>);

Console.WriteLine();
Console.WriteLine("--- 8. Special types ---");
var special = new TestSpecial
{
    When = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    UniqueId = Guid.NewGuid(),
    Role = TestRole.Admin,
    MaybeCount = 5
};
string specialJson = JsonSerializer.Serialize(special);
var specialBack = JsonSerializer.Deserialize<TestSpecial>(specialJson);
Check("DateTime round-trips", specialBack!.When == special.When);
Check("Guid round-trips", specialBack.UniqueId == special.UniqueId);
Check("enum round-trips as name", specialJson.Contains("\"Role\":\"Admin\"") && specialBack.Role == TestRole.Admin);
Check("nullable int (has value) round-trips", specialBack.MaybeCount == 5);

var specialNull = new TestSpecial { Role = TestRole.Member, MaybeCount = null };
var specialNullBack = JsonSerializer.Deserialize<TestSpecial>(JsonSerializer.Serialize(specialNull));
Check("nullable int (null) round-trips", specialNullBack!.MaybeCount == null);

Check("enum by numeric value also accepted", JsonSerializer.Deserialize<TestSpecial>("{\"Role\":1}")!.Role == TestRole.Admin);

Console.WriteLine();
Console.WriteLine("--- 9. Error handling ---");
CheckThrows<JsonParseException>("unterminated object throws parse exception",
    () => JsonSerializer.Deserialize<TestUser>("{ \"Id\": 1, "));
CheckThrows<JsonParseException>("trailing comma in array throws",
    () => JsonSerializer.Deserialize<List<int>>("[1,2,]"));
CheckThrows<JsonParseException>("bad literal throws",
    () => JsonSerializer.Deserialize<TestUser>("{ \"Id\": tru }"));
CheckThrows<JsonParseException>("trailing content after value throws",
    () => JsonSerializer.Deserialize<int>("1 2"));
CheckThrows<JsonDeserializationException>("string into int throws type mismatch",
    () => JsonSerializer.Deserialize<TestUser>("{\"Id\":\"nope\"}"));
CheckThrows<JsonDeserializationException>("null into non-nullable value type throws",
    () => JsonSerializer.Deserialize<int>("null"));
CheckThrows<JsonDeserializationException>("invalid enum name throws",
    () => JsonSerializer.Deserialize<TestSpecial>("{\"Role\":\"NotARole\"}"));

Console.WriteLine();
Console.WriteLine("--- 10. Circular references ---");
var a = new TestNode { Name = "A" };
a.Next = a;
CheckThrows<JsonSerializationException>("direct self-reference throws on serialize",
    () => JsonSerializer.Serialize(a));

var n1 = new TestNode { Name = "1" };
var n2 = new TestNode { Name = "2" };
n1.Next = n2;
n2.Next = n1;
CheckThrows<JsonSerializationException>("indirect cycle (A -> B -> A) throws on serialize",
    () => JsonSerializer.Serialize(n1));

var n3 = new TestNode { Name = "3" };
var n4 = new TestNode { Name = "4" };
n3.Next = n4; // shared reference but NOT a cycle - must serialize fine
var shared = new List<TestNode> { n3, n4 };
Check("repeated (non-cyclic) reference does not throw", JsonSerializer.Serialize(shared).Length > 0);

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
Environment.Exit(failed == 0 ? 0 : 1);

public class TestUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public class TestCompany
{
    public string Name { get; set; } = "";
    public TestUser Owner { get; set; } = new();
}

public enum TestRole { Member, Admin }

public class TestSpecial
{
    public DateTime When { get; set; }
    public Guid UniqueId { get; set; }
    public TestRole Role { get; set; }
    public int? MaybeCount { get; set; }
}

public class TestNode
{
    public string Name { get; set; } = "";
    public TestNode? Next { get; set; }
}
