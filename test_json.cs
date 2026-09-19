using System;
using System.Text.Json;

class Program
{
    static void Main()
    {
        var json = JsonSerializer.Serialize(new { candidate_id = "test" });
        Console.WriteLine(json);
    }
}
