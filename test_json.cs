using System;
using Newtonsoft.Json.Linq;

class TestJson 
{
    static void Main()
    {
        var jsonWithSingleQuotes = @"
        {
            'id': 'test',
            'states': {
                'idle': {
                    'on': {
                        'START': 'running'
                    }
                }
            }
        }";
        
        var jsonWithDoubleQuotes = jsonWithSingleQuotes.Replace("'", "\"");
        
        Console.WriteLine("Original with single quotes:");
        Console.WriteLine(jsonWithSingleQuotes);
        Console.WriteLine("\nConverted with double quotes:");
        Console.WriteLine(jsonWithDoubleQuotes);
        
        try 
        {
            var parsed = JObject.Parse(jsonWithDoubleQuotes);
            Console.WriteLine("\nParsing successful!");
            Console.WriteLine("States: " + parsed["states"]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nParsing failed: " + ex.Message);
        }
    }
}