using SemiFlow.Converter;
using System.Text.Json;

namespace SemiFlow.CLI;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("SemiFlow to XState Converter");
        Console.WriteLine("============================\n");

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: SemiFlow.CLI <input-file.json> [output-file.json]");
            Console.WriteLine("\nExample:");
            Console.WriteLine("  SemiFlow.CLI example_wafer_flow.json output_xstate.json");
            return;
        }

        var inputPath = args[0];
        var outputPath = args.Length > 1 ? args[1] : "output_xstate.json";

        try
        {
            Console.WriteLine($"Reading SemiFlow from: {inputPath}");

            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"Error: Input file not found: {inputPath}");
                return;
            }

            var converter = new SemiFlowToXStateConverter();

            Console.WriteLine("Converting to XState...");
            converter.ConvertFile(inputPath, outputPath);

            Console.WriteLine($"✓ Successfully converted to: {outputPath}\n");

            // Display summary
            var outputJson = File.ReadAllText(outputPath);
            var xstate = JsonSerializer.Deserialize<JsonDocument>(outputJson);

            if (xstate != null)
            {
                Console.WriteLine("XState Machine Summary:");
                Console.WriteLine($"  ID: {xstate.RootElement.GetProperty("id").GetString()}");

                if (xstate.RootElement.TryGetProperty("type", out var typeProperty))
                {
                    Console.WriteLine($"  Type: {typeProperty.GetString()}");
                }

                if (xstate.RootElement.TryGetProperty("states", out var statesProperty))
                {
                    var stateCount = CountStates(statesProperty);
                    Console.WriteLine($"  Total States: {stateCount}");
                }

                Console.WriteLine($"\nOutput file: {Path.GetFullPath(outputPath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
        }
    }

    static int CountStates(JsonElement statesElement)
    {
        int count = 0;

        if (statesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var state in statesElement.EnumerateObject())
            {
                count++;

                if (state.Value.TryGetProperty("states", out var nestedStates))
                {
                    count += CountStates(nestedStates);
                }
            }
        }

        return count;
    }
}
