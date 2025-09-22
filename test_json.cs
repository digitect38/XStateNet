using System;
using Newtonsoft.Json.Linq;

class TestJson 
{
    static void Main()
    {
        var jsonWithSingleQuotes = @"
        {
            id: 'test',
            states: {
                idle: {
                    on: {
                        START: 'running'
                    }
                }
            }
        }";
        
        Console.WriteLine("JSON with cleaned format:");
        Console.WriteLine(jsonWithSingleQuotes);

        try
        {
            // For JSON parsing, we still need double quotes around values
            var jsonForParsing = jsonWithSingleQuotes.Replace("'", "\"");
            var parsed = JObject.Parse(jsonForParsing);
            Console.WriteLine("\nParsing successful!");
            Console.WriteLine("States: " + parsed["states"]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nParsing failed: " + ex.Message);
        }
    }
}